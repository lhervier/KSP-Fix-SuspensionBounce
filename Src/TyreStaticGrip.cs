using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using VehiclePhysics;

namespace com.github.lhervier.ksp.fix.suspensionbounce
{
    /// <summary>
    /// Gives a slow tyre the static grip it lacks, so that a parked rover no longer creeps down a slope or turns on its
    /// own.
    /// <para>
    /// Below about 1 m/s at the contact, Vehicle Physics Pro holds a tyre with a force proportional to the speed of the
    /// contact point (VehicleBase.CalculateLocalFrame: tireImpulseRatio x carried mass x speed / step, plus the surface
    /// force), with no memory of the way travelled: a force that lasts, the slope the surface force misses or the torque
    /// of uneven ground normals under free wheels, is only held while the tyre slides at a constant speed, whatever its
    /// friction.
    /// </para>
    /// <para>
    /// The fix adds a spring towards an anchor point at each wheel: the way the contact travelled since it came all but
    /// to rest (<see cref="HoldSpeed"/>), times the carried mass over <see cref="SpringTime"/> squared. The way is that
    /// of the vessel's wheels on the ground taken as one rigid body (<see cref="RigidAnchor"/>,
    /// <see cref="GroundedAnchor"/>).
    /// Sideways always; along the wheel only while it brakes, since a free wheel must roll. The anchor has no limit of its
    /// own: VPP's tyre friction caps what it passes on, and when it does, the anchor is moved to hold only what the tyre
    /// passed on (the tyre slips, and grips again where it stops), so that a torque beyond the grip, such as a reaction
    /// wheel's, still turns the rover as without the fix.
    /// </para>
    /// </summary>
    public static class TyreStaticGrip
    {
        // Knobs, public so that they can be changed in game (KSP-Util-MCPServer set_member).

        /// <summary>Whether the fix runs at all (settings.cfg, staticGrip).</summary>
        public static bool Enabled = true;

        /// <summary>Whether the anchor also holds along the wheel while it brakes.</summary>
        public static bool HoldWhenBraking = true;

        /// <summary>
        /// Whether the anchor follows the vessel taken as one rigid body at the contact (true), or the contact point of
        /// the wheel's own body (false, the prototype). A wheel's own body jitters on its suspension and joints: the
        /// anchors would sum that jitter, each its own, into forces of the wheels against each other that the vessel,
        /// rigid, never relieves by moving, and that end up taking the tyres' whole grip.
        /// </summary>
        public static bool RigidAnchor = true;

        /// <summary>
        /// Whether the rigid body the anchor follows is made of the bodies of the vessel's wheels on the ground (true),
        /// or of all the vessel's bodies (false). What a vessel carries above its wheels may move on its own: a load
        /// swinging from a crane carries momentum that the whole vessel, taken as one rigid body, reads as a turn of
        /// its wheels, and the anchors would push the wheels in time with the swing, feeding it.
        /// </summary>
        public static bool GroundedAnchor = true;

        /// <summary>
        /// Speed of the contact over the ground (m/s) below which the anchor holds in full, and above which it lets go:
        /// a tyre that slides faster is VPP's to slow down, as in stock, since a touch-down, a skid or a turn driven by
        /// a reaction wheel is no lasting force to hold.
        /// </summary>
        public static float HoldSpeed = 0.05f;

        /// <inheritdoc cref="HoldSpeed"/>
        public static float ReleaseSpeed = 0.1f;

        /// <summary>Time constant of the anchor spring, in seconds: its stiffness is the carried mass over its square.</summary>
        public static float SpringTime = 0.15f;

        /// <summary>
        /// Share of the force asked of a tyre, and least force (kN), that the tyre must fall short of before its anchor is
        /// moved: below, the gap is the wheel solver's own rounding.
        /// </summary>
        public static float ReseatShare = 0.02f;

        /// <inheritdoc cref="ReseatShare"/>
        public static float ReseatTolerance = 1e-3f;

        /// <summary>How many times an anchor was moved because its tyre did not pass its whole force on, for checking.</summary>
        public static long Reseats;

        private static readonly MethodInfo CalculateLocalFrame =
            AccessTools.DeclaredMethod(typeof(VehicleBase), "CalculateLocalFrame");
        private static readonly MethodInfo ApplyTireForce =
            AccessTools.DeclaredMethod(typeof(VehicleBase), "ApplyTireForce");

        private static readonly AccessTools.FieldRef<VehicleBase, Rigidbody> VehicleBody =
            AccessTools.FieldRefAccess<VehicleBase, Rigidbody>("m_rigidbody");

        private static readonly ConditionalWeakTable<VehicleBase.WheelState, Anchor> anchors =
            new ConditionalWeakTable<VehicleBase.WheelState, Anchor>();

        /// <summary>One wheel's anchor, and what it asked of the tyre at the current step.</summary>
        private class Anchor
        {
            /// <summary>Way travelled by the contact from the anchor, in metres (x sideways, y along the wheel).</summary>
            public Vector2 offset;

            /// <summary>Force per metre of offset at this step (stiffness times fade), 0 while the anchor is off.</summary>
            public float gain;

            /// <summary>Force the anchor added to the tyre's external force at this step.</summary>
            public Vector2 force;

            /// <summary>Time.fixedTime of the step the anchor last held at.</summary>
            public float lastTime = float.NegativeInfinity;

            /// <summary>The wheel's part, found at the first step.</summary>
            public Part part;

            // TEMPORARY trace: the slides of this step, the anchor's (rigid) and the wheel's own.
            public Vector2 slide, ownSlide;
        }

        // TEMPORARY trace: one CSV line per anchored wheel and step while TracePath is set (KSP-Util-MCPServer
        // set_member); the file is closed when TracePath is set back to null.
        public static string TracePath;
        private static string traceOpen;
        private static System.IO.StreamWriter trace;

        private static void Trace(VehicleBase.WheelState wheel, Anchor anchor, Vector2 before, bool movedX, bool movedY)
        {
            if (TracePath != traceOpen)
            {
                trace?.Dispose();
                trace = null;
                traceOpen = TracePath;
                if (TracePath != null)
                {
                    trace = new System.IO.StreamWriter(TracePath, false);
                    trace.WriteLine("time,part,slideX,slideY,ownX,ownY,offX0,offY0,offX,offY,gain,anchorFX,anchorFY," +
                        "askedX,askedY,passedX,passedY,movedX,movedY,brake,load,adherentCap,wheelOmega");
                }
            }
            if (trace == null)
            {
                return;
            }
            System.Globalization.CultureInfo c = System.Globalization.CultureInfo.InvariantCulture;
            trace.WriteLine(string.Format(c,
                "{0:F2},{1},{2:G5},{3:G5},{4:G5},{5:G5},{6:G5},{7:G5},{8:G5},{9:G5},{10:G5},{11:G5},{12:G5},{13:G5}," +
                "{14:G5},{15:G5},{16:G5},{17},{18},{19:G5},{20:G5},{21:G5},{22:G5}",
                Time.fixedTime, anchor.part != null ? anchor.part.persistentId : 0u, anchor.slide.x, anchor.slide.y,
                anchor.ownSlide.x, anchor.ownSlide.y, before.x, before.y, anchor.offset.x, anchor.offset.y, anchor.gain,
                anchor.force.x, anchor.force.y, wheel.externalTireForce.x, wheel.externalTireForce.y, wheel.tireForce.x,
                wheel.tireForce.y, movedX ? 1 : 0, movedY ? 1 : 0, wheel.brakeTorque, wheel.hit.force,
                wheel.hit.force * 0f, wheel.angularVelocity));
        }

        /// <summary>Patches VPP's tyre step: the force asked of each tyre, and what the tyre passed on of it.</summary>
        /// <exception cref="MissingMemberException">A member the fix patches is missing from the game.</exception>
        public static void Install(Harmony harmony)
        {
            if (CalculateLocalFrame == null)
            {
                throw new MissingMemberException("VehicleBase.CalculateLocalFrame");
            }
            if (ApplyTireForce == null)
            {
                throw new MissingMemberException("VehicleBase.ApplyTireForce");
            }
            harmony.Patch(CalculateLocalFrame,
                postfix: new HarmonyMethod(typeof(TyreStaticGrip), nameof(CalculateLocalFramePostfix)));
            harmony.Patch(ApplyTireForce,
                prefix: new HarmonyMethod(typeof(TyreStaticGrip), nameof(ApplyTireForcePrefix)));
        }

        /// <summary>
        /// Adds the anchor's force to the external force of a slow tyre on the ground, once VPP has worked it out, and
        /// lets the anchor go when the tyre leaves the ground, is no longer slow, or missed a step (a vessel back from
        /// rails or loaded again starts from a new anchor).
        /// </summary>
        private static void CalculateLocalFramePostfix(VehicleBase __instance, VehicleBase.WheelState wheel)
        {
            if (!Enabled || wheel == null)
            {
                return;
            }
            Anchor anchor = anchors.GetOrCreateValue(wheel);
            anchor.gain = 0f;
            anchor.force = Vector2.zero;
            float now = Time.fixedTime;
            bool continued = now - anchor.lastTime < Time.fixedDeltaTime * 1.5f;
            anchor.lastTime = now;
            if (!continued)
            {
                anchor.offset = Vector2.zero;
            }
            if (!wheel.grounded)
            {
                anchor.offset = Vector2.zero;
                return;
            }
            Vector2 slide;
            if (!RigidAnchor || !RigidSlide(__instance, wheel, anchor, out slide))
            {
                slide = wheel.localWheelVelocity;
            }
            anchor.slide = slide;
            anchor.ownSlide = wheel.localWheelVelocity;
            // Static grip only: the anchor holds a contact all but at rest, and lets VPP stop one that moves, as in
            // stock. Faded as VPP fades its surface force too, on the wheel's own contact speed.
            float fade = Mathf.InverseLerp(ReleaseSpeed, HoldSpeed, slide.magnitude) *
                Mathf.InverseLerp(1f, 0.25f, wheel.wheelVelocity.sqrMagnitude);
            float gravity = __instance.gravity.Magnitude;
            if (fade <= 0f || gravity <= 0f || wheel.hit.force <= 0f)
            {
                anchor.offset = Vector2.zero;
                return;
            }
            // The carried mass, from the load VPP reads (the end-stop's share included when BumpStopLoad adds it).
            float stiffness = wheel.hit.force / gravity / (SpringTime * SpringTime);
            anchor.offset.x += slide.x * Time.deltaTime;
            // brakeTorque is the value the wheel solver kept from the last step.
            if (HoldWhenBraking && wheel.brakeTorque > 0f)
            {
                anchor.offset.y += slide.y * Time.deltaTime;
            }
            else
            {
                anchor.offset.y = 0f;
            }
            anchor.gain = stiffness * fade;
            anchor.force = -anchor.gain * anchor.offset;
            wheel.externalTireForce += anchor.force;
        }

        /// <summary>
        /// Gives the velocity over the ground, at the wheel's contact point, of the wheel's vessel taken as one rigid
        /// body (its wheels on the ground only, with <see cref="GroundedAnchor"/>), in the tyre's axes (x sideways,
        /// y forward), as VPP gives the wheel's own in localWheelVelocity. Returns false when the wheel has no part in a
        /// vessel in physics, or the rigid motion cannot be had (no wheel on the ground at the step before).
        /// </summary>
        private static bool RigidSlide(VehicleBase vehicle, VehicleBase.WheelState wheel, Anchor anchor, out Vector2 slide)
        {
            slide = Vector2.zero;
            if (anchor.part == null)
            {
                Rigidbody body = VehicleBody(vehicle);
                anchor.part = body != null ? body.GetComponent<Part>() : null;
                if (anchor.part == null)
                {
                    return false;
                }
            }
            Vessel vessel = anchor.part.vessel;
            if (vessel == null || vessel.packed)
            {
                return false;
            }
            VesselBodies bodies = VesselBodies.Of(vessel);
            Vector3 centre, velocity, angularVelocity;
            if (GroundedAnchor)
            {
                // The wheels on the ground at the step before: this step's are not all known yet.
                bodies.MarkGrounded(VehicleBody(vehicle));
                if (!bodies.GroundedRigidMotion(out centre, out velocity, out angularVelocity))
                {
                    return false;
                }
            }
            else if (!bodies.RigidMotion(out centre, out velocity, out angularVelocity))
            {
                return false;
            }
            Vector3 point = wheel.hit.point;
            Vector3 v = velocity + Vector3.Cross(angularVelocity, point - centre);
            Rigidbody ground = wheel.hit.collider != null ? wheel.hit.collider.attachedRigidbody : null;
            if (ground != null)
            {
                v -= ground.GetPointVelocity(point);
            }
            slide = new Vector2(Vector3.Dot(wheel.hit.sidewaysDir, v), Vector3.Dot(wheel.hit.forwardDir, v));
            return true;
        }

        /// <summary>
        /// Moves the anchor when the tyre did not pass its whole force on, before VPP applies the tyre force: tireForce
        /// holds what the wheel solver let through, externalTireForce what was asked of it.
        /// </summary>
        private static void ApplyTireForcePrefix(VehicleBase.WheelState wheel)
        {
            Anchor anchor;
            if (wheel == null || !anchors.TryGetValue(wheel, out anchor))
            {
                return;
            }
            if (anchor.gain <= 0f)
            {
                if ((TracePath != null || trace != null) && wheel.grounded)
                {
                    Trace(wheel, anchor, anchor.offset, false, false);
                }
                return;
            }
            bool moved = false;
            Vector2 before = anchor.offset;
            anchor.offset.x = Reseat(anchor.offset.x, anchor.force.x, wheel.externalTireForce.x, wheel.tireForce.x,
                anchor.gain, ref moved);
            bool movedX = moved;
            if (anchor.offset.y != 0f)
            {
                anchor.offset.y = Reseat(anchor.offset.y, anchor.force.y, wheel.externalTireForce.y, wheel.tireForce.y,
                    anchor.gain, ref moved);
            }
            if (moved)
            {
                Reseats++;
            }
            if (TracePath != null || trace != null)
            {
                Trace(wheel, anchor, before, movedX, moved && !movedX || anchor.offset.y != before.y);
            }
        }

        /// <summary>
        /// Returns the offset whose force is the part of <paramref name="anchorForce"/> the tyre passed on: the offset
        /// unchanged when the tyre passed it all, smaller when it passed part of it, 0 when it passed none; sets
        /// <paramref name="moved"/> in the last two cases.
        /// </summary>
        private static float Reseat(float offset, float anchorForce, float asked, float passed, float gain,
            ref bool moved)
        {
            if (anchorForce == 0f)
            {
                return offset;
            }
            // What the tyre passed on beyond VPP's own force, along the anchor's force.
            float anchorPassed = passed - (asked - anchorForce);
            float sign = Mathf.Sign(anchorForce);
            float shortfall = Mathf.Abs(anchorForce) - anchorPassed * sign;
            // VPP's wheel solver gives back the impulse its integration found, a few tenths of a percent off the force
            // asked even when the tyre holds, either way: an anchor moved for that would only ever shrink, and the
            // vessel would creep. Only a tyre short of the force asked by more than that is taken as held back.
            if (shortfall <= Mathf.Max(ReseatTolerance, ReseatShare * Mathf.Abs(asked)))
            {
                return offset;
            }
            moved = true;
            return anchorPassed * sign <= 0f ? 0f : -anchorPassed / gain;
        }
    }
}
