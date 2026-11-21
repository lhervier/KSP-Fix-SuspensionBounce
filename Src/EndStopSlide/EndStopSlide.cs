using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using ModuleWheels;
using UnityEngine;
using VehiclePhysics;
using com.github.lhervier.ksp.fix.suspensions.vesselbodies;

namespace com.github.lhervier.ksp.fix.suspensions.endstopslide
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
    /// At every physics step, while a wheel of a vessel nears the end of its travel, the fix hands every wheel on the
    /// ground to the <see cref="ContactSolver"/> as a contact whose gap is the travel left before its end-stop: the
    /// vessel taken as one rigid body, as VPP expects, the solver finds the least forces, none pulling, that keep every
    /// wheel from going past the end of its travel by the end of the coming step, all wheels at once, and applies them
    /// to the wheels' bodies, which leaves PhysX's limit nothing to correct. The fix adds each to the force VPP reads
    /// for the load of its tyre.
    /// </para>
    /// </summary>
    public static class EndStopSlide
    {
        // Knobs, public so that they can be changed in game (KSP-Util-MCPServer set_member).

        /// <summary>Whether the fix runs at all (settings.cfg, endStopSlide).</summary>
        public static bool Enabled = true;

        /// <summary>Whether the fix applies the end-stop forces it finds to the wheels.</summary>
        public static bool Hold = true;

        /// <summary>Whether the fix adds the end-stop forces it applies to the load VPP reads for the tyres.</summary>
        public static bool Inject = true;

        /// <summary>
        /// Whether the travel left is read where the wheel is at the start of the coming step, the contact PhysX reports
        /// being a step old (true), or as PhysX reports it (false).
        /// </summary>
        public static bool PredictContact = true;

        /// <summary>
        /// What the fix found for one wheel at the last physics step, kept for reading in game; the wheel's contact for
        /// the solver, its gap the travel left before its end-stop, its force the end-stop force
        /// (<see cref="ContactSolver.Contact.held"/>).
        /// </summary>
        public class Probe : ContactSolver.Contact
        {
            public string partName;
            public uint persistentId;
            /// <summary>
            /// Contact depth over the travel, where PhysX's suspension really is: 0 fully extended, 1 on its end-stop
            /// (<see cref="ContactDepth"/>).
            /// </summary>
            public float compression;
            /// <summary>Force PhysX reports for the spring and damper (kN).</summary>
            public float springForce;
            /// <summary>The tyre load as VPP would have computed it without the fix (kN).</summary>
            public float stockDownforce;

            internal VehicleBase.WheelState state;
            internal Part part;
            /// <summary>Force added to the one VPP reads at this step (kN).</summary>
            internal float injected;
            internal float lastTime = -1f;
            internal Vector3 lastTyreForce;
            internal Vector3 lastTyrePoint;
            /// <summary>The contact PhysX reports for the last step, read before VPP reads it.</summary>
            internal WheelHit hit;
            internal bool grounded;
        }

        public static readonly List<Probe> Probes = new List<Probe>();

        private static readonly Dictionary<VehicleBase.WheelState, Probe> probes =
            new Dictionary<VehicleBase.WheelState, Probe>();

        private static readonly MethodInfo CalculateSuspensionForces =
            AccessTools.DeclaredMethod(typeof(VehicleBase), "CalculateSuspensionForces");
        private static readonly MethodInfo CalculateDownforce =
            AccessTools.DeclaredMethod(typeof(VehicleBase), "CalculateDownforce");
        private static readonly MethodInfo ApplyTireForce =
            AccessTools.DeclaredMethod(typeof(VehicleBase), "ApplyTireForce");
        private static readonly MethodInfo DamageFixedUpdate =
            AccessTools.DeclaredMethod(typeof(ModuleWheelDamage), "FixedUpdate");

        private static readonly AccessTools.FieldRef<VehicleBase, Rigidbody> VehicleBody =
            AccessTools.FieldRefAccess<VehicleBase, Rigidbody>("m_rigidbody");
        private static readonly AccessTools.FieldRef<ModuleWheelSubmodule, KSPWheelController> Wheel =
            AccessTools.FieldRefAccess<ModuleWheelSubmodule, KSPWheelController>("wheel");

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

        /// <summary>
        /// Patches VPP's suspension step and the wheel damage that reads the tyre load, and installs the
        /// <see cref="ContactSolver"/>, which records KSP's forces while the fix is on.
        /// </summary>
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
            if (ApplyTireForce == null)
            {
                throw new MissingMemberException("VehicleBase.ApplyTireForce");
            }
            ContactSolver.Install(harmony, () => Enabled);
            harmony.Patch(CalculateSuspensionForces,
                postfix: new HarmonyMethod(typeof(EndStopSlide), nameof(CalculateSuspensionForcesPostfix)));
            harmony.Patch(CalculateDownforce,
                postfix: new HarmonyMethod(typeof(EndStopSlide), nameof(CalculateDownforcePostfix)));
            harmony.Patch(ApplyTireForce,
                postfix: new HarmonyMethod(typeof(EndStopSlide), nameof(ApplyTireForcePostfix)));
            harmony.Patch(DamageFixedUpdate,
                prefix: new HarmonyMethod(typeof(EndStopSlide), nameof(DamageFixedUpdatePrefix)),
                postfix: new HarmonyMethod(typeof(EndStopSlide), nameof(DamageFixedUpdatePostfix)));
            GameEvents.onGameSceneLoadRequested.Add(scene => Clear());
        }

        private static void Clear()
        {
            Probes.Clear();
            probes.Clear();
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

        /// <summary>Returns the probe of a wheel, created at its first step; null when the wheel has no part.</summary>
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

        // The wheels of the vessel being solved, and the same as the solver's contacts: all of them, those on the
        // ground. Filled anew at each solve.
        private static readonly List<Probe> wheels = new List<Probe>();
        private static readonly List<ContactSolver.Contact> contacts = new List<ContactSolver.Contact>();
        private static readonly List<ContactSolver.Contact> touching = new List<ContactSolver.Contact>();
        private static readonly List<ContactSolver.KnownForce> known = new List<ContactSolver.KnownForce>();

        /// <summary>
        /// How deep (m) <paramref name="contactPoint"/> is into the travel of the wheel of <paramref name="col"/>, its
        /// suspension at <paramref name="travel"/>, where PhysX's <c>WheelCollider</c> really is: 0 at full extension,
        /// <paramref name="travel"/> on its end-stop.
        /// </summary>
        internal static float ContactDepth(VPWheelCollider col, Vector3 contactPoint, float travel)
        {
            // VPP's own depth leaves out the wheel's centre: VPP places Unity's collider at that centre, shifted along
            // the travel by the anchor, but GetContactDepth counts the anchor's shift only. A wheel whose part sets a
            // centre, such as the LT-1 leg (-0.119 m), would seem that much less compressed: short of its end-stop
            // while PhysX's end-stop already carries it.
            return col.GetContactDepth(contactPoint, travel) - col.center.y;
        }

        /// <summary>
        /// Once per physics step and vessel, at its first wheel: reads the contact of every wheel of the vessel. While
        /// one of them could reach its end-stop during the coming step, hands the wheels on the ground to the
        /// <see cref="ContactSolver"/>, each a contact along the axis of its suspension whose gap is the travel it has
        /// left, with the forces known to act on them over the last step (the suspension springs, the tyre forces), for
        /// it to hold them at their end-stops (when <see cref="Hold"/>) and check what PhysX's own limits pushed at the
        /// last step (<see cref="ContactSolver.Balance.unexplainedNormal"/>).
        /// </summary>
        private static void Solve(Vessel vessel)
        {
            ContactSolver.Balance balance = ContactSolver.Begin(vessel);
            if (balance == null)
            {
                return;
            }
            float now = Time.fixedTime;
            float dt = Time.fixedDeltaTime;

            wheels.Clear();
            for (int i = Probes.Count - 1; i >= 0; i--)
            {
                Probe p = Probes[i];
                // A wheel state VPP no longer updates is dropped: a vessel packed and unpacked again (time warp on
                // rails, a vessel moved) gets new wheel states on the same parts, and the old ones would be held as
                // wheels of their own, their share of the load given to tyres that no longer exist. A wheel still in
                // use gets a new probe at its next step.
                if (p.lastTime >= 0f && now - p.lastTime > 2.5f * dt)
                {
                    probes.Remove(p.state);
                    Probes.RemoveAt(i);
                }
            }
            for (int i = 0; i < Probes.Count; i++)
            {
                Probe p = Probes[i];
                if (p.part != null && p.part.vessel == vessel && p.state.wheelCol != null)
                {
                    wheels.Add(p);
                }
            }

            // Every wheel's contact, as PhysX reports it for the last step: the other wheels' VPP state is still that
            // of the step before. The vessel is followed when a wheel could reach its end-stop during the coming step:
            // the travel it has left is no more than what its speed towards the end-stop, plus a step of free fall,
            // would cover.
            bool near = false;
            float fall = (float)vessel.graviticAcceleration.magnitude * dt;
            for (int i = 0; i < wheels.Count; i++)
            {
                Probe p = wheels[i];
                VPWheelCollider col = p.state.wheelCol;
                p.grounded = col.GetGroundHit(out p.hit);
                float travel = col.lastRuntimeSuspensionTravel;
                // The end-stop works along the axis of the suspension, as PhysX's does.
                p.axis = col.cachedTransform.up;
                p.compression = p.grounded && travel > 0f ? ContactDepth(col, p.hit.point, travel) / travel : 0f;
                p.springForce = p.grounded ? p.hit.force : 0f;
                p.gap = 0f;
                if (p.grounded)
                {
                    p.point = p.hit.point;
                    // PhysX measures the travel along the axis, down to the ground: a leg that leans out closes it
                    // faster than the vessel comes down, and the vessel comes down faster than the leg moves along its
                    // axis. The rate at which the travel opens, for a vessel coming straight down onto the ground, is
                    // the speed along the axis over the square of the axis's share along the ground normal. Only the
                    // tilt of the axis to the ground is taken from the ground normal, not its direction: a normal
                    // tilted for a single step, by a bump or a seam between two pieces of terrain, would turn part of
                    // the forward speed into a speed towards the end-stop.
                    float upright = Mathf.Max(0.5f, Vector3.Dot(p.hit.normal, p.axis));
                    p.rate = p.axis / (upright * upright);
                    float closing = -Vector3.Dot(p.body.GetPointVelocity(p.hit.point), p.rate);
                    p.gap = (1f - p.compression) * travel;
                    // The contact is the one PhysX found at the start of the last step, the wheel where it was a step
                    // ago: the wheel has since moved by its speed over that step. Read as it is, a wheel coming fast is
                    // a step short of its end-stop when it is already past it, and PhysX's limit then pushes the whole
                    // overshoot out in one step: a lander bounces off its legs. Only a wheel coming towards its end-stop
                    // is moved on: a wheel at rest on it, rocking by a few millimetres as a rover drives, is held as
                    // before.
                    if (PredictContact)
                    {
                        p.gap -= Mathf.Max(0f, closing) * dt;
                    }
                    near |= p.gap <= Mathf.Max(0f, closing + fall) * dt;
                    p.gap = Mathf.Max(0f, p.gap);
                }
            }
            if (!near || vessel.packed)
            {
                // No wheel can reach its end-stop: nothing held, and nothing to keep for the next step.
                ContactSolver.Idle(balance);
                return;
            }

            // The forces known to act on the wheels at the last step, besides those the solver applied: the spring and
            // damper, which PhysX applies along the contact normal, and the tyre force VPP applied.
            contacts.Clear();
            touching.Clear();
            known.Clear();
            Vector3 normal = Vector3.zero;
            for (int i = 0; i < wheels.Count; i++)
            {
                Probe p = wheels[i];
                contacts.Add(p);
                if (!p.grounded)
                {
                    continue;
                }
                known.Add(new ContactSolver.KnownForce { force = p.hit.normal * p.springForce, point = p.hit.point });
                if (p.lastTime >= 0f && Mathf.Abs(now - p.lastTime - dt) < dt * 0.25f)
                {
                    known.Add(new ContactSolver.KnownForce { force = p.lastTyreForce, point = p.lastTyrePoint });
                }
                normal += p.hit.normal;
                touching.Add(p);
            }
            ContactSolver.Solve(balance, contacts, touching, known, normal, Hold);
        }
    }
}
