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
    /// to rest (<see cref="HoldSpeed"/>), times the carried mass over <see cref="SpringTime"/> squared. The way is the
    /// one VPP reads at the contact (localWheelVelocity), which PoseMotion takes from how the wheel really moved: from the
    /// wheel's Rigidbody velocity, the anchors would sum the steady velocities the joints leave there into forces of the
    /// wheels against each other. Sideways always; along the wheel only while it brakes, since a free wheel must roll. The anchor has no limit of its
    /// own: VPP's tyre friction caps what it passes on, and when it does, the anchor is moved to hold only what the tyre
    /// passed on (the tyre slips, and grips again where it stops), so that a torque beyond the grip, such as a reaction
    /// wheel's, still turns the rover as without the fix.
    /// </para>
    /// </summary>
    public static class TyreStaticGrip
    {
        // Knobs, public so that they can be changed in game (KSP-Util-MCPServer set_member).

        /// <summary>
        /// Whether the fix runs at all (settings.cfg, staticGrip). Off by default: VPP smooths the tyre force before
        /// applying it (VehicleBase.ApplyTireForce, tireForceSharpness), so the anchor's spring acts late, and a late
        /// spring feeds energy into a light vessel instead of taking it out: a probe of 100 kg on three LT-05 legs, whose
        /// smoothing is ten times slower than other wheels', turns to and fro on itself, ever wider.
        /// </summary>
        public static bool Enabled = false;

        /// <summary>Whether the anchor also holds along the wheel while it brakes.</summary>
        public static bool HoldWhenBraking = true;

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
        private static readonly MethodInfo WheelSetup =
            AccessTools.DeclaredMethod(typeof(ModuleWheelBase), "wheelSetup");

        /// <summary>
        /// The stock LT-05 leg, whose config sets tireForceSharpness to 1 where every other stock wheel has the default
        /// of 10: VPP then applies its tyre force about a second late, and the anchor's spring with it, which makes a
        /// light probe on three of them swing on itself without end.
        /// </summary>
        private const string MiniLeg = "miniLandingLeg";

        /// <summary>The tyre force smoothing the fix gives the LT-05 leg: the default of every other stock wheel.</summary>
        private const float MiniLegSharpness = 10f;

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

            /// <summary>How much of the anchor holds at this step, 0 to 1 (the slide speed and VPP's fade).</summary>
            public float fade;

            /// <summary>Whether the anchor was moved at this step because the tyre did not pass its force on.</summary>
            public bool reseated;

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
            if (WheelSetup == null)
            {
                Log.Warning("ModuleWheelBase.wheelSetup not found: the LT-05 leg keeps its slow tyre force smoothing");
            }
            else
            {
                harmony.Patch(WheelSetup,
                    prefix: new HarmonyMethod(typeof(TyreStaticGrip), nameof(WheelSetupPrefix)));
            }
        }

        /// <summary>
        /// Gives the stock LT-05 leg the tyre force smoothing of every other stock wheel, before KSP hands it to VPP.
        /// tireForceSharpness is read from the part's config and never saved, so the change lasts as long as the part.
        /// </summary>
        private static void WheelSetupPrefix(ModuleWheelBase __instance)
        {
            AvailablePart info = __instance.part != null ? __instance.part.partInfo : null;
            if (info != null && info.name == MiniLeg && __instance.tireForceSharpness < MiniLegSharpness)
            {
                __instance.tireForceSharpness = MiniLegSharpness;
            }
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
            anchor.fade = 0f;
            anchor.reseated = false;
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
            Vector2 slide = wheel.localWheelVelocity;
            // brakeTorque is the value the wheel solver kept from the last step.
            bool braking = HoldWhenBraking && wheel.brakeTorque > 0f;
            // Static grip only: the anchor holds a contact all but at rest, and lets VPP stop one that moves, as in
            // stock. A free wheel rolls: only its sideways slide counts. Faded as VPP fades its surface force too, on
            // the wheel's own contact speed.
            float sliding = braking ? slide.magnitude : Mathf.Abs(slide.x);
            float fade = Mathf.InverseLerp(ReleaseSpeed, HoldSpeed, sliding) *
                Mathf.InverseLerp(1f, 0.25f, wheel.wheelVelocity.sqrMagnitude);
            anchor.fade = fade;
            float gravity = __instance.gravity.Magnitude;
            if (fade <= 0f || gravity <= 0f || wheel.hit.force <= 0f)
            {
                anchor.offset = Vector2.zero;
                return;
            }
            // The carried mass, from the load VPP reads (the end-stop's share included when BumpStopLoad adds it).
            float stiffness = wheel.hit.force / gravity / (SpringTime * SpringTime);
            anchor.offset.x += slide.x * Time.deltaTime;
            if (braking)
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
        /// Reads the anchor of <paramref name="wheel"/> at the current physics step, for KSP Diag - Suspension, which
        /// calls it by reflection: the way the contact travelled from it (m, x sideways, y along the wheel), the force it
        /// added to the tyre (kN), its stiffness at this step (kN/m), how much of it holds (0 to 1), and whether it was
        /// moved at this step. Returns false, every value at zero, when the fix is off, or the wheel has no anchor
        /// worked out at this step.
        /// </summary>
        public static bool TryGetAnchor(VehicleBase.WheelState wheel, out Vector2 offset, out Vector2 force,
            out float gain, out float fade, out bool reseated)
        {
            offset = Vector2.zero;
            force = Vector2.zero;
            gain = 0f;
            fade = 0f;
            reseated = false;
            Anchor anchor;
            if (!Enabled || wheel == null || !anchors.TryGetValue(wheel, out anchor) ||
                Time.fixedTime - anchor.lastTime >= Time.fixedDeltaTime * 1.5f)
            {
                return false;
            }
            offset = anchor.offset;
            force = anchor.force;
            gain = anchor.gain;
            fade = anchor.fade;
            reseated = anchor.reseated;
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
                return;
            }
            bool moved = false;
            anchor.offset.x = Reseat(anchor.offset.x, anchor.force.x, wheel.externalTireForce.x, wheel.tireForce.x,
                anchor.gain, ref moved);
            if (anchor.offset.y != 0f)
            {
                anchor.offset.y = Reseat(anchor.offset.y, anchor.force.y, wheel.externalTireForce.y, wheel.tireForce.y,
                    anchor.gain, ref moved);
            }
            anchor.reseated = moved;
            if (moved)
            {
                Reseats++;
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
