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
            public Transform transform;
            public int age;
        }

        private static BodyNow[] bodiesNow = new BodyNow[64];
        private static readonly List<Probe> bottomedProbes = new List<Probe>();

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
                    b.spin = Spin(rotation * kept.inertiaRotation, kept.inertia, angular);
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

            Vector3 force = Vector3.zero;
            Vector3 torque = Vector3.zero;
            Vector3 normal = Vector3.zero;
            bottomedProbes.Clear();
            if (complete && deepest >= BottomedCompression)
            {
                // Change of momentum and of angular momentum about the centre of mass, against the known forces.
                for (int i = 0; i < count; i++)
                {
                    BodyNow b = bodiesNow[i];
                    BodyState before = balance.states[i];
                    Applied a = AppliedAt(b.body, now - dt);
                    Vector3 excess = b.mass * ((b.velocity - before.velocity) / dt - acceleration) - a.force;
                    force += excess;
                    torque += Vector3.Cross(b.centre - centre, excess) + (b.spin - before.spin) / dt - a.torque;
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
                    force -= spring;
                    torque -= Vector3.Cross(p.hit.point - centre, spring);
                    if (p.lastTime >= 0f && Mathf.Abs(now - p.lastTime - dt) < dt * 0.25f)
                    {
                        Vector3 tyre = p.lastTyreForce;
                        force -= tyre;
                        torque -= Vector3.Cross(p.lastTyrePoint - centre, tyre);
                    }
                    normal += p.hit.normal;
                    if (p.compression >= BottomedCompression)
                    {
                        bottomedProbes.Add(p);
                    }
                }
            }

            if (complete && bottomedProbes.Count > 0 && normal != Vector3.zero)
            {
                normal.Normalize();
                Vector3 across = force - normal * Vector3.Dot(force, normal);
                Vector3 torqueAcross = torque - normal * Vector3.Dot(torque, normal);
                balance.residualNormal = Vector3.Dot(force, normal);
                balance.residualTangent = across.magnitude;
                balance.residualTorque = torqueAcross.magnitude;
                balance.bottomed = bottomedProbes.Count;
                Share(balance, centre, normal, force, torque);
            }
            else
            {
                balance.bottomed = 0;
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
        /// Finds the normal forces of the bottomed wheels that best explain the residual force along
        /// <paramref name="normal"/> and the residual torque across it, none negative, and hands them to their probes.
        /// </summary>
        private static void Share(Balance balance, Vector3 centre, Vector3 normal, Vector3 force, Vector3 torque)
        {
            int n = bottomedProbes.Count;
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
                length += (bottomedProbes[i].hit.point - centre).sqrMagnitude;
            }
            length = Mathf.Max(Mathf.Sqrt(length / n), 0.5f);

            // Rows: force along the normal, torque about axis1 and axis2 (divided by the length); a[row * n + k].
            float b0 = Vector3.Dot(force, normal);
            float b1 = Vector3.Dot(torque, axis1) / length;
            float b2 = Vector3.Dot(torque, axis2) / length;
            for (int k = 0; k < n; k++)
            {
                Probe w = bottomedProbes[k];
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
            Vector3 remainingForce = force;
            Vector3 remainingTorque = torque;
            for (int k = 0; k < n; k++)
            {
                Probe p = bottomedProbes[k];
                float value = float.IsNaN(x[k]) || float.IsInfinity(x[k]) ? 0f : x[k];
                p.missing = value;
                p.missingFresh = true;
                Vector3 f = p.hit.normal * value;
                remainingForce -= f;
                remainingTorque -= Vector3.Cross(p.hit.point - centre, f);
            }
            balance.remainingNormal = Vector3.Dot(remainingForce, normal);
            balance.remainingTorque = (remainingTorque - normal * Vector3.Dot(remainingTorque, normal)).magnitude;
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
