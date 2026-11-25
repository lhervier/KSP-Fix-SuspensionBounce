using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using ModuleWheels;
using UnityEngine;
using VehiclePhysics;

namespace com.github.lhervier.ksp.fix.suspensions.vesselbodies
{
    /// <summary>
    /// The contact of every wheel of a vessel with the ground, read once per vessel and physics step for every fix that
    /// needs it: EndStopSlide, for the travel each wheel has left before its end-stop and the force its spring and damper
    /// will push it with over the coming step, and DamperBounce, for the wheels on the ground and where they touch it.
    /// <para>
    /// The contact is the one PhysX found for the wheel at the start of the last physics step, which Vehicle Physics Pro
    /// reads at the start of each wheel's own update: within a step, a wheel whose update has not run yet still holds
    /// the contact of the step before. The read asks PhysX itself, for every wheel at once. The wheels are those VPP
    /// updates, in the order VPP first updated them; one VPP no longer updates is dropped.
    /// </para>
    /// </summary>
    public sealed class WheelContacts
    {
        /// <summary>
        /// Whether the travel left is read where the wheel is at the start of the coming step, the contact PhysX reports
        /// being a step old (true), or as PhysX reports it (false). Public so that it can be changed in game
        /// (KSP-Util-MCPServer set_member).
        /// </summary>
        public static bool Predict = true;

        /// <summary>
        /// Whether the force of each wheel's spring and damper is the one PhysX will apply over the coming step (true),
        /// or the one it applied over the last step (false) (<see cref="Wheel.comingForce"/>). Public so that it can be
        /// changed in game (KSP-Util-MCPServer set_member).
        /// </summary>
        public static bool PredictForce = true;

        /// <summary>One wheel, and its contact as read at the last step its vessel was read.</summary>
        public sealed class Wheel
        {
            /// <summary>The wheel's state in VPP, which identifies it.</summary>
            internal VehicleBase.WheelState state;
            /// <summary>The body VPP pushes the wheel's forces on, and its part.</summary>
            internal Rigidbody body;
            internal Part part;
            /// <summary>The last step VPP updated the wheel.</summary>
            internal float seenTime = -1f;
            /// <summary>Whether the wheel is still followed; false once dropped, VPP no longer updating it.</summary>
            internal bool alive = true;

            // The contact, read at each step the wheel's vessel is read.
            internal bool grounded;
            internal WheelHit hit;
            /// <summary>The length of the suspension's travel (m).</summary>
            internal float travel;
            /// <summary>
            /// Contact depth over the travel, where PhysX's suspension really is: 0 fully extended, 1 on its end-stop
            /// (<see cref="ContactDepth"/>); 0 off the ground.
            /// </summary>
            internal float compression;
            /// <summary>The axis of the suspension, a unit vector, along which PhysX's end-stop works.</summary>
            internal Vector3 axis;
            /// <summary>
            /// A velocity's component on this vector is how fast the travel opens (positive) or closes: the axis over
            /// the square of its share along the ground normal. Zero off the ground.
            /// </summary>
            internal Vector3 rate;
            /// <summary>How fast the wheel's body closes its travel at the contact point (m/s); 0 off the ground.</summary>
            internal float closing;
            /// <summary>
            /// The travel left before the end-stop (m), where the wheel is at the start of the coming step when
            /// <see cref="Predict"/>: negative for a wheel coming fast that will be past its end-stop. 0 off the ground.
            /// </summary>
            internal float gap;
            /// <summary>
            /// The force, along the contact normal, that PhysX will push the wheel with for its spring and damper over
            /// the coming step (kN), when <see cref="PredictForce"/>; otherwise, or when it cannot be foreseen, the one
            /// it reported for the last step. 0 off the ground.
            /// </summary>
            internal float comingForce;

            // How much further into its travel PhysX will find the wheel at the start of the coming step than at the
            // start of the last one (m), as foreseen at the read of this step and at the read of the step before, and
            // that read's step and contact.
            internal float step;
            internal float readTime = -1f;
            internal float lastStep;
            internal float lastReadTime = -1f;
            internal bool lastGrounded;
        }

        private static readonly MethodInfo CalculateSuspensionTravel =
            AccessTools.DeclaredMethod(typeof(VehicleBase), "CalculateSuspensionTravel");

        private static readonly AccessTools.FieldRef<VehicleBase, Rigidbody> VehicleBody =
            AccessTools.FieldRefAccess<VehicleBase, Rigidbody>("m_rigidbody");

        /// <summary>Unity's wheel behind VPP's, whose spring and damper PhysX uses.</summary>
        private static readonly AccessTools.FieldRef<VPWheelCollider, WheelCollider> UnityWheel =
            AccessTools.FieldRefAccess<VPWheelCollider, WheelCollider>("m_wheelCollider");

        /// <summary>The wheel of a wheel submodule, internal to the game's assembly.</summary>
        internal static readonly AccessTools.FieldRef<ModuleWheelSubmodule, KSPWheelController> Controller =
            AccessTools.FieldRefAccess<ModuleWheelSubmodule, KSPWheelController>("wheel");

        private static bool installed;

        // Every wheel VPP updates, in the order it first did, and the same by state.
        private static readonly List<Wheel> all = new List<Wheel>();
        private static readonly Dictionary<VehicleBase.WheelState, Wheel> byState =
            new Dictionary<VehicleBase.WheelState, Wheel>();

        /// <summary>How many wheels have been followed since the game started.</summary>
        private static int followed;

        private static readonly Dictionary<Vessel, WheelContacts> vessels = new Dictionary<Vessel, WheelContacts>();

        /// <summary>The wheels of the vessel, as read at <see cref="time"/>.</summary>
        internal readonly List<Wheel> wheels = new List<Wheel>();
        /// <summary>How many of <see cref="wheels"/> touch the ground.</summary>
        internal int grounded;

        // The step, the floating origin and the wheels followed at the last read.
        private float time = -1f;
        private Vector3d origin;
        private int readFollowed;

        /// <summary>
        /// Follows the wheels VPP updates, for every fix that reads their contacts; does nothing once done. Called by
        /// each fix's Install, before it patches anything of its own.
        /// </summary>
        /// <exception cref="MissingMemberException">A member the service reads is missing from the game.</exception>
        internal static void Install(Harmony harmony)
        {
            if (installed)
            {
                return;
            }
            if (CalculateSuspensionTravel == null)
            {
                throw new MissingMemberException("VehicleBase.CalculateSuspensionTravel");
            }
            harmony.Patch(CalculateSuspensionTravel,
                postfix: new HarmonyMethod(typeof(WheelContacts), nameof(CalculateSuspensionTravelPostfix)));
            GameEvents.onGameSceneLoadRequested.Add(scene => Clear());
            GameEvents.onVesselDestroy.Add(vessel => vessels.Remove(vessel));
            installed = true;
        }

        private static void Clear()
        {
            foreach (Wheel w in all)
            {
                w.alive = false;
            }
            all.Clear();
            byState.Clear();
            vessels.Clear();
        }

        /// <summary>Notes that VPP has just updated the contact of <paramref name="wheel"/>.</summary>
        private static void CalculateSuspensionTravelPostfix(VehicleBase __instance, VehicleBase.WheelState wheel)
        {
            if (!HighLogic.LoadedSceneIsFlight || wheel == null || wheel.wheelCol == null)
            {
                return;
            }
            Wheel w = Track(__instance, wheel);
            if (w != null)
            {
                w.seenTime = Time.fixedTime;
            }
        }

        /// <summary>
        /// Returns the wheel of <paramref name="state"/>, followed from VPP's first update of it; null when VPP has not
        /// updated it yet, or it has no part.
        /// </summary>
        internal static Wheel Get(VehicleBase.WheelState state)
        {
            Wheel w;
            return state != null && byState.TryGetValue(state, out w) && w.body != null && w.part != null ? w : null;
        }

        /// <summary>Returns the wheel of a state VPP updates, followed from now on; null when the wheel has no part.</summary>
        private static Wheel Track(VehicleBase vehicle, VehicleBase.WheelState state)
        {
            Wheel w;
            if (byState.TryGetValue(state, out w))
            {
                if (w.body != null && w.part != null)
                {
                    return w;
                }
                Drop(w);
            }
            Rigidbody body = VehicleBody(vehicle);
            Part part = body != null ? body.GetComponent<Part>() : null;
            if (part == null)
            {
                return null;
            }
            w = new Wheel { state = state, body = body, part = part };
            byState[state] = w;
            all.Add(w);
            followed++;
            return w;
        }

        private static void Drop(Wheel w)
        {
            w.alive = false;
            all.Remove(w);
            byState.Remove(w.state);
        }

        /// <summary>
        /// Returns the wheels of <paramref name="vessel"/> with their contacts for the current physics step, read at the
        /// first call of the step, or again when the floating origin moved since or wheels were followed since.
        /// </summary>
        internal static WheelContacts Of(Vessel vessel)
        {
            WheelContacts found;
            if (!vessels.TryGetValue(vessel, out found))
            {
                found = new WheelContacts();
                vessels[vessel] = found;
            }
            found.Read(vessel);
            return found;
        }

        /// <summary>Returns the wheel of <paramref name="state"/> among those read, or null.</summary>
        internal Wheel Find(VehicleBase.WheelState state)
        {
            for (int i = 0; i < wheels.Count; i++)
            {
                if (wheels[i].state == state)
                {
                    return wheels[i];
                }
            }
            return null;
        }

        private void Read(Vessel vessel)
        {
            float now = Time.fixedTime;
            // Within a step the wheels do not move, but for a shift of the floating origin, which moves them all. The
            // first step of a vessel loaded or unpacked, every wheel has a new state, followed only once VPP updates it:
            // a read at the vessel's first wheel knows that wheel alone, and is made again once the others are followed.
            Vector3d shift = FloatingOrigin.fetch != null ? FloatingOrigin.fetch.offsetNonKrakensbane : Vector3d.zero;
            if (time == now && origin.x == shift.x && origin.y == shift.y && origin.z == shift.z &&
                readFollowed == followed)
            {
                return;
            }
            time = now;
            origin = shift;
            readFollowed = followed;
            float dt = Time.fixedDeltaTime;

            for (int i = all.Count - 1; i >= 0; i--)
            {
                Wheel w = all[i];
                // A wheel state VPP no longer updates is dropped: a vessel packed and unpacked again (time warp on
                // rails, a vessel moved) gets new wheel states on the same parts, and the old ones would be read as
                // wheels of their own. A wheel still in use is followed again at its next update.
                if (w.seenTime >= 0f && now - w.seenTime > 2.5f * dt)
                {
                    w.alive = false;
                    byState.Remove(w.state);
                    all.RemoveAt(i);
                }
            }

            wheels.Clear();
            grounded = 0;
            for (int i = 0; i < all.Count; i++)
            {
                Wheel w = all[i];
                if (w.part == null || w.body == null || w.part.vessel != vessel || w.state.wheelCol == null)
                {
                    continue;
                }
                wheels.Add(w);
                ReadContact(w, dt);
                if (w.grounded)
                {
                    grounded++;
                }
            }
        }

        /// <summary>Reads the contact of <paramref name="w"/> as PhysX reports it for the last step.</summary>
        private static void ReadContact(Wheel w, float dt)
        {
            // The read of the step before becomes the last one at the first read of a step; a read made again within
            // the same step (the floating origin moved, wheels followed since) keeps it.
            float now = Time.fixedTime;
            if (w.readTime != now)
            {
                w.lastStep = w.step;
                w.lastGrounded = w.grounded;
                w.lastReadTime = w.readTime;
                w.readTime = now;
            }
            VPWheelCollider col = w.state.wheelCol;
            w.grounded = Read(col, out w.hit, out w.travel, out w.compression);
            // The end-stop works along the axis of the suspension, as PhysX's does.
            w.axis = col.cachedTransform.up;
            w.rate = Vector3.zero;
            w.closing = 0f;
            w.gap = 0f;
            w.step = 0f;
            w.comingForce = 0f;
            if (!w.grounded)
            {
                return;
            }
            // PhysX measures the travel along the axis, down to the ground: a leg that leans out closes it faster than
            // the vessel comes down, and the vessel comes down faster than the leg moves along its axis. The rate at
            // which the travel opens, for a vessel coming straight down onto the ground, is the speed along the axis
            // over the square of the axis's share along the ground normal. Only the tilt of the axis to the ground is
            // taken from the ground normal, not its direction: a normal tilted for a single step, by a bump or a seam
            // between two pieces of terrain, would turn part of the forward speed into a speed towards the end-stop.
            float upright = Mathf.Max(0.5f, Vector3.Dot(w.hit.normal, w.axis));
            w.rate = w.axis / (upright * upright);
            w.closing = -Vector3.Dot(w.body.GetPointVelocity(w.hit.point), w.rate);
            w.gap = (1f - w.compression) * w.travel;
            // The contact is the one PhysX found at the start of the last step, the wheel where it was a step ago: the
            // wheel has since moved by its speed over that step. Read as it is, a wheel coming fast is a step short of
            // its end-stop when it is already past it, and PhysX's limit then pushes the whole overshoot out in one
            // step: a lander bounces off its legs. Only a wheel coming towards its end-stop is moved on: a wheel at
            // rest on it, rocking by a few millimetres as a rover drives, is read as it is.
            if (Predict)
            {
                w.gap -= Mathf.Max(0f, w.closing) * dt;
            }
            w.comingForce = ComingForce(w, col, now, dt);
        }

        /// <summary>
        /// Returns the force, along the contact normal, that PhysX will push the grounded wheel <paramref name="w"/> with
        /// for its spring and damper over the coming step (kN), from its contact read at this step; the force PhysX
        /// reported for the last step when <see cref="PredictForce"/> is off, or when the wheel was not read on the
        /// ground at the step before.
        /// </summary>
        private static float ComingForce(Wheel w, VPWheelCollider col, float now, float dt)
        {
            // PhysX's vehicle (PxVehicleUpdate.cpp, processSuspTireWheels) works the force out once, at the start of
            // each step, from where its ray finds the ground then, and applies it over the whole step: a spring on the
            // jounce, capped at the travel, and a damper on the jounce's change since the step before, over the step,
            // projected on the contact normal. Between the start of the last step and the start of the coming one, the
            // wheel's body has moved by its speed at the end of the last step, read now: the jounce changes by the
            // closing speed times the step, and the damper's speed is the closing speed itself. The compression the
            // read measures does not follow PhysX's ray closely enough for either.
            w.step = w.closing * dt;
            if (w.compression >= 1f && w.step > 0f)
            {
                // On its end-stop, PhysX caps the jounce: going further changes nothing.
                w.step = 0f;
            }
            float reported = w.hit.force;
            if (!PredictForce || !w.lastGrounded || Mathf.Abs(now - w.lastReadTime - dt) >= dt * 0.25f)
            {
                return reported;
            }
            if (Mathf.Min(w.compression, 1f) * w.travel + w.step <= 0f)
            {
                // The wheel leaves the ground.
                return 0f;
            }
            WheelCollider unity = UnityWheel(col);
            if (unity == null)
            {
                return reported;
            }
            // The force reported is that of the last step: what the spring and the damper gain since.
            JointSpring spring = unity.suspensionSpring;
            float along = Vector3.Dot(w.hit.normal, w.axis);
            return reported + along * (spring.spring * w.step + spring.damper * (w.step - w.lastStep) / dt);
        }

        /// <summary>
        /// Reads the contact of one wheel as PhysX reports it for the last step: whether it touches anything, the
        /// contact, the length of its travel (m) and its compression (0 fully extended, 1 on its end-stop). Returns
        /// false, the compression 0, when the wheel touches nothing or has no travel.
        /// </summary>
        internal static bool Read(VPWheelCollider col, out WheelHit hit, out float travel, out float compression)
        {
            travel = col.lastRuntimeSuspensionTravel;
            bool touches = col.GetGroundHit(out hit);
            compression = touches && travel > 0f ? ContactDepth(col, hit.point, travel) / travel : 0f;
            return touches;
        }

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
    }
}
