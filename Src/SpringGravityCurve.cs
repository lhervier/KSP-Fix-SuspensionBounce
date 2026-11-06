using System;
using System.Reflection;
using HarmonyLib;
using ModuleWheels;
using UnityEngine;

namespace com.github.lhervier.ksp.fix.suspensionbounce
{
    /// <summary>
    /// Stiffens the spring of a suspension on a body of less than Kerbin's gravity, so that it no longer bottoms out
    /// there. ModuleWheelSuspension.SuspensionSpringUpdate sizes the spring for the weight the wheel carries, then
    /// boosts it as the suspension sinks, at a pace set by a curve of the body's gravity: the whole boost on Kerbin, a
    /// tenth of it on the Mun, none on Minmus. A heavy rover sinks to the end of its travel there, where the spring only
    /// holds up part of its weight. Below 1 g, the curve is held at its value on Kerbin, 1: the weight already follows
    /// the gravity, so a suspension sinks as far as on Kerbin. In automatic and manual mode alike, since the curve is not
    /// one of the sliders; never to a softer spring than the game's.
    /// </summary>
    internal static class SpringGravityCurve
    {
        /// <summary>Whether the fix runs (settings.cfg, kerbinCurve).</summary>
        public static bool Enabled = true;

        private static readonly MethodInfo SuspensionSpringUpdate =
            AccessTools.DeclaredMethod(typeof(ModuleWheelSuspension), "SuspensionSpringUpdate");

        private delegate float BoostCurveDelegate(ModuleWheelSuspension suspension, float b, float power);

        /// <summary>The game's boost curve, 1 / |1 - 2 / b^power| rounded to a tenth.</summary>
        private static readonly BoostCurveDelegate BoostCurve = AccessTools.MethodDelegate<BoostCurveDelegate>(
            AccessTools.DeclaredMethod(typeof(ModuleWheelSuspension), "BoostCurve"));

        private static readonly AccessTools.FieldRef<ModuleWheelSubmodule, KSPWheelController> Wheel =
            AccessTools.FieldRefAccess<ModuleWheelSubmodule, KSPWheelController>("wheel");
        private static readonly AccessTools.FieldRef<ModuleWheelSuspension, float> Spring =
            AccessTools.FieldRefAccess<ModuleWheelSuspension, float>("spring");
        private static readonly AccessTools.FieldRef<ModuleWheelSuspension, float> Damper =
            AccessTools.FieldRefAccess<ModuleWheelSuspension, float>("damper");
        private static readonly AccessTools.FieldRef<ModuleWheelSuspension, float> Boost =
            AccessTools.FieldRefAccess<ModuleWheelSuspension, float>("boost");
        private static readonly AccessTools.FieldRef<ModuleWheelSuspension, float> BoostClamp =
            AccessTools.FieldRefAccess<ModuleWheelSuspension, float>("boostClamp");
        private static readonly AccessTools.FieldRef<ModuleWheelSuspension, float> DamperFudge =
            AccessTools.FieldRefAccess<ModuleWheelSuspension, float>("damperFudge");
        private static readonly AccessTools.FieldRef<ModuleWheelSuspension, float> SpringClampMax =
            AccessTools.FieldRefAccess<ModuleWheelSuspension, float>("springClampMax");
        private static readonly AccessTools.FieldRef<ModuleWheelSuspension, float> DamperClampMax =
            AccessTools.FieldRefAccess<ModuleWheelSuspension, float>("damperClampMax");
        private static readonly AccessTools.FieldRef<ModuleWheelSuspension, float> OscillationDamper =
            AccessTools.FieldRefAccess<ModuleWheelSuspension, float>("oscillationDamper");

        /// <summary>
        /// Patches the spring update of the suspensions, ahead of the damper fix, which starts from the damper set here.
        /// </summary>
        /// <exception cref="MissingMemberException">A member of the game the fix needs is missing.</exception>
        public static void Install(Harmony harmony)
        {
            if (SuspensionSpringUpdate == null)
            {
                throw new MissingMemberException("ModuleWheelSuspension.SuspensionSpringUpdate");
            }
            HarmonyMethod postfix = new HarmonyMethod(typeof(SpringGravityCurve), nameof(SuspensionSpringUpdatePostfix));
            postfix.priority = Priority.High;
            harmony.Patch(SuspensionSpringUpdate, postfix: postfix);
        }

        /// <summary>
        /// Redoes, with the gravity curve of Kerbin, the spring and damper SuspensionSpringUpdate has just computed for a
        /// suspension on a body of less than 1 g, and sets them, when the spring is stiffer, in the module and on the
        /// wheel collider, with the game's write thresholds. Leaves the wheel as the game set it otherwise.
        /// </summary>
        private static void SuspensionSpringUpdatePostfix(ModuleWheelSuspension __instance)
        {
            ModuleWheelSuspension s = __instance;
            if (!Enabled)
            {
                return;
            }
            KSPWheelController wheel = Wheel(s);
            if (wheel == null || wheel.wheelCollider == null || s.vessel == null || s.vessel.mainBody == null)
            {
                return;
            }
            // The body's gravity alone: the game also scales it by Vessel.gravityMultiplier, which
            // FlightGlobals.ToggleVesselEaseIn lowers while it sets a vessel down, on Kerbin too. The fix is about light
            // bodies; while a vessel is eased onto Kerbin, its springs stay the game's.
            float gee = (float)s.vessel.mainBody.GeeASL;
            // The gravity curve is 1 at 1 g, below 1 under it, above 1 over it: only a lighter gravity weakens the boost.
            float geeCurve = BoostCurve(s, gee, 1f);
            float kerbinCurve = BoostCurve(s, 1f, 1f);
            if (geeCurve >= kerbinCurve)
            {
                return;
            }

            // The stock formulas, with the gravity curve of Kerbin. The high gravity correction does not apply under
            // 1 g.
            float boost = 1f + Mathf.Clamp(s.suspCompression * kerbinCurve, -1f, BoostClamp(s));
            float spring = Mathf.Clamp(s.springTweakable * s.springRatio * s.sprungMassGravity *
                BoostCurve(s, boost, s.boostRatio + s.autoBoost), 0.01f, SpringClampMax(s));
            float damper = Mathf.Sqrt(Mathf.Clamp(spring * s.damperRatio * DamperFudge(s), 0.01f, DamperClampMax(s))) *
                s.damperTweakable;
            damper = Mathf.Max(Mathf.Round(damper), 0.1f);
            spring = Mathf.Max(Mathf.Round(spring), 0.1f);
            if (spring <= Spring(s))
            {
                return;
            }
            Boost(s) = boost;
            Spring(s) = spring;
            Damper(s) = damper;
            // The same write thresholds as the game's, so that a spring that settles is not written at every step.
            if (Mathf.Abs(spring - wheel.wheelCollider.springRate) > OscillationDamper(s))
            {
                wheel.wheelCollider.springRate = spring;
            }
            if (Mathf.Abs(damper - wheel.wheelCollider.damperRate) > 1.05f)
            {
                wheel.wheelCollider.damperRate = damper;
            }
        }
    }
}
