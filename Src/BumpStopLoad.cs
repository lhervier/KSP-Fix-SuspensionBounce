using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using ModuleWheels;
using UnityEngine;
using VehiclePhysics;

namespace com.github.lhervier.ksp.fix.suspensionbounce
{
    /// <summary>
    /// Holds the suspensions at the end of their travel itself, and gives the tyres the weight they carry there.
    /// <para>
    /// Vehicle Physics Pro is written for a car made of one rigid body; in KSP, every wheel is a vehicle of its own, on
    /// its own part, joined to the others. Once a suspension reaches the end of its travel, PhysX holds the rest of the
    /// weight with a hard limit that only exists at the steps that start with the wheel already past the end, and puts
    /// it back exactly at the end: the next step has no limit, the vessel falls for a step, then the limit pushes twice
    /// the weight. The vessel shakes on its end-stops, the tyres slip at each beat, and the rover creeps down slopes.
    /// VPP, besides, reads the load of each tyre in the force the PhysX WheelCollider reports (WheelHit.force), the
    /// spring and damper only: it never sees what the limit holds.
    /// </para>
    /// <para>
    /// At every physics step, while a wheel of a vessel nears the end of its travel, the fix takes the vessel as one
    /// rigid body, as VPP expects, and finds the least forces, none pulling, that keep every wheel on the ground from
    /// going past the end of its travel by the end of the coming step, all wheels at once. It applies them to the
    /// wheels' bodies, which leaves PhysX's limit nothing to correct, and adds each to the force VPP reads for the load
    /// of its tyre.
    /// </para>
    /// </summary>
    public static class BumpStopLoad
    {
        // Knobs, public so that they can be changed in game (KSP-Util-MCPServer set_member).

        /// <summary>Whether the fix runs at all (settings.cfg, bumpStopLoad).</summary>
        public static bool Enabled = true;

        /// <summary>Whether the fix applies the end-stop forces it finds to the wheels.</summary>
        public static bool Hold = true;

        /// <summary>Whether the fix adds the end-stop forces it applies to the load VPP reads for the tyres.</summary>
        public static bool Inject = true;

        /// <summary>
        /// Weight of the size of the end-stop forces in their solve, relative to the contacts' scale: four wheels give
        /// more forces than the three equations (one force, two torques) need, and this picks the evenest.
        /// </summary>
        public static float Regularization = 0.1f;

        /// <summary>Most Gauss-Seidel sweeps of each pass of the end-stop solve.</summary>
        public static int Iterations = 30;

        /// <summary>
        /// A pass of the end-stop solve ends once no force changed by more than this over a sweep (kN).
        /// </summary>
        public static float Tolerance = 1e-4f;

        /// <summary>
        /// Passes of the end-stop solve: each makes up the shortfall the regularisation leaves, by a factor of about
        /// the regularisation.
        /// </summary>
        public static int Passes = 4;

        /// <summary>What the fix found for one wheel at the last physics step, kept for reading in game.</summary>
        public class Probe
        {
            public string partName;
            public uint persistentId;
            public float compression;
            /// <summary>Force PhysX reports for the spring and damper (kN).</summary>
            public float springForce;
            /// <summary>Normal force the fix applies to the wheel for the coming physics step (kN).</summary>
            public float held;
            /// <summary>The tyre load as VPP would have computed it without the fix (kN).</summary>
            public float stockDownforce;

            internal VehicleBase.WheelState state;
            internal Rigidbody body;
            internal Part part;
            /// <summary>When <see cref="held"/> was found, and the force as applied, for the check of the next step.</summary>
            internal float heldTime = -1f;
            internal Vector3 heldForce;
            internal Vector3 heldPoint;
            /// <summary>Force added to the one VPP reads at this step (kN).</summary>
            internal float injected;
            internal float lastTime = -1f;
            internal Vector3 lastTyreForce;
            internal Vector3 lastTyrePoint;
            /// <summary>The contact PhysX reports for the last step, read before VPP reads it.</summary>
            internal WheelHit hit;
            internal bool grounded;
            /// <summary>The axis of the suspension, upwards, read with the contact.</summary>
            internal Vector3 axis;
        }

        /// <summary>What the fix found for one vessel at the last physics step, kept for reading in game.</summary>
        public class Balance
        {
            public string vesselName;
            /// <summary>
            /// Normal force PhysX's own limits still pushed at the last step, along the mean normal: the vessel's change
            /// of momentum that the known forces and the fix's forces do not explain (kN). Near zero while the fix holds
            /// the end-stops; a hard landing shows here.
            /// </summary>
            public float endStopNormal;
            /// <summary>Sum of the forces the fix applies to the wheels for the coming step (kN).</summary>
            public float heldNormal;
            /// <summary>How many wheels the fix holds for the coming step.</summary>
            public int holding;
            public float totalMass;
            /// <summary>Whether the vessel was followed at the last step.</summary>
            public bool watching;

            internal Vessel vessel;
            internal float time = -1f;
            internal readonly List<Probe> wheels = new List<Probe>();
            /// <summary>The bodies followed, and their velocity at the end of the step before (same index).</summary>
            internal readonly List<Rigidbody> bodies = new List<Rigidbody>();
            internal readonly List<Vector3> velocities = new List<Vector3>();
            internal float statesTime = -1f;
            /// <summary>The vessel's acceleration KSP applied at the last two steps, the latest second.</summary>
            internal float accelerationTime0 = -1f, accelerationTime1 = -1f;
            internal Vector3 acceleration0, acceleration1;
        }

        /// <summary>Forces a part's modules handed its body for one physics step (FlightIntegrator.Integrate).</summary>
        private struct Applied
        {
            public float time;
            public Vector3 force;
            /// <summary>Torque about the body's centre of mass.</summary>
            public Vector3 torque;
        }

        public static readonly List<Probe> Probes = new List<Probe>();
        public static readonly List<Balance> Balances = new List<Balance>();

        private static readonly Dictionary<VehicleBase.WheelState, Probe> probes =
            new Dictionary<VehicleBase.WheelState, Probe>();
        private static readonly Dictionary<Vessel, Balance> balances = new Dictionary<Vessel, Balance>();

        /// <summary>
        /// For each body whose part's modules handed it a force or a torque, what they handed at the last two physics
        /// steps, the latest second. A body missing here, or without an entry for a step, got none.
        /// </summary>
        private static readonly Dictionary<Rigidbody, Applied[]> applied = new Dictionary<Rigidbody, Applied[]>();

        private static readonly MethodInfo CalculateSuspensionForces =
            AccessTools.DeclaredMethod(typeof(VehicleBase), "CalculateSuspensionForces");
        private static readonly MethodInfo CalculateDownforce =
            AccessTools.DeclaredMethod(typeof(VehicleBase), "CalculateDownforce");
        private static readonly MethodInfo ApplyTireForce =
            AccessTools.DeclaredMethod(typeof(VehicleBase), "ApplyTireForce");
        private static readonly MethodInfo DamageFixedUpdate =
            AccessTools.DeclaredMethod(typeof(ModuleWheelDamage), "FixedUpdate");
        private static readonly MethodInfo Integrate =
            AccessTools.DeclaredMethod(typeof(FlightIntegrator), "Integrate", new[] { typeof(Part) });

        private static readonly AccessTools.FieldRef<VehicleBase, Rigidbody> VehicleBody =
            AccessTools.FieldRefAccess<VehicleBase, Rigidbody>("m_rigidbody");
        private static readonly AccessTools.FieldRef<ModuleWheelSubmodule, KSPWheelController> Wheel =
            AccessTools.FieldRefAccess<ModuleWheelSubmodule, KSPWheelController>("wheel");

        private static void IntegratePrefix(Part part)
        {
            IntegratePrefixBody(part);
        }

        private static void CalculateSuspensionForcesPostfix(VehicleBase __instance, VehicleBase.WheelState wheel)
        {
            CalculateSuspensionForcesPostfixBody(__instance, wheel);
        }

        private static void CalculateDownforcePostfix(VehicleBase __instance, VehicleBase.WheelState wheel)
        {
            CalculateDownforcePostfixBody(__instance, wheel);
        }

        private static void ApplyTireForcePostfix(VehicleBase.WheelState wheel)
        {
            ApplyTireForcePostfixBody(wheel);
        }

        private static void DamageFixedUpdatePrefix(ModuleWheelDamage __instance, out float __state)
        {
            DamageFixedUpdatePrefixBody(__instance, out __state);
        }

        private static void DamageFixedUpdatePostfix(ModuleWheelDamage __instance, float __state)
        {
            DamageFixedUpdatePostfixBody(__instance, __state);
        }

        /// <summary>Patches VPP's suspension step, the wheel damage that reads the tyre load, and KSP's force step.</summary>
        /// <exception cref="MissingMemberException">A member the fix reads is missing from the game.</exception>
        public static void Install(Harmony harmony)
        {
            if (CalculateSuspensionForces == null)
            {
                throw new MissingMemberException("VehicleBase.CalculateSuspensionForces");
            }
            if (CalculateDownforce == null)
            {
                throw new MissingMemberException("VehicleBase.CalculateDownforce");
            }
            if (DamageFixedUpdate == null)
            {
                throw new MissingMemberException("ModuleWheelDamage.FixedUpdate");
            }
            if (Integrate == null)
            {
                throw new MissingMemberException("FlightIntegrator.Integrate");
            }
            if (ApplyTireForce == null)
            {
                throw new MissingMemberException("VehicleBase.ApplyTireForce");
            }
            harmony.Patch(Integrate, prefix: new HarmonyMethod(typeof(BumpStopLoad), nameof(IntegratePrefix)));
            harmony.Patch(CalculateSuspensionForces,
                postfix: new HarmonyMethod(typeof(BumpStopLoad), nameof(CalculateSuspensionForcesPostfix)));
            harmony.Patch(CalculateDownforce,
                postfix: new HarmonyMethod(typeof(BumpStopLoad), nameof(CalculateDownforcePostfix)));
            harmony.Patch(ApplyTireForce,
                postfix: new HarmonyMethod(typeof(BumpStopLoad), nameof(ApplyTireForcePostfix)));
            harmony.Patch(DamageFixedUpdate,
                prefix: new HarmonyMethod(typeof(BumpStopLoad), nameof(DamageFixedUpdatePrefix)),
                postfix: new HarmonyMethod(typeof(BumpStopLoad), nameof(DamageFixedUpdatePostfix)));
            GameEvents.onGameSceneLoadRequested.Add(scene => Clear());
        }

        private static void Clear()
        {
            Probes.Clear();
            probes.Clear();
            Balances.Clear();
            balances.Clear();
            applied.Clear();
        }

        /// <summary>
        /// Records what KSP is about to hand the part's bodies for the coming physics step: the vessel's acceleration
        /// (gravity and the frame's), once per vessel, and the forces and torques the part's modules gathered, for the
        /// parts that have any.
        /// </summary>
        private static void IntegratePrefixBody(Part part)
        {
            if (!Enabled || part == null)
            {
                return;
            }
            Vessel vessel = part.vessel;
            if (vessel == null)
            {
                return;
            }
            Balance balance;
            if (part == vessel.rootPart && balances.TryGetValue(vessel, out balance))
            {
                float now = Time.fixedTime;
                if (balance.accelerationTime1 != now)
                {
                    balance.accelerationTime0 = balance.accelerationTime1;
                    balance.acceleration0 = balance.acceleration1;
                }
                balance.accelerationTime1 = now;
                balance.acceleration1 = vessel.precalc.integrationAccel;
            }
            if (part.rb == null || (part.forces.Count == 0 && part.force.sqrMagnitude == 0.0 && part.torque.sqrMagnitude == 0.0))
            {
                return;
            }
            Vector3 force = part.force;
            Vector3 torque = part.torque;
            if (part.forces.Count > 0)
            {
                Vector3 centre = part.rb.worldCenterOfMass;
                for (int i = 0; i < part.forces.Count; i++)
                {
                    Vector3 f = part.forces[i].force;
                    force += f;
                    torque += Vector3.Cross((Vector3)part.forces[i].pos - centre, f);
                }
            }
            Applied[] history;
            if (!applied.TryGetValue(part.rb, out history))
            {
                history = new Applied[2];
                applied[part.rb] = history;
            }
            float time = Time.fixedTime;
            if (history[1].time != time)
            {
                history[0] = history[1];
            }
            history[1] = new Applied { time = time, force = force, torque = torque };
        }

        /// <summary>
        /// Returns the force and torque the part's modules handed <paramref name="body"/> for the physics step that
        /// started at <paramref name="time"/>; zero when they handed none.
        /// </summary>
        private static Applied AppliedAt(Rigidbody body, float time)
        {
            Applied[] history;
            if (applied.TryGetValue(body, out history))
            {
                for (int i = 1; i >= 0; i--)
                {
                    if (Mathf.Abs(history[i].time - time) < Time.fixedDeltaTime * 0.25f)
                    {
                        return history[i];
                    }
                }
            }
            return default(Applied);
        }

        /// <summary>
        /// Holds the end-stops of the wheel's vessel for the coming physics step (once per vessel and step, at its first
        /// wheel), and adds to the force VPP is about to read for the tyre load of <paramref name="wheel"/> the end-stop
        /// force the fix applies to it. Called by VPP at every physics step, once the spring and damper force of the last
        /// step is read, before the tyre load is computed from it.
        /// </summary>
        private static void CalculateSuspensionForcesPostfixBody(VehicleBase vehicle, VehicleBase.WheelState wheel)
        {
            if (!Enabled || !HighLogic.LoadedSceneIsFlight || wheel == null || wheel.wheelCol == null)
            {
                return;
            }
            Probe probe = ProbeOf(vehicle, wheel);
            if (probe == null)
            {
                return;
            }
            probe.injected = 0f;
            Vessel vessel = probe.part.vessel;
            if (vessel != null)
            {
                Solve(vessel);
            }
            float now = Time.fixedTime;
            bool fresh = Mathf.Abs(probe.heldTime - now) < Time.fixedDeltaTime * 0.25f;
            if (Inject && wheel.grounded && fresh && probe.held > 0f)
            {
                probe.injected = probe.held;
                wheel.hit.force += probe.injected;
            }
            probe.lastTime = now;
        }

        /// <summary>
        /// Keeps the tyre load VPP would have computed without the fix, for the wheel damage, which reads its jumps as
        /// impacts.
        /// </summary>
        private static void CalculateDownforcePostfixBody(VehicleBase vehicle, VehicleBase.WheelState wheel)
        {
            Probe probe;
            if (wheel == null || !probes.TryGetValue(wheel, out probe))
            {
                return;
            }
            if (probe.injected <= 0f || !wheel.grounded)
            {
                probe.stockDownforce = wheel.downforce;
                return;
            }
            float downforce = wheel.hit.force - probe.injected + wheel.wheelCol.runtimeExtraDownforce;
            if (vehicle.contactAngleAffectsTireForce)
            {
                downforce *= Mathf.Cos(wheel.contactAngle);
            }
            probe.stockDownforce = Mathf.Max(downforce, vehicle.minimumdownForce);
        }

        /// <summary>Keeps the tyre force VPP has just applied, as it applied it, for the known forces of the next step.</summary>
        private static void ApplyTireForcePostfixBody(VehicleBase.WheelState wheel)
        {
            Probe probe;
            if (wheel == null || !probes.TryGetValue(wheel, out probe))
            {
                return;
            }
            if (!wheel.grounded)
            {
                probe.lastTyreForce = Vector3.zero;
                return;
            }
            float drag = wheel.groundMaterial == null ? 0f : wheel.groundMaterial.drag;
            Vector2 dragForce = -wheel.hit.force * wheel.localWheelVelocity.magnitude * drag * 0.001f *
                wheel.localWheelVelocity;
            probe.lastTyreForce = wheel.hit.forwardDir * (wheel.tireForce.y + dragForce.y) +
                wheel.hit.sidewaysDir * (wheel.tireForce.x + dragForce.x);
            probe.lastTyrePoint = wheel.hit.point;
        }

        /// <summary>Shows the wheel damage the tyre load without the fix, for the length of its update.</summary>
        private static void DamageFixedUpdatePrefixBody(ModuleWheelDamage damage, out float __state)
        {
            __state = float.NaN;
            VehicleBase.WheelState wheel = StateOf(damage);
            Probe probe;
            if (wheel == null || !probes.TryGetValue(wheel, out probe) || probe.injected <= 0f)
            {
                return;
            }
            __state = wheel.downforce;
            wheel.downforce = probe.stockDownforce;
        }

        /// <summary>Gives the tyre load back to VPP after the wheel damage update.</summary>
        private static void DamageFixedUpdatePostfixBody(ModuleWheelDamage damage, float __state)
        {
            if (float.IsNaN(__state))
            {
                return;
            }
            VehicleBase.WheelState wheel = StateOf(damage);
            if (wheel != null)
            {
                wheel.downforce = __state;
            }
        }

        private static VehicleBase.WheelState StateOf(ModuleWheelDamage damage)
        {
            KSPWheelController controller = Wheel(damage);
            return controller != null && controller.wheelState != null && controller.wheelState.Length > 0
                ? controller.wheelState[0] : null;
        }

        /// <summary>
        /// Returns the probe of a wheel, created at its first step and filed under its vessel's balance; null when the
        /// wheel has no part.
        /// </summary>
        private static Probe ProbeOf(VehicleBase vehicle, VehicleBase.WheelState wheel)
        {
            Probe probe;
            if (probes.TryGetValue(wheel, out probe))
            {
                if (probe.body != null && probe.part != null)
                {
                    return probe;
                }
                Probes.Remove(probe);
                probes.Remove(wheel);
            }
            Rigidbody body = VehicleBody(vehicle);
            Part part = body != null ? body.GetComponent<Part>() : null;
            if (part == null)
            {
                return null;
            }
            probe = new Probe
            {
                partName = part.name,
                persistentId = part.persistentId,
                state = wheel,
                body = body,
                part = part
            };
            probes[wheel] = probe;
            Probes.Add(probe);
            return probe;
        }

        private static Balance BalanceOf(Vessel vessel)
        {
            Balance balance;
            if (!balances.TryGetValue(vessel, out balance))
            {
                balance = new Balance { vessel = vessel, vesselName = vessel.vesselName };
                balances[vessel] = balance;
                Balances.Add(balance);
            }
            return balance;
        }

        private static readonly List<Probe> groundedProbes = new List<Probe>();

        /// <summary>
        /// Once per physics step and vessel, at its first wheel: reads the contact of every wheel of the vessel. While
        /// one of them could reach its end-stop during the coming step, follows all the bodies of the vessel, sums the
        /// forces known to act
        /// on it (gravity, the forces KSP's modules applied, the suspension springs, the tyre forces), holds the
        /// end-stops for the coming step (<see cref="HoldEndStops"/>), and checks what PhysX's own limits pushed at the
        /// last step (<see cref="Balance.endStopNormal"/>).
        /// </summary>
        private static void Solve(Vessel vessel)
        {
            Balance balance = BalanceOf(vessel);
            float now = Time.fixedTime;
            if (balance.time == now)
            {
                return;
            }
            balance.time = now;
            float dt = Time.fixedDeltaTime;

            balance.wheels.Clear();
            for (int i = 0; i < Probes.Count; i++)
            {
                Probe p = Probes[i];
                if (p.part != null && p.part.vessel == vessel && p.state.wheelCol != null)
                {
                    balance.wheels.Add(p);
                }
            }

            // Every wheel's contact, as PhysX reports it for the last step: the other wheels' VPP state is still that
            // of the step before. The vessel is followed when a wheel could reach its end-stop during the coming step:
            // the travel it has left is no more than what its speed towards the end-stop, plus a step of free fall,
            // would cover.
            bool near = false;
            float fall = (float)vessel.graviticAcceleration.magnitude * dt;
            for (int i = 0; i < balance.wheels.Count; i++)
            {
                Probe p = balance.wheels[i];
                VPWheelCollider col = p.state.wheelCol;
                p.grounded = col.GetGroundHit(out p.hit);
                float travel = col.lastRuntimeSuspensionTravel;
                // The end-stop works along the axis of the suspension, as PhysX's does.
                p.axis = col.cachedTransform.up;
                p.compression = p.grounded && travel > 0f ? col.GetContactDepth(p.hit.point, travel) / travel : 0f;
                p.springForce = p.grounded ? p.hit.force : 0f;
                if (p.grounded && !near)
                {
                    float closing = -Vector3.Dot(p.body.GetPointVelocity(p.hit.point), p.axis);
                    near = (1f - p.compression) * travel <= Mathf.Max(0f, closing + fall) * dt;
                }
            }
            balance.heldNormal = 0f;
            balance.holding = 0;
            if (!near || vessel.packed)
            {
                // No wheel can reach its end-stop: nothing held, and nothing to keep for the next step.
                balance.watching = false;
                balance.endStopNormal = 0f;
                balance.statesTime = -1f;
                return;
            }
            balance.watching = true;

            // The bodies of the vessel as they end the last step, read once per step for all the fixes (VesselBodies):
            // their mass and inertia kept from the steps before, their velocities read now.
            bool followed = Mathf.Abs(now - balance.statesTime - dt) < dt * 0.25f;
            VesselBodies read = VesselBodies.Of(vessel);
            read.ReadMotion();
            VesselBodies.Body[] bodiesNow = read.bodies;
            int count = read.count;
            float mass = 0f;
            Vector3 centre = Vector3.zero;
            for (int i = 0; i < count; i++)
            {
                mass += bodiesNow[i].mass;
                centre += bodiesNow[i].mass * bodiesNow[i].centre;
            }
            balance.totalMass = mass;
            if (mass > 0f)
            {
                centre /= mass;
            }

            // KSP's acceleration of the last step. KSP's update of the parts may run before or after the wheels in a step:
            // either record may be the one of the step before.
            Vector3 acceleration;
            bool accelerationKnown = true;
            if (Mathf.Abs(balance.accelerationTime1 - (now - dt)) < dt * 0.25f)
            {
                acceleration = balance.acceleration1;
            }
            else if (Mathf.Abs(balance.accelerationTime0 - (now - dt)) < dt * 0.25f)
            {
                acceleration = balance.acceleration0;
            }
            else
            {
                acceleration = Vector3.zero;
                accelerationKnown = false;
            }

            // The forces known to act on the vessel at the last step, and its momentum change over it when its bodies
            // were followed then.
            bool complete = count > 0 && accelerationKnown && followed && balance.bodies.Count == count;
            for (int i = 0; complete && i < count; i++)
            {
                complete = ReferenceEquals(balance.bodies[i], bodiesNow[i].body);
            }
            Vector3 known = Vector3.zero;
            Vector3 knownTorque = Vector3.zero;
            Vector3 change = Vector3.zero;
            Vector3 normal = Vector3.zero;
            groundedProbes.Clear();
            for (int i = 0; i < count; i++)
            {
                ref VesselBodies.Body b = ref bodiesNow[i];
                Applied a = AppliedAt(b.body, now - dt);
                Vector3 f = b.mass * acceleration + a.force;
                known += f;
                knownTorque += Vector3.Cross(b.centre - centre, f) + a.torque;
                if (complete)
                {
                    change += b.mass * (b.velocity - balance.velocities[i]) / dt;
                }
            }
            for (int i = 0; i < balance.wheels.Count; i++)
            {
                Probe p = balance.wheels[i];
                if (!p.grounded)
                {
                    continue;
                }
                // PhysX applies the suspension force along the contact normal.
                Vector3 spring = p.hit.normal * p.springForce;
                known += spring;
                knownTorque += Vector3.Cross(p.hit.point - centre, spring);
                if (p.lastTime >= 0f && Mathf.Abs(now - p.lastTime - dt) < dt * 0.25f)
                {
                    Vector3 tyre = p.lastTyreForce;
                    known += tyre;
                    knownTorque += Vector3.Cross(p.lastTyrePoint - centre, tyre);
                }
                normal += p.hit.normal;
                groundedProbes.Add(p);
            }

            // What PhysX's limits pushed at the last step: the momentum change that neither the known forces nor the
            // forces the fix applied then explain.
            balance.endStopNormal = 0f;
            if (complete && normal != Vector3.zero)
            {
                Vector3 unexplained = change - known;
                for (int i = 0; i < balance.wheels.Count; i++)
                {
                    Probe p = balance.wheels[i];
                    if (Mathf.Abs(now - p.heldTime - dt) < dt * 0.25f)
                    {
                        unexplained -= p.heldForce;
                    }
                }
                balance.endStopNormal = Vector3.Dot(unexplained, normal.normalized);
            }

            if (Hold && accelerationKnown && mass > 0f && groundedProbes.Count > 0)
            {
                HoldEndStops(balance, bodiesNow, count, centre, known, knownTorque, now, dt);
            }

            // Kept for the next step.
            if (balance.bodies.Count != count)
            {
                balance.bodies.Clear();
                balance.velocities.Clear();
                for (int i = 0; i < count; i++)
                {
                    balance.bodies.Add(null);
                    balance.velocities.Add(Vector3.zero);
                }
            }
            for (int i = 0; i < count; i++)
            {
                balance.bodies[i] = bodiesNow[i].body;
                balance.velocities[i] = bodiesNow[i].velocity;
            }
            balance.statesTime = now;
        }

        private static float[] holdA = new float[8 * 8];
        private static float[] holdB = new float[8];
        private static float[] holdF = new float[8];
        private static float[] holdBefore = new float[8];
        private static Vector3[] holdArm = new Vector3[8];
        private static Vector3[] holdTurn = new Vector3[8];

        /// <summary>
        /// Holds the wheels on the ground at their end-stops for the coming physics step, in place of PhysX's limit. The
        /// vessel is taken as one rigid body (the first <paramref name="count"/> of <paramref name="bodies"/>, about
        /// <paramref name="centre"/>, their velocities read for this step), pushed by the <paramref name="known"/> forces and
        /// <paramref name="knownTorque"/> of the last step. Finds the least normal forces, none pulling, that keep every
        /// wheel of <see cref="groundedProbes"/> from going past the end of its travel by the end of the coming step,
        /// all wheels at once; applies them to the wheels' bodies at their contacts, and records them in
        /// <see cref="Probe.held"/>, <see cref="Balance.heldNormal"/> and <see cref="Balance.holding"/>. A wheel
        /// resting on its end-stop gets its share of the load beyond its spring; a wheel leaving it, or far from it, gets
        /// nothing; a wheel coming fast is slowed to just reach it.
        /// </summary>
        private static void HoldEndStops(Balance balance, VesselBodies.Body[] bodies, int count, Vector3 centre,
            Vector3 known, Vector3 knownTorque, float now, float dt)
        {
            int n = groundedProbes.Count;
            if (holdB.Length < n)
            {
                holdA = new float[n * n];
                holdB = new float[n];
                holdF = new float[n];
                holdBefore = new float[n];
                holdArm = new Vector3[n];
                holdTurn = new Vector3[n];
            }

            // The vessel as one rigid body: mass, velocity, inertia about the centre of mass, angular velocity.
            float mass = balance.totalMass;
            Vector3 momentum = Vector3.zero;
            Vector3 angularMomentum = Vector3.zero;
            Sym3 inertia = default(Sym3);
            for (int i = 0; i < count; i++)
            {
                ref VesselBodies.Body b = ref bodies[i];
                Vector3 offset = b.centre - centre;
                Vector3 p = b.mass * b.velocity;
                momentum += p;
                angularMomentum += b.spin + Vector3.Cross(offset, p);
                inertia.AddPrincipal(b.frame, b.inertia);
                inertia.AddPoint(b.mass, offset);
            }
            Sym3 inverse;
            if (!inertia.TryInverse(out inverse))
            {
                return;
            }
            Vector3 velocity = momentum / mass;
            Vector3 spin = inverse.Times(angularMomentum);
            Vector3 acceleration = known / mass;
            Vector3 angularAcceleration = inverse.Times(knownTorque);

            // Per wheel k: its separating speed at the end of the step, rhs[k] - gap/dt + dt * sum_j A[k, j] F[j], must
            // stay at least -gap/dt, the speed that just closes the travel left.
            float[] a = holdA, rhs = holdB, f = holdF;
            float trace = 0f;
            for (int k = 0; k < n; k++)
            {
                Probe p = groundedProbes[k];
                Vector3 normal = p.axis;
                Vector3 arm = p.hit.point - centre;
                holdArm[k] = Vector3.Cross(arm, normal);
                holdTurn[k] = inverse.Times(holdArm[k]);
                float travel = p.state.wheelCol.lastRuntimeSuspensionTravel;
                float gap = Mathf.Max(0f, (1f - p.compression) * travel);
                float separating = Vector3.Dot(normal, velocity + Vector3.Cross(spin, arm));
                rhs[k] = separating + dt * (Vector3.Dot(normal, acceleration) + Vector3.Dot(holdArm[k], angularAcceleration)) +
                    gap / dt;
                // Start from the force found at the step before, when there is one.
                f[k] = Mathf.Abs(now - p.heldTime - dt) < dt * 0.25f ? p.held : 0f;
            }
            for (int k = 0; k < n; k++)
            {
                Vector3 normal = groundedProbes[k].axis;
                for (int j = 0; j < n; j++)
                {
                    a[k * n + j] = Vector3.Dot(normal, groundedProbes[j].axis) / mass +
                        Vector3.Dot(holdArm[k], holdTurn[j]);
                }
                trace += a[k * n + k];
            }
            float lambda = Regularization * trace / n;

            // Projected Gauss-Seidel, in passes: no wheel pulls. The first pass pulls the forces towards zero, which picks
            // the evenest answer among the many but leaves them short by about the regularisation; each next pass pulls
            // them towards the answer of the pass before instead of zero, which keeps its sharing and makes up a further
            // part of the shortfall (short by about the regularisation to the power of the passes). Nothing is kept
            // from the step before but the starting point, so the sharing cannot drift from step to step: a wheel pair
            // across a diagonal left to carry the whole vessel makes the tyres slip.
            float[] target = holdBefore;
            for (int k = 0; k < n; k++)
            {
                target[k] = 0f;
            }
            for (int pass = 0; pass < Passes; pass++)
            {
                Sweep(n, dt, lambda);
                for (int k = 0; k < n; k++)
                {
                    target[k] = f[k];
                }
            }

            for (int k = 0; k < n; k++)
            {
                Probe p = groundedProbes[k];
                p.held = f[k];
                p.heldTime = now;
                p.heldForce = p.axis * f[k];
                p.heldPoint = p.hit.point;
                if (f[k] > 0f && p.body != null)
                {
                    p.body.AddForceAtPosition(p.heldForce, p.heldPoint, ForceMode.Force);
                    balance.heldNormal += f[k];
                    balance.holding++;
                }
            }
        }

        /// <summary>
        /// Runs at most <see cref="Iterations"/> sweeps of projected Gauss-Seidel, until no force changes by more than
        /// <see cref="Tolerance"/>, on the end-stop forces of
        /// <see cref="HoldEndStops"/>: <see cref="holdF"/> (n of them, also the starting point) such that
        /// (A + lambda) F = -rhs / dt + lambda * target, none negative, with A in <see cref="holdA"/>, rhs in
        /// <see cref="holdB"/> and the target in <see cref="holdBefore"/>.
        /// </summary>
        private static void Sweep(int n, float dt, float lambda)
        {
            float[] a = holdA, rhs = holdB, f = holdF, target = holdBefore;
            for (int iteration = 0; iteration < Iterations; iteration++)
            {
                float change = 0f;
                for (int k = 0; k < n; k++)
                {
                    float s = -rhs[k] / dt + lambda * target[k];
                    for (int j = 0; j < n; j++)
                    {
                        if (j != k)
                        {
                            s -= a[k * n + j] * f[j];
                        }
                    }
                    float value = s / (a[k * n + k] + lambda);
                    value = float.IsNaN(value) || value < 0f ? 0f : value;
                    change = Mathf.Max(change, Mathf.Abs(value - f[k]));
                    f[k] = value;
                }
                // Converged: the start from the step before usually leaves only a few sweeps to do.
                if (change <= Tolerance)
                {
                    break;
                }
            }
        }

        /// <summary>A symmetric 3 x 3 matrix, in doubles: the inertia tensor of a vessel.</summary>
        private struct Sym3
        {
            public double xx, yy, zz, xy, xz, yz;

            /// <summary>Adds a body's inertia, given by its principal axes in world axes and its principal moments.</summary>
            public void AddPrincipal(Quaternion frame, Vector3 moments)
            {
                Vector3 ex, ey, ez;
                VesselBodies.Axes(frame, out ex, out ey, out ez);
                AddOuter(ex, moments.x);
                AddOuter(ey, moments.y);
                AddOuter(ez, moments.z);
            }

            /// <summary>Adds the inertia of a point mass at <paramref name="offset"/> (parallel axis theorem).</summary>
            public void AddPoint(double mass, Vector3 offset)
            {
                double d2 = offset.sqrMagnitude;
                xx += mass * (d2 - offset.x * offset.x);
                yy += mass * (d2 - offset.y * offset.y);
                zz += mass * (d2 - offset.z * offset.z);
                xy -= mass * offset.x * offset.y;
                xz -= mass * offset.x * offset.z;
                yz -= mass * offset.y * offset.z;
            }

            private void AddOuter(Vector3 axis, double scale)
            {
                xx += scale * axis.x * axis.x;
                yy += scale * axis.y * axis.y;
                zz += scale * axis.z * axis.z;
                xy += scale * axis.x * axis.y;
                xz += scale * axis.x * axis.z;
                yz += scale * axis.y * axis.z;
            }

            public Vector3 Times(Vector3 v)
            {
                return new Vector3(
                    (float)(xx * v.x + xy * v.y + xz * v.z),
                    (float)(xy * v.x + yy * v.y + yz * v.z),
                    (float)(xz * v.x + yz * v.y + zz * v.z));
            }

            /// <summary>Returns false, with <paramref name="inverse"/> zero, when the matrix is singular.</summary>
            public bool TryInverse(out Sym3 inverse)
            {
                double cxx = yy * zz - yz * yz;
                double cxy = xz * yz - xy * zz;
                double cxz = xy * yz - xz * yy;
                double det = xx * cxx + xy * cxy + xz * cxz;
                inverse = default(Sym3);
                if (Math.Abs(det) < 1e-12 || double.IsNaN(det))
                {
                    return false;
                }
                inverse.xx = cxx / det;
                inverse.xy = cxy / det;
                inverse.xz = cxz / det;
                inverse.yy = (xx * zz - xz * xz) / det;
                inverse.yz = (xy * xz - xx * yz) / det;
                inverse.zz = (xx * yy - xy * xy) / det;
                return true;
            }
        }
    }
}
