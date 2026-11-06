using System;
using System.Collections.Generic;
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
    /// And on every body, a spring sized on the load each wheel carries: the game sizes it on the vessel's weight shared
    /// equally between the wheels on the ground, and a wheel that carries more, as the lower one of a rover across a
    /// slope, sinks to the end of its travel. There, it is held by the stop of Unity's wheel collider, whose force is
    /// reported nowhere, and the tyre model of the game, which reads the load of a wheel from its spring, misses part of
    /// the weight: it holds the vessel with forces that do not match the slope, and the vessel creeps.
    /// </summary>
    internal static class SuspensionSpring
    {
        private static readonly MethodInfo SuspensionSpringUpdate =
            AccessTools.DeclaredMethod(typeof(ModuleWheelSuspension), "SuspensionSpringUpdate");

        private static readonly MethodInfo FixedUpdate =
            AccessTools.DeclaredMethod(typeof(ModuleWheelSuspension), "FixedUpdate");

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
        /// The compression a wheel is held at, at most, under the load it carries. A wheel at rest sinks to about 0.75 of
        /// its travel on Kerbin: the fix leaves alone a wheel that carries its share, and stiffens one that carries more.
        /// </summary>
        private const float LoadedCompression = 0.8f;

        /// <summary>
        /// Where the spring of Unity's wheel collider pushes from, in its travel, under a spring stiff enough to hold its
        /// wheel: VehiclePhysics.VPWheelCollider sets that target position to the weight of the wheel's own rigidbody,
        /// the part, over the spring times the travel, between 0.1 and 1. A spring set very soft moves it to the end of
        /// the travel, where the wheel pushes nothing: a TR-2L under a spring of 8 kN/m or less, on Kerbin.
        /// </summary>
        private const float SpringStart = 0.1f;

        /// <summary>
        /// How long the load of a wheel is averaged over, in seconds: the spring follows the load a wheel carries on a
        /// slope or under a heavy end of the vessel, not the hits of the ground nor the swing of the suspension. A spring
        /// that followed the compression of the step before would push more on the way up than on the way down, and keep
        /// the vessel swinging.
        /// </summary>
        private const float LoadTime = 1f;

        /// <summary>
        /// The most the spring is raised above the game's: the slider of the spring goes down to 0.05, a twentieth of
        /// the spring the game sizes for the wheel's share.
        /// </summary>
        private const float MaxFactor = 50f;

        /// <summary>What the fix keeps of a suspension whose wheel is on the ground.</summary>
        private sealed class State
        {
            /// <summary>The load of the wheel averaged over <see cref="LoadTime"/>, in kN.</summary>
            public float Load;

            /// <summary>How many times the game's spring the fix set at the last step; 1 when it left it.</summary>
            public float Factor = 1f;
        }

        private static readonly Dictionary<ModuleWheelSuspension, State> states =
            new Dictionary<ModuleWheelSuspension, State>();

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
            HarmonyMethod postfix = new HarmonyMethod(typeof(SuspensionSpring), nameof(SuspensionSpringUpdatePostfix));
            postfix.priority = Priority.High;
            harmony.Patch(SuspensionSpringUpdate, postfix: postfix);
            if (FixedUpdate == null)
            {
                throw new MissingMemberException("ModuleWheelSuspension.FixedUpdate");
            }
            // Ahead of the damper fix too, which sizes the damper off the ground from the one given back here.
            HarmonyMethod offGround = new HarmonyMethod(typeof(SuspensionSpring), nameof(FixedUpdatePostfix));
            offGround.priority = Priority.High;
            harmony.Patch(FixedUpdate, postfix: offGround);
            // The set holds no suspension of a scene that is gone.
            GameEvents.onGameSceneLoadRequested.Add(scene => states.Clear());
        }

        /// <summary>
        /// Takes the spring and damper SuspensionSpringUpdate has just computed and sets on the wheel collider the ones
        /// the fix sizes: under 1 g, redone with the gravity curve of Kerbin when that is stiffer; for a wheel that carries
        /// more than its share, raised until that load holds it at <see cref="LoadedCompression"/>. Leaves the wheel as the
        /// game set it when neither applies.
        /// </summary>
        private static void SuspensionSpringUpdatePostfix(ModuleWheelSuspension __instance)
        {
            ModuleWheelSuspension s = __instance;
            KSPWheelController wheel = Wheel(s);
            if (wheel == null || wheel.wheelCollider == null || s.vessel == null)
            {
                return;
            }
            bool geeFixed = Settings.KerbinCurve && ApplyKerbinCurve(s);

            float factor = 1f;
            float previous = 1f;
            if (Settings.LoadSpring)
            {
                float load = Mathf.Max(wheel.currentState.hit.force, 0f);
                State state;
                // From nothing at the first contact, whose force is a hit, not a load.
                if (!states.TryGetValue(s, out state))
                {
                    state = new State();
                    states[s] = state;
                }
                state.Load += (load - state.Load) * Mathf.Min(Time.fixedDeltaTime / LoadTime, 1f);
                previous = state.Factor;
                // At the end of its travel, the force the wheel reports is the spring's alone, short of its load: the
                // spring needed for it is still stiffer than the one it has, and the next steps raise it further. A
                // spring set so soft that the wheel pushes nothing reports no load at all: the wheel is held up at least
                // as its share of the vessel's weight among the wheels on the ground. The real share, not the one the
                // game sizes the spring for, which never falls below half a ton on Kerbin: a light vessel would get the
                // stiff spring, and the damper, of a heavy one.
                int grounded = Mathf.Max(SuspensionDamper.Count(s.vessel).Grounded, 1);
                float share = (float)(s.vessel.totalMass * s.vessel.mainBody.GeeASL *
                    PhysicsGlobals.GravitationalAcceleration) / grounded;
                float travel = wheel.wheelCollider.suspensionDistance;
                float needed = travel > 0f
                    ? Mathf.Max(state.Load, share) / (travel * (LoadedCompression - SpringStart))
                    : 0f;
                factor = Mathf.Clamp(needed / Mathf.Max(Spring(s), 0.1f), 1f, MaxFactor);
                state.Factor = factor;
            }
            if (factor <= 1f && previous <= 1f && !geeFixed)
            {
                return;
            }

            // The damper follows the square root of the spring, as the game's does: the same damping ratio. The damper
            // fix, which runs next, still limits it. Not rounded to the unit, as the game's are: a spring that would flip
            // between two units at every step would shake the wheel it holds.
            float spring = Mathf.Max(Spring(s) * factor, 0.1f);
            float damper = Mathf.Max(Damper(s) * Mathf.Sqrt(factor), 0.1f);
            Damper(s) = damper;
            if (factor > 1f || previous > 1f)
            {
                // The raised spring follows the averaged load at every step: no write threshold.
                wheel.wheelCollider.springRate = spring;
                wheel.wheelCollider.damperRate = damper;
                return;
            }
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

        /// <summary>
        /// Forgets the load of a suspension whose wheel left the ground, and gives back the game's spring and damper when
        /// the fix had raised them: the game only sizes them on the ground, and the wheel would hit the ground with them.
        /// </summary>
        private static void FixedUpdatePostfix(ModuleWheelSuspension __instance)
        {
            State state;
            if (!states.TryGetValue(__instance, out state))
            {
                return;
            }
            KSPWheelController wheel = Wheel(__instance);
            if (wheel == null || wheel.wheelCollider == null || wheel.IsGrounded)
            {
                return;
            }
            states.Remove(__instance);
            if (state.Factor > 1f)
            {
                Damper(__instance) = Mathf.Max(Damper(__instance) / Mathf.Sqrt(state.Factor), 0.1f);
                wheel.wheelCollider.springRate = Spring(__instance);
                wheel.wheelCollider.damperRate = Damper(__instance);
            }
        }

        /// <summary>
        /// Redoes, with the gravity curve of Kerbin, the spring and damper SuspensionSpringUpdate has just computed for a
        /// suspension on a body of less than 1 g, and keeps them in the module when the spring is stiffer. Returns
        /// whether it did.
        /// </summary>
        private static bool ApplyKerbinCurve(ModuleWheelSuspension s)
        {
            // The body's gravity alone: the game also scales it by Vessel.gravityMultiplier, which
            // FlightGlobals.ToggleVesselEaseIn lowers while it sets a vessel down, on Kerbin too. The fix is about light
            // bodies; while a vessel is eased onto Kerbin, its springs stay the game's.
            float gee = (float)s.vessel.mainBody.GeeASL;
            // The gravity curve is 1 at 1 g, below 1 under it, above 1 over it: only a lighter gravity weakens the boost.
            float geeCurve = BoostCurve(s, gee, 1f);
            float kerbinCurve = BoostCurve(s, 1f, 1f);
            if (geeCurve >= kerbinCurve)
            {
                return false;
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
                return false;
            }
            Boost(s) = boost;
            Spring(s) = spring;
            Damper(s) = damper;
            return true;
        }
    }
}
