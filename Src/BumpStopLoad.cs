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
            internal Vessel vessel;
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

            internal Vessel vessel;
            internal float time = -1f;
            internal readonly Dictionary<Rigidbody, BodyState> bodies = new Dictionary<Rigidbody, BodyState>();
        }

        internal class BodyState
        {
            public float time;
            public Vector3 velocity;
            public Vector3 spin;
        }

        /// <summary>Forces KSP handed to a body for one physics step (FlightIntegrator.Integrate).</summary>
        private struct Applied
        {
            public float time;
            public Vector3 acceleration;
            public Vector3 force;
            /// <summary>Torque about the body's centre of mass.</summary>
            public Vector3 torque;
        }

        public static readonly List<Probe> Probes = new List<Probe>();
        public static readonly List<Balance> Balances = new List<Balance>();

        private static readonly Dictionary<VehicleBase.WheelState, Probe> probes =
            new Dictionary<VehicleBase.WheelState, Probe>();
        private static readonly Dictionary<Vessel, Balance> balances = new Dictionary<Vessel, Balance>();

        /// <summary>For each body, what KSP handed it at the last two physics steps, the latest second.</summary>
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
        /// (gravity and the frame's), and the forces and torques the part's modules gathered.
        /// </summary>
        private static void IntegratePrefix(FlightIntegrator __instance, Part part)
        {
            if (!Enabled || part == null || part.vessel == null)
            {
                return;
            }
            Vector3 acceleration = part.vessel.precalc.integrationAccel;
            if (part.rb != null)
            {
                Vector3 force = part.force;
                Vector3 torque = part.torque;
                Vector3 centre = part.rb.worldCenterOfMass;
                for (int i = 0; i < part.forces.Count; i++)
                {
                    Vector3 f = part.forces[i].force;
                    force += f;
                    torque += Vector3.Cross((Vector3)part.forces[i].pos - centre, f);
                }
                Record(part.rb, acceleration, force, torque);
            }
            if (part.servoRb != null)
            {
                Record(part.servoRb, acceleration, Vector3.zero, Vector3.zero);
            }
        }

        private static void Record(Rigidbody body, Vector3 acceleration, Vector3 force, Vector3 torque)
        {
            Applied[] history;
            if (!applied.TryGetValue(body, out history))
            {
                history = new Applied[2];
                applied[body] = history;
            }
            float now = Time.fixedTime;
            if (history[1].time != now)
            {
                history[0] = history[1];
            }
            history[1] = new Applied { time = now, acceleration = acceleration, force = force, torque = torque };
        }

        /// <summary>Returns what KSP handed <paramref name="body"/> for the physics step that started at <paramref name="time"/>.</summary>
        private static bool AppliedAt(Rigidbody body, float time, out Applied result)
        {
            Applied[] history;
            result = default(Applied);
            if (!applied.TryGetValue(body, out history))
            {
                return false;
            }
            for (int i = 1; i >= 0; i--)
            {
                if (Mathf.Abs(history[i].time - time) < Time.fixedDeltaTime * 0.25f)
                {
                    result = history[i];
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Adds to the force VPP is about to read for the tyre load of <paramref name="wheel"/>, when it is bottomed, the
        /// normal force its end-stop carried at the last physics step. Called by VPP at every physics step, once the
        /// spring and damper force of the last step is read, before the tyre load is computed from it.
        /// </summary>
        private static void CalculateSuspensionForcesPostfix(VehicleBase __instance, VehicleBase.WheelState wheel)
        {
            if (!Enabled || !HighLogic.LoadedSceneIsFlight || wheel == null || wheel.wheelCol == null)
            {
                return;
            }
            Probe probe = ProbeOf(__instance, wheel);
            if (probe == null)
            {
                return;
            }
            probe.injected = 0f;
            if (probe.vessel != null)
            {
                Solve(probe.vessel);
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
            else
            {
                float weight = SmoothingTime > 0f ? Mathf.Clamp01(Time.fixedDeltaTime / SmoothingTime) : 1f;
                probe.smoothed += (probe.missing - probe.smoothed) * weight;
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
        /// impacts; and the tyre force VPP is about to apply, for the next balance.
        /// </summary>
        private static void CalculateDownforcePostfix(VehicleBase __instance, VehicleBase.WheelState wheel)
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
            if (__instance.contactAngleAffectsTireForce)
            {
                downforce *= Mathf.Cos(wheel.contactAngle);
            }
            probe.stockDownforce = Mathf.Max(downforce, __instance.minimumdownForce);
        }

        /// <summary>Keeps the tyre force VPP has just applied, as it applied it, for the balance of the next step.</summary>
        private static void ApplyTireForcePostfix(VehicleBase.WheelState wheel)
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
        private static void DamageFixedUpdatePrefix(ModuleWheelDamage __instance, out float __state)
        {
            __state = float.NaN;
            VehicleBase.WheelState wheel = StateOf(__instance);
            Probe probe;
            if (wheel == null || !probes.TryGetValue(wheel, out probe) || probe.injected <= 0f)
            {
                return;
            }
            __state = wheel.downforce;
            wheel.downforce = probe.stockDownforce;
        }

        /// <summary>Gives the tyre load back to VPP after the wheel damage update.</summary>
        private static void DamageFixedUpdatePostfix(ModuleWheelDamage __instance, float __state)
        {
            if (float.IsNaN(__state))
            {
                return;
            }
            VehicleBase.WheelState wheel = StateOf(__instance);
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

        /// <summary>Returns the probe of a wheel, created at its first step; null when the wheel has no part.</summary>
        private static Probe ProbeOf(VehicleBase vehicle, VehicleBase.WheelState wheel)
        {
            Probe probe;
            if (probes.TryGetValue(wheel, out probe))
            {
                if (probe.body != null)
                {
                    Part owner = probe.body.GetComponent<Part>();
                    probe.vessel = owner != null ? owner.vessel : null;
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
                vessel = part.vessel
            };
            probes[wheel] = probe;
            Probes.Add(probe);
            return probe;
        }

        private static readonly List<Probe> vesselProbes = new List<Probe>();
        private static readonly List<Probe> bottomedProbes = new List<Probe>();
        private static readonly List<Rigidbody> vesselBodies = new List<Rigidbody>();

        /// <summary>
        /// Once per physics step and vessel, at its first wheel: from the change of momentum and angular momentum of
        /// all its bodies over the last step, takes away every force known to act on them (gravity, the forces KSP's
        /// modules applied, the suspension springs, the tyre forces), and shares what is left along the ground normal
        /// between the bottomed wheels, so that it also balances the torques. The joints between the parts cancel out.
        /// </summary>
        private static void Solve(Vessel vessel)
        {
            Balance balance;
            if (!balances.TryGetValue(vessel, out balance))
            {
                balance = new Balance { vessel = vessel, vesselName = vessel.vesselName };
                balances[vessel] = balance;
                Balances.Add(balance);
            }
            float now = Time.fixedTime;
            if (balance.time == now)
            {
                return;
            }
            balance.time = now;
            float dt = Time.fixedDeltaTime;

            vesselProbes.Clear();
            bottomedProbes.Clear();
            for (int i = 0; i < Probes.Count; i++)
            {
                Probe p = Probes[i];
                if (p.vessel == vessel && p.body != null && p.state.wheelCol != null)
                {
                    // Read here for every wheel, the others' VPP state being still that of the step before.
                    VPWheelCollider col = p.state.wheelCol;
                    p.grounded = col.GetGroundHit(out p.hit);
                    float travel = col.lastRuntimeSuspensionTravel;
                    p.compression = p.grounded && travel > 0f ? col.GetContactDepth(p.hit.point, travel) / travel : 0f;
                    p.springForce = p.grounded ? p.hit.force : 0f;
                    vesselProbes.Add(p);
                }
            }

            // The bodies of the vessel, as they end the last step.
            vesselBodies.Clear();
            for (int i = 0; i < vessel.parts.Count; i++)
            {
                Part part = vessel.parts[i];
                if (part == null)
                {
                    continue;
                }
                if (part.rb != null)
                {
                    vesselBodies.Add(part.rb);
                }
                if (part.servoRb != null)
                {
                    vesselBodies.Add(part.servoRb);
                }
            }

            bool complete = !vessel.packed && vesselBodies.Count > 0;
            float mass = 0f;
            Vector3 centre = Vector3.zero;
            for (int i = 0; i < vesselBodies.Count; i++)
            {
                Rigidbody body = vesselBodies[i];
                mass += body.mass;
                centre += body.mass * body.worldCenterOfMass;
                BodyState state;
                if (!balance.bodies.TryGetValue(body, out state) || Mathf.Abs(now - state.time - dt) > dt * 0.25f ||
                    body.isKinematic)
                {
                    complete = false;
                }
            }
            balance.totalMass = mass;
            if (mass > 0f)
            {
                centre /= mass;
            }

            // Change of momentum and of angular momentum about the centre of mass, against the known forces.
            Vector3 force = Vector3.zero;
            Vector3 torque = Vector3.zero;
            if (complete)
            {
                for (int i = 0; i < vesselBodies.Count; i++)
                {
                    Rigidbody body = vesselBodies[i];
                    BodyState state = balance.bodies[body];
                    Vector3 arm = body.worldCenterOfMass - centre;
                    Vector3 change = body.mass * (body.velocity - state.velocity) / dt;
                    Applied a;
                    if (!AppliedAt(body, now - dt, out a))
                    {
                        complete = false;
                        break;
                    }
                    Vector3 known = body.mass * a.acceleration + a.force;
                    Vector3 excess = change - known;
                    force += excess;
                    torque += Vector3.Cross(arm, excess) + (Spin(body) - state.spin) / dt - a.torque;
                }
            }

            Vector3 normal = Vector3.zero;
            if (complete)
            {
                for (int i = 0; i < vesselProbes.Count; i++)
                {
                    Probe p = vesselProbes[i];
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

            if (complete && normal != Vector3.zero)
            {
                normal.Normalize();
                Vector3 across = force - normal * Vector3.Dot(force, normal);
                Vector3 torqueAcross = torque - normal * Vector3.Dot(torque, normal);
                balance.residualNormal = Vector3.Dot(force, normal);
                balance.residualTangent = across.magnitude;
                balance.residualTorque = torqueAcross.magnitude;
                balance.bottomed = bottomedProbes.Count;
                if (bottomedProbes.Count > 0)
                {
                    Share(balance, centre, normal, force, torque);
                }
                else
                {
                    balance.remainingNormal = balance.residualNormal;
                    balance.remainingTorque = balance.residualTorque;
                }
            }
            else
            {
                balance.bottomed = 0;
            }

            // Kept for the next step.
            for (int i = 0; i < vesselBodies.Count; i++)
            {
                Rigidbody body = vesselBodies[i];
                BodyState state;
                if (!balance.bodies.TryGetValue(body, out state))
                {
                    state = new BodyState();
                    balance.bodies[body] = state;
                }
                state.time = now;
                state.velocity = body.velocity;
                state.spin = Spin(body);
            }
        }

        /// <summary>
        /// Finds the normal forces of the bottomed wheels that best explain the residual force along
        /// <paramref name="normal"/> and the residual torque across it, none negative, and hands them to their probes.
        /// </summary>
        private static void Share(Balance balance, Vector3 centre, Vector3 normal, Vector3 force, Vector3 torque)
        {
            int n = bottomedProbes.Count;
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

            // Rows: force along the normal, torque about axis1 and axis2 (divided by the length).
            float[,] a = new float[3, n];
            float[] b = { Vector3.Dot(force, normal), Vector3.Dot(torque, axis1) / length, Vector3.Dot(torque, axis2) / length };
            for (int k = 0; k < n; k++)
            {
                Probe w = bottomedProbes[k];
                Vector3 moment = Vector3.Cross(w.hit.point - centre, w.hit.normal);
                a[0, k] = Vector3.Dot(w.hit.normal, normal);
                a[1, k] = Vector3.Dot(moment, axis1) / length;
                a[2, k] = Vector3.Dot(moment, axis2) / length;
            }
            // Normal equations, solved by projected Gauss-Seidel to keep every force at or above zero.
            float[,] m = new float[n, n];
            float[] r = new float[n];
            float trace = 0f;
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    float s = 0f;
                    for (int row = 0; row < 3; row++)
                    {
                        s += a[row, i] * a[row, j];
                    }
                    m[i, j] = s;
                }
                float t = 0f;
                for (int row = 0; row < 3; row++)
                {
                    t += a[row, i] * b[row];
                }
                r[i] = t;
                trace += m[i, i];
            }
            float lambda = Regularization * trace / n;
            float[] x = new float[n];
            for (int iteration = 0; iteration < 50; iteration++)
            {
                for (int i = 0; i < n; i++)
                {
                    float s = r[i];
                    for (int j = 0; j < n; j++)
                    {
                        if (j != i)
                        {
                            s -= m[i, j] * x[j];
                        }
                    }
                    x[i] = Mathf.Max(0f, s / (m[i, i] + lambda));
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

        /// <summary>The angular momentum of a body about its centre of mass, in world axes.</summary>
        private static Vector3 Spin(Rigidbody body)
        {
            Quaternion frame = body.rotation * body.inertiaTensorRotation;
            Vector3 local = Quaternion.Inverse(frame) * body.angularVelocity;
            return frame * Vector3.Scale(body.inertiaTensor, local);
        }
    }
}
