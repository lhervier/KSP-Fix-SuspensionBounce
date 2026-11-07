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
    /// Gives the tyres the weight a bottomed suspension carries. Vehicle Physics Pro reads the load of each tyre in the
    /// force the PhysX WheelCollider reports (WheelHit.force), which is the spring and damper only: once the suspension
    /// reaches the end of its travel, PhysX holds the rest of the weight with a hard limit it reports nowhere, and the
    /// tyre loses the grip, the hold on a slope and the brake against sliding that this weight should give it. The fix
    /// measures those missing forces from the motion of the whole vessel, and adds each to the force VPP reads for its
    /// wheel, before VPP uses it.
    /// </summary>
    public static class BumpStopLoad
    {
        // Prototype knobs, public so that they can be changed in game (KSP-Util-MCPServer set_member).

        /// <summary>Whether the fix runs at all (settings.cfg, bumpStopLoad).</summary>
        public static bool Enabled = true;

        /// <summary>Whether the measured forces are given to VPP; when false, they are only measured.</summary>
        public static bool Inject = true;

        /// <summary>Compression from which a wheel is taken as bottomed.</summary>
        public static float BottomedCompression = 0.99f;

        /// <summary>
        /// Compression from which the vessel's motion is followed, so that the balance is ready when a wheel bottoms
        /// out; below it on every wheel, the fix only reads the wheels' contacts.
        /// </summary>
        public static float WatchCompression = 0.9f;

        /// <summary>
        /// Time constant (s) over which the measured force is averaged before VPP gets it: PhysX's end-stop holds the
        /// weight in pulses a few steps apart; 0 hands each step's force as it is.
        /// </summary>
        public static float SmoothingTime = 0.1f;

        /// <summary>Weight of the size of the solution against the residual, relative to the equations' scale.</summary>
        public static float Regularization = 1e-3f;

        /// <summary>
        /// Whether the fix carries the load of the bottomed wheels itself. PhysX's end-stop only exists at the steps
        /// that start with the wheel past the end of its travel, and puts it back exactly at the end: the next step has
        /// no end-stop, the vessel falls for a step, and the end-stop then pushes twice the weight. A force that holds
        /// the vessel still leaves the end-stop nothing to correct.
        /// </summary>
        public static bool Carry = true;

        /// <summary>Part of the load that keeps the vessel still the fix carries; the end-stop holds the rest.</summary>
        public static float CarryFraction = 1f;

        /// <summary>
        /// Weight of the size of the end-stop forces in their solve, relative to the contacts' scale: four wheels give
        /// more forces than the three equations (one force, two torques) need, and this picks the evenest.
        /// </summary>
        public static float HoldRegularization = 1e-2f;

        /// <summary>Gauss-Seidel sweeps of the end-stop solve.</summary>
        public static int HoldIterations = 30;

        /// <summary>What the fix measured on one wheel at the last physics step, kept for reading in game.</summary>
        public class Probe
        {
            public string partName;
            public uint persistentId;
            public float compression;
            /// <summary>Force PhysX reports for the spring and damper (kN).</summary>
            public float springForce;
            /// <summary>Normal force the vessel's motion gives this bottomed wheel beyond its spring (kN).</summary>
            public float missing;
            /// <summary>The same, averaged over <see cref="SmoothingTime"/> (kN).</summary>
            public float smoothed;
            /// <summary>Force added to the one VPP reads (kN).</summary>
            public float injected;
            /// <summary>The tyre load as VPP would have computed it without the fix (kN).</summary>
            public float stockDownforce;
            /// <summary>Normal force the fix applied to the wheel's body for the coming physics step (kN).</summary>
            public float carried;
            /// <summary>The force the contact solve found for this wheel, and when: the next solve starts from it.</summary>
            internal float held;
            internal float heldTime = -1f;

            internal VehicleBase.WheelState state;
            internal Rigidbody body;
            internal Part part;
            internal float lastTime = -1f;
            internal Vector3 lastTyreForce;
            internal Vector3 lastTyrePoint;
            internal bool missingFresh;
            /// <summary>The contact PhysX reports for the last step, read by the balance before VPP reads it.</summary>
            internal WheelHit hit;
            internal bool grounded;
        }

        /// <summary>What the fix measured on one vessel at the last physics step, kept for reading in game.</summary>
        public class Balance
        {
            public string vesselName;
            /// <summary>Momentum change of the vessel the known forces do not explain, along the mean normal (kN).</summary>
            public float residualNormal;
            /// <summary>The same, across the mean normal (kN).</summary>
            public float residualTangent;
            /// <summary>Angular momentum change the known torques do not explain, across the mean normal (kN.m).</summary>
            public float residualTorque;
            /// <summary>The same, once the forces given to the bottomed wheels are counted (kN, kN.m).</summary>
            public float remainingNormal;
            public float remainingTorque;
            /// <summary>
            /// Force along the mean normal that would keep the vessel from accelerating along it: the opposite of the
            /// known forces (kN).
            /// </summary>
            public float stillNormal;
            /// <summary>Sum of the forces the fix applied to the bottomed wheels for the coming step (kN).</summary>
            public float carriedNormal;
            public int bottomed;
            public float totalMass;
            /// <summary>Whether the vessel's motion was followed at the last step.</summary>
            public bool watching;

            internal Vessel vessel;
            internal float time = -1f;
            internal readonly List<Probe> wheels = new List<Probe>();
            /// <summary>The bodies followed, and their state at the end of the step before (same index).</summary>
            internal readonly List<Rigidbody> bodies = new List<Rigidbody>();
            internal readonly List<BodyState> states = new List<BodyState>();
            internal float statesTime = -1f;
            /// <summary>The vessel's acceleration KSP applied at the last two steps, the latest second.</summary>
            internal float accelerationTime0 = -1f, accelerationTime1 = -1f;
            internal Vector3 acceleration0, acceleration1;
        }

        internal struct BodyState
        {
            public Vector3 velocity;
            public Vector3 spin;
            // Read from Unity every RefreshSteps steps only: they change with the resources and the structure.
            public float mass;
            public Vector3 inertia;
            public Quaternion inertiaRotation;
            public Transform transform;
            public int age;
        }

        /// <summary>How many physics steps a body's mass and inertia are kept before being read again.</summary>
        public static int RefreshSteps = 50;

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
        /// Adds to the force VPP is about to read for the tyre load of <paramref name="wheel"/>, when it is bottomed, the
        /// normal force its end-stop carried at the last physics step. Called by VPP at every physics step, once the
        /// spring and damper force of the last step is read, before the tyre load is computed from it.
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
            if (!probe.missingFresh)
            {
                probe.missing = 0f;
            }
            probe.missingFresh = false;
            if (!wheel.grounded)
            {
                probe.smoothed = 0f;
            }
            else if (probe.missing > 0f || probe.smoothed > 0f)
            {
                float weight = SmoothingTime > 0f ? Mathf.Clamp01(Time.fixedDeltaTime / SmoothingTime) : 1f;
                probe.smoothed += (probe.missing - probe.smoothed) * weight;
                if (probe.smoothed < 1e-4f)
                {
                    probe.smoothed = 0f;
                }
            }
            if (Inject && wheel.grounded && probe.smoothed > 0f)
            {
                probe.injected = probe.smoothed;
                wheel.hit.force += probe.injected;
            }
            probe.lastTime = Time.fixedTime;
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

        /// <summary>Keeps the tyre force VPP has just applied, as it applied it, for the balance of the next step.</summary>
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

        /// <summary>A body of the vessel as it ends the last step: read once from Unity.</summary>
        private struct BodyNow
        {
            public Rigidbody body;
            public float mass;
            public Vector3 centre;
            public Vector3 velocity;
            public Vector3 spin;
            public Vector3 inertia;
            public Quaternion inertiaRotation;
            /// <summary>The body's principal axes in world axes.</summary>
            public Quaternion frame;
            public Transform transform;
            public int age;
        }

        private static BodyNow[] bodiesNow = new BodyNow[64];
        private static readonly List<Probe> bottomedProbes = new List<Probe>();
        private static readonly List<Probe> groundedProbes = new List<Probe>();

        /// <summary>
        /// Once per physics step and vessel, at its first wheel: reads the contact of every wheel of the vessel. While
        /// one of them is near the end of its travel, follows the motion of all its bodies; when one is at the end, takes
        /// away from the change of momentum and angular momentum of the vessel over the last step every force known to
        /// act on it (gravity, the forces KSP's modules applied, the suspension springs, the tyre forces), and shares
        /// what is left along the ground normal between the bottomed wheels, so that it also balances the torques. The
        /// joints between the parts cancel out.
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

            // The wheels of the vessel, filed again only when a wheel's vessel changed.
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
            // of the step before.
            float deepest = 0f;
            for (int i = 0; i < balance.wheels.Count; i++)
            {
                Probe p = balance.wheels[i];
                VPWheelCollider col = p.state.wheelCol;
                p.grounded = col.GetGroundHit(out p.hit);
                float travel = col.lastRuntimeSuspensionTravel;
                p.compression = p.grounded && travel > 0f ? col.GetContactDepth(p.hit.point, travel) / travel : 0f;
                p.springForce = p.grounded ? p.hit.force : 0f;
                if (p.compression > deepest)
                {
                    deepest = p.compression;
                }
            }
            if (deepest < WatchCompression || vessel.packed)
            {
                // Nothing near the end of its travel: no balance, and nothing to keep for the next step.
                balance.watching = false;
                balance.bottomed = 0;
                balance.statesTime = -1f;
                return;
            }
            balance.watching = true;

            // The bodies of the vessel as they end the last step, read once.
            bool followed = Mathf.Abs(now - balance.statesTime - dt) < dt * 0.25f;
            int count = 0;
            float mass = 0f;
            Vector3 centre = Vector3.zero;
            List<Part> parts = vessel.parts;
            for (int i = 0; i < parts.Count; i++)
            {
                Part part = parts[i];
                if (part == null)
                {
                    continue;
                }
                for (int k = 0; k < 2; k++)
                {
                    Rigidbody body = k == 0 ? part.rb : part.servoRb;
                    if (body == null)
                    {
                        continue;
                    }
                    if (count == bodiesNow.Length)
                    {
                        Array.Resize(ref bodiesNow, count * 2);
                    }
                    // The mass and inertia kept from the step before, when they are of the same body and not too old.
                    BodyState kept = default(BodyState);
                    bool keep = followed && count < balance.bodies.Count && ReferenceEquals(balance.bodies[count], body);
                    if (keep)
                    {
                        kept = balance.states[count];
                        keep = kept.age < RefreshSteps;
                    }
                    if (!keep)
                    {
                        kept.mass = body.mass;
                        kept.inertia = body.inertiaTensor;
                        kept.inertiaRotation = body.inertiaTensorRotation;
                        kept.transform = body.transform;
                        kept.age = 0;
                    }
                    BodyNow b;
                    b.body = body;
                    b.mass = kept.mass;
                    b.inertia = kept.inertia;
                    b.inertiaRotation = kept.inertiaRotation;
                    b.transform = kept.transform;
                    b.age = kept.age + 1;
                    b.centre = body.worldCenterOfMass;
                    b.velocity = body.velocity;
                    // The transform's rotation, which the physics wrote back at the end of the step: Rigidbody.rotation would
                    // sync every transform of the scene first (Physics.autoSyncTransforms), at a high cost.
                    Quaternion rotation = kept.transform.rotation;
                    Vector3 angular = body.angularVelocity;
                    b.frame = rotation * kept.inertiaRotation;
                    b.spin = Spin(b.frame, kept.inertia, angular);
                    bodiesNow[count++] = b;
                    mass += b.mass;
                    centre += b.mass * b.centre;
                }
            }
            balance.totalMass = mass;
            if (mass > 0f)
            {
                centre /= mass;
            }

            // The bodies must be those of the step before, in the same order, and KSP's acceleration of that step known.
            // KSP's update of the parts may run before or after the wheels in a step: either record may be the one of
            // the step before.
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
            bool complete = count > 0 && accelerationKnown &&
                Mathf.Abs(now - balance.statesTime - dt) < dt * 0.25f && balance.bodies.Count == count;
            for (int i = 0; complete && i < count; i++)
            {
                complete = ReferenceEquals(balance.bodies[i], bodiesNow[i].body);
            }

            // The momentum change of the last step, and the known forces of that step, apart.
            Vector3 momentum = Vector3.zero;
            Vector3 angularMomentum = Vector3.zero;
            Vector3 known = Vector3.zero;
            Vector3 knownTorque = Vector3.zero;
            // The same without the springs: what all the wheels together must hold for the vessel to keep still.
            Vector3 load = Vector3.zero;
            Vector3 loadTorque = Vector3.zero;
            Vector3 normal = Vector3.zero;
            bottomedProbes.Clear();
            groundedProbes.Clear();
            if (complete)
            {
                // Change of momentum and of angular momentum about the centre of mass, and the forces KSP applied.
                for (int i = 0; i < count; i++)
                {
                    BodyNow b = bodiesNow[i];
                    BodyState before = balance.states[i];
                    Applied a = AppliedAt(b.body, now - dt);
                    Vector3 change = b.mass * (b.velocity - before.velocity) / dt;
                    Vector3 applied = b.mass * acceleration + a.force;
                    momentum += change;
                    angularMomentum += Vector3.Cross(b.centre - centre, change) + (b.spin - before.spin) / dt;
                    known += applied;
                    knownTorque += Vector3.Cross(b.centre - centre, applied) + a.torque;
                    load -= applied;
                    loadTorque -= Vector3.Cross(b.centre - centre, applied) + a.torque;
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
                        load -= tyre;
                        loadTorque -= Vector3.Cross(p.lastTyrePoint - centre, tyre);
                    }
                    normal += p.hit.normal;
                    groundedProbes.Add(p);
                    if (p.compression >= BottomedCompression)
                    {
                        bottomedProbes.Add(p);
                    }
                }
            }

            for (int i = 0; i < balance.wheels.Count; i++)
            {
                balance.wheels[i].carried = 0f;
            }
            balance.carriedNormal = 0f;
            balance.bottomed = 0;
            bool ready = complete && normal != Vector3.zero;
            if (ready)
            {
                normal.Normalize();
            }
            if (ready && bottomedProbes.Count > 0)
            {
                // What the end-stops did at the last step, the force carried by the fix included: the tyre load.
                Vector3 force = momentum - known;
                Vector3 torque = angularMomentum - knownTorque;
                Vector3 across = force - normal * Vector3.Dot(force, normal);
                Vector3 torqueAcross = torque - normal * Vector3.Dot(torque, normal);
                balance.residualNormal = Vector3.Dot(force, normal);
                balance.residualTangent = across.magnitude;
                balance.residualTorque = torqueAcross.magnitude;
                balance.bottomed = bottomedProbes.Count;
                ShareForces(bottomedProbes, centre, normal, force, torque);
                Vector3 remainingForce = force;
                Vector3 remainingTorque = torque;
                for (int k = 0; k < bottomedProbes.Count; k++)
                {
                    Probe p = bottomedProbes[k];
                    p.missing = shareX[k];
                    p.missingFresh = true;
                    Vector3 f = p.hit.normal * shareX[k];
                    remainingForce -= f;
                    remainingTorque -= Vector3.Cross(p.hit.point - centre, f);
                }
                balance.remainingNormal = Vector3.Dot(remainingForce, normal);
                balance.remainingTorque = (remainingTorque - normal * Vector3.Dot(remainingTorque, normal)).magnitude;
            }

            balance.stillNormal = ready ? Vector3.Dot(load, normal) : 0f;
            if (ready && Carry && CarryFraction > 0f && balance.totalMass > 0f)
            {
                HoldEndStops(balance, centre, count, known, knownTorque, now, dt);
            }

            // Kept for the next step.
            if (balance.bodies.Count != count)
            {
                balance.bodies.Clear();
                balance.states.Clear();
                for (int i = 0; i < count; i++)
                {
                    balance.bodies.Add(null);
                    balance.states.Add(default(BodyState));
                }
            }
            for (int i = 0; i < count; i++)
            {
                balance.bodies[i] = bodiesNow[i].body;
                BodyNow b = bodiesNow[i];
                balance.states[i] = new BodyState
                {
                    velocity = b.velocity, spin = b.spin, mass = b.mass, inertia = b.inertia,
                    inertiaRotation = b.inertiaRotation, transform = b.transform, age = b.age
                };
            }
            balance.statesTime = now;
        }

        private static float[] shareA = new float[3 * 8];
        private static float[] shareM = new float[8 * 8];
        private static float[] shareR = new float[8];
        private static float[] shareX = new float[8];

        /// <summary>
        /// Finds the normal forces of <paramref name="wheels"/> that best give <paramref name="force"/> along
        /// <paramref name="normal"/> and <paramref name="torque"/> across it, none negative, and leaves them in
        /// <see cref="shareX"/>, in the order of <paramref name="wheels"/>.
        /// </summary>
        private static void ShareForces(List<Probe> wheels, Vector3 centre, Vector3 normal, Vector3 force, Vector3 torque)
        {
            int n = wheels.Count;
            if (shareR.Length < n)
            {
                shareA = new float[3 * n];
                shareM = new float[n * n];
                shareR = new float[n];
                shareX = new float[n];
            }
            float[] a = shareA, m = shareM, r = shareR, x = shareX;
            // Two axes across the normal, and a length to put torques on the scale of forces.
            Vector3 axis1 = Vector3.Cross(normal, Vector3.right);
            if (axis1.sqrMagnitude < 0.01f)
            {
                axis1 = Vector3.Cross(normal, Vector3.forward);
            }
            axis1.Normalize();
            Vector3 axis2 = Vector3.Cross(normal, axis1);
            float length = 0f;
            for (int i = 0; i < n; i++)
            {
                length += (wheels[i].hit.point - centre).sqrMagnitude;
            }
            length = Mathf.Max(Mathf.Sqrt(length / n), 0.5f);

            // Rows: force along the normal, torque about axis1 and axis2 (divided by the length); a[row * n + k].
            float b0 = Vector3.Dot(force, normal);
            float b1 = Vector3.Dot(torque, axis1) / length;
            float b2 = Vector3.Dot(torque, axis2) / length;
            for (int k = 0; k < n; k++)
            {
                Probe w = wheels[k];
                Vector3 moment = Vector3.Cross(w.hit.point - centre, w.hit.normal);
                a[k] = Vector3.Dot(w.hit.normal, normal);
                a[n + k] = Vector3.Dot(moment, axis1) / length;
                a[2 * n + k] = Vector3.Dot(moment, axis2) / length;
            }
            // Normal equations, solved by projected Gauss-Seidel to keep every force at or above zero.
            float trace = 0f;
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    m[i * n + j] = a[i] * a[j] + a[n + i] * a[n + j] + a[2 * n + i] * a[2 * n + j];
                }
                r[i] = a[i] * b0 + a[n + i] * b1 + a[2 * n + i] * b2;
                trace += m[i * n + i];
                x[i] = 0f;
            }
            float lambda = Regularization * trace / n;
            for (int iteration = 0; iteration < 50; iteration++)
            {
                for (int i = 0; i < n; i++)
                {
                    float s = r[i];
                    for (int j = 0; j < n; j++)
                    {
                        if (j != i)
                        {
                            s -= m[i * n + j] * x[j];
                        }
                    }
                    x[i] = Mathf.Max(0f, s / (m[i * n + i] + lambda));
                }
            }
            for (int k = 0; k < n; k++)
            {
                if (float.IsNaN(x[k]) || float.IsInfinity(x[k]))
                {
                    x[k] = 0f;
                }
            }
        }

        private static float[] holdA = new float[8 * 8];
        private static float[] holdB = new float[8];
        private static float[] holdF = new float[8];
        private static Vector3[] holdArm = new Vector3[8];
        private static Vector3[] holdTurn = new Vector3[8];

        /// <summary>
        /// Holds the wheels on the ground at their end-stops for the coming physics step, in place of PhysX's end-stop,
        /// which only exists at the steps that start past the end of the travel. The vessel is taken as one rigid body
        /// (the bodies of <see cref="bodiesNow"/>, <paramref name="count"/> of them, about <paramref name="centre"/>),
        /// pushed by the <paramref name="known"/> forces and <paramref name="knownTorque"/> of the last step. Finds the
        /// least normal forces, none pulling, that keep every wheel from going past the end of its travel by the end of
        /// the coming step, all wheels at once; applies them to the wheels' bodies at their contacts, and records them in
        /// <see cref="Probe.carried"/> and <see cref="Balance.carriedNormal"/>. A wheel resting on its end-stop gets
        /// its share of the load beyond its spring; a wheel leaving it, or far from it, gets nothing; a wheel coming fast
        /// is slowed to just reach it.
        /// </summary>
        private static void HoldEndStops(Balance balance, Vector3 centre, int count, Vector3 known, Vector3 knownTorque,
            float now, float dt)
        {
            int n = groundedProbes.Count;
            if (n == 0)
            {
                return;
            }
            if (holdB.Length < n)
            {
                holdA = new float[n * n];
                holdB = new float[n];
                holdF = new float[n];
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
                BodyNow b = bodiesNow[i];
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

            // Per wheel k: its separating speed at the end of the step is b[k] - gap/dt + dt * sum_j A[k, j] F[j], and
            // must stay at least -gap/dt, the speed that just closes the travel left.
            float[] a = holdA, rhs = holdB, f = holdF;
            float trace = 0f;
            for (int k = 0; k < n; k++)
            {
                Probe p = groundedProbes[k];
                Vector3 normal = p.hit.normal;
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
                Vector3 normal = groundedProbes[k].hit.normal;
                for (int j = 0; j < n; j++)
                {
                    a[k * n + j] = Vector3.Dot(normal, groundedProbes[j].hit.normal) / mass +
                        Vector3.Dot(holdArm[k], holdTurn[j]);
                }
                trace += a[k * n + k];
            }
            float lambda = HoldRegularization * trace / n;

            // Projected Gauss-Seidel: no wheel pulls.
            for (int iteration = 0; iteration < HoldIterations; iteration++)
            {
                for (int k = 0; k < n; k++)
                {
                    float s = -rhs[k] / dt;
                    for (int j = 0; j < n; j++)
                    {
                        if (j != k)
                        {
                            s -= a[k * n + j] * f[j];
                        }
                    }
                    float value = s / (a[k * n + k] + lambda);
                    f[k] = float.IsNaN(value) || value < 0f ? 0f : value;
                }
            }

            for (int k = 0; k < n; k++)
            {
                Probe p = groundedProbes[k];
                p.held = f[k];
                p.heldTime = now;
                p.carried = f[k] * CarryFraction;
                if (p.carried > 0f && p.body != null)
                {
                    p.body.AddForceAtPosition(p.hit.normal * p.carried, p.hit.point, ForceMode.Force);
                    balance.carriedNormal += p.carried;
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
                AddOuter(frame * Vector3.right, moments.x);
                AddOuter(frame * Vector3.up, moments.y);
                AddOuter(frame * Vector3.forward, moments.z);
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

            /// <summary>Returns false, with <paramref name="inverse"/> unset, when the matrix is singular.</summary>
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

        /// <summary>
        /// The angular momentum of a body about its centre of mass, in world axes, from its principal axes
        /// <paramref name="frame"/>, its principal moments <paramref name="inertia"/> and its angular velocity.
        /// </summary>
        private static Vector3 Spin(Quaternion frame, Vector3 inertia, Vector3 angularVelocity)
        {
            Vector3 local = Quaternion.Inverse(frame) * angularVelocity;
            return frame * Vector3.Scale(inertia, local);
        }
    }
}
