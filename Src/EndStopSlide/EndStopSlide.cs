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
            /// (<see cref="WheelContacts.ContactDepth"/>).
            /// </summary>
            public float compression;
            /// <summary>Force PhysX reports for the spring and damper (kN).</summary>
            public float springForce;
            /// <summary>The tyre load as VPP would have computed it without the fix (kN).</summary>
            public float stockDownforce;

            internal VehicleBase.WheelState state;
            internal Part part;
            /// <summary>The wheel as the contacts' read follows it (<see cref="WheelContacts"/>).</summary>
            internal WheelContacts.Wheel wheel;
            /// <summary>Force added to the one VPP reads at this step (kN).</summary>
            internal float injected;
            internal float lastTime = -1f;
            internal Vector3 lastTyreForce;
            internal Vector3 lastTyrePoint;
            /// <summary>The contact PhysX reports for the last step (<see cref="WheelContacts"/>).</summary>
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
        /// <see cref="WheelContacts"/>, which follow the wheels, and the <see cref="ContactSolver"/>, which records KSP's
        /// forces while the fix is on.
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
            WheelContacts.Install(harmony);
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
            Probe probe = ProbeOf(WheelContacts.Get(wheel));
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
            KSPWheelController controller = WheelContacts.Controller(damage);
            return controller != null && controller.wheelState != null && controller.wheelState.Length > 0
                ? controller.wheelState[0] : null;
        }

        /// <summary>
        /// Returns the probe of a wheel the contacts' read follows, created at its first step; null for no wheel.
        /// </summary>
        private static Probe ProbeOf(WheelContacts.Wheel wheel)
        {
            if (wheel == null)
            {
                return null;
            }
            Probe probe;
            if (probes.TryGetValue(wheel.state, out probe))
            {
                if (probe.wheel == wheel)
                {
                    return probe;
                }
                // The read follows the state anew, its body or part gone: so does the probe.
                Probes.Remove(probe);
            }
            probe = new Probe
            {
                partName = wheel.part.name,
                persistentId = wheel.part.persistentId,
                state = wheel.state,
                wheel = wheel,
                body = wheel.body,
                part = wheel.part
            };
            probes[wheel.state] = probe;
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
        /// Once per physics step and vessel, at its first wheel: reads the contact of every wheel of the vessel
        /// (<see cref="WheelContacts"/>). While
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

            // Every wheel's contact, as PhysX reports it for the last step, the travel each has left moved on to where
            // the wheel is now (WheelContacts.Predict).
            WheelContacts read = WheelContacts.Of(vessel);
            for (int i = Probes.Count - 1; i >= 0; i--)
            {
                // The probe of a wheel the read no longer follows is dropped with it: a vessel packed and unpacked
                // again gets new wheel states on the same parts, and the old ones would be held as wheels of their own,
                // their share of the load given to tyres that no longer exist.
                Probe p = Probes[i];
                if (!p.wheel.alive)
                {
                    probes.Remove(p.state);
                    Probes.RemoveAt(i);
                }
            }

            // The vessel is followed when a wheel could reach its end-stop during the coming step: the travel it has
            // left is no more than what its speed towards the end-stop, plus a step of free fall, would cover.
            wheels.Clear();
            bool near = false;
            float fall = (float)vessel.graviticAcceleration.magnitude * dt;
            for (int i = 0; i < read.wheels.Count; i++)
            {
                WheelContacts.Wheel w = read.wheels[i];
                Probe p = ProbeOf(w);
                wheels.Add(p);
                p.grounded = w.grounded;
                p.hit = w.hit;
                p.axis = w.axis;
                p.compression = w.compression;
                p.springForce = w.grounded ? w.hit.force : 0f;
                p.gap = 0f;
                if (w.grounded)
                {
                    p.point = w.hit.point;
                    p.rate = w.rate;
                    near |= w.gap <= Mathf.Max(0f, w.closing + fall) * dt;
                    p.gap = Mathf.Max(0f, w.gap);
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
