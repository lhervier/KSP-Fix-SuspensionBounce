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
    /// Sizes the spring and the damper of each suspension so that it can no longer throw its vessel. Three stock flaws
    /// are taken care of. ModuleWheelSuspension.SuspensionSpringUpdate derives the damper from the spring alone, and the
    /// spring from a weight that SuspensionLoadBalancer never lets fall below half a ton: a light vessel gets the damper
    /// of a heavy one, which reverses its speed at each physics step instead of slowing it. The game only sizes a
    /// suspension while its wheel touches the ground, and keeps the last spring and damper while it does not, whatever
    /// happens to the vessel's mass meanwhile. And the suspension keeps pushing along its axis when its part lies on its
    /// side, its axis almost parallel to the ground, where the wheel's ray grazes the ground and reads a compression
    /// that jumps at the slightest rotation.
    /// </summary>
    internal static class SuspensionDamper
    {
        private static readonly MethodInfo SuspensionSpringUpdate =
            AccessTools.DeclaredMethod(typeof(ModuleWheelSuspension), "SuspensionSpringUpdate");

        private static readonly MethodInfo FixedUpdate =
            AccessTools.DeclaredMethod(typeof(ModuleWheelSuspension), "FixedUpdate");

        /// <summary>The wheel of a wheel submodule, internal to the game's assembly.</summary>
        private static readonly AccessTools.FieldRef<ModuleWheelSubmodule, KSPWheelController> Wheel =
            AccessTools.FieldRefAccess<ModuleWheelSubmodule, KSPWheelController>("wheel");

        /// <summary>The spring SuspensionSpringUpdate last computed, before it decides whether to set it.</summary>
        private static readonly AccessTools.FieldRef<ModuleWheelSuspension, float> StockSpring =
            AccessTools.FieldRefAccess<ModuleWheelSuspension, float>("spring");

        /// <summary>The damper SuspensionSpringUpdate last computed, before it decides whether to set it.</summary>
        private static readonly AccessTools.FieldRef<ModuleWheelSuspension, float> StockDamper =
            AccessTools.FieldRefAccess<ModuleWheelSuspension, float>("damper");

        /// <summary>The suspensions of a vessel, as SuspensionLoadBalancer lists them to share its weight.</summary>
        private static readonly AccessTools.FieldRef<SuspensionLoadBalancer, List<ModuleWheelSuspension>> Suspensions =
            AccessTools.FieldRefAccess<SuspensionLoadBalancer, List<ModuleWheelSuspension>>("suspensionModules");

        /// <summary>
        /// The angle between the suspension's axis and the local vertical up to which the suspension works in full;
        /// beyond it, its spring and damper fade out.
        /// </summary>
        private const float FullUpToDegrees = 30f;

        private static readonly float CosFullUpTo = Mathf.Cos(FullUpToDegrees * Mathf.Deg2Rad);

        /// <summary>How many suspensions a vessel has, and how many touch the ground.</summary>
        private struct SuspensionCount
        {
            public int All;
            public int Grounded;
        }

        /// <summary>The suspensions of each vessel, counted for the physics step <see cref="countStep"/>.</summary>
        private static readonly Dictionary<Vessel, SuspensionCount> counts = new Dictionary<Vessel, SuspensionCount>();
        private static float countStep = -1f;

        /// <summary>The suspensions whose damper is lowered to the limit.</summary>
        private static readonly HashSet<ModuleWheelSuspension> limited = new HashSet<ModuleWheelSuspension>();

        /// <summary>The suspensions whose spring and damper are faded for the angle of their axis.</summary>
        private static readonly HashSet<ModuleWheelSuspension> faded = new HashSet<ModuleWheelSuspension>();

        /// <summary>The suspensions already sized in MassBased mode, for one Debug line each.</summary>
        private static readonly HashSet<ModuleWheelSuspension> sized = new HashSet<ModuleWheelSuspension>();

        /// <summary>Patches the spring and damper update of the suspensions, on the ground and off it.</summary>
        /// <exception cref="MissingMemberException">A member the fix reads is missing from the game.</exception>
        public static void Install(Harmony harmony)
        {
            if (SuspensionSpringUpdate == null)
            {
                throw new MissingMemberException("ModuleWheelSuspension.SuspensionSpringUpdate");
            }
            if (FixedUpdate == null)
            {
                throw new MissingMemberException("ModuleWheelSuspension.FixedUpdate");
            }
            harmony.Patch(SuspensionSpringUpdate,
                postfix: new HarmonyMethod(typeof(SuspensionDamper), nameof(SuspensionSpringUpdatePostfix)));
            harmony.Patch(FixedUpdate,
                postfix: new HarmonyMethod(typeof(SuspensionDamper), nameof(FixedUpdatePostfix)));
            // No set holds a suspension of a scene that is gone.
            GameEvents.onGameSceneLoadRequested.Add(scene =>
            {
                limited.Clear();
                faded.Clear();
                sized.Clear();
            });
        }

        /// <summary>
        /// Replaces the spring and damper SuspensionSpringUpdate has just set on the wheel collider with the ones the
        /// mode sizes, for the share of the vessel the suspension carries among the grounded ones. The game calls it at
        /// every physics step where the wheel touches the ground.
        /// </summary>
        private static void SuspensionSpringUpdatePostfix(ModuleWheelSuspension __instance)
        {
            KSPWheelController wheel = Wheel(__instance);
            Vessel vessel = __instance.vessel;
            if (wheel == null || wheel.wheelCollider == null || vessel == null)
            {
                return;
            }
            SuspensionCount count = Count(vessel);
            float share = (float)vessel.totalMass / Mathf.Max(count.Grounded, 1);
            Apply(__instance, wheel, share, StockDamper(__instance), false);
        }

        /// <summary>
        /// Sizes the spring and damper of a suspension whose wheel is off the ground, which the game leaves as they were
        /// at the last contact, for the share of the vessel it will carry once all the suspensions of the vessel are on
        /// the ground.
        /// </summary>
        private static void FixedUpdatePostfix(ModuleWheelSuspension __instance)
        {
            KSPWheelController wheel = Wheel(__instance);
            Vessel vessel = __instance.vessel;
            if (!HighLogic.LoadedSceneIsFlight || wheel == null || wheel.wheelCollider == null || vessel == null ||
                __instance.part == null || __instance.part.packed || wheel.IsGrounded)
            {
                return;
            }
            // The spring and damper were sized for the mass and gravity of the last contact: since then, the vessel may
            // have burnt or transferred most of its mass, or, for a part placed alone in EVA construction, lost the 4 t
            // ModuleCargoPart.MakePartSettle gives it for 3 s; it may also have tipped over. The wheel pushes with them
            // at the first step of the next contact, before SuspensionSpringUpdate sizes them again. Each suspension
            // will then carry its share of the vessel: the share among all of them, the smallest it can be when they
            // all touch.
            SuspensionCount count = Count(vessel);
            float share = (float)vessel.totalMass / Mathf.Max(count.All, 1);
            // The wheel's own damper may already hold what the mod set at the previous step: start again from the
            // game's, as for the spring.
            Apply(__instance, wheel, share, StockDamper(__instance), true);
        }

        /// <summary>
        /// Sets on the wheel collider the spring and damper the mode sizes for a suspension carrying
        /// <paramref name="share"/> tons, or the mass the vessel sets in motion at its wheel when that is less, starting
        /// from <paramref name="stockDamper"/>, the damper the game last computed: faded with the angle of the
        /// suspension's axis, and the damper kept under what one physics step can take.
        /// </summary>
        private static void Apply(ModuleWheelSuspension suspension, KSPWheelController wheel, float share,
            float stockDamper, bool offGround)
        {
            Vector3 normal, point;
            float cos = LeanCosine(suspension, wheel, out normal, out point);

            // The share is the weight the suspension holds up when the vessel rests on its suspensions, but a damper
            // slows what moves at its wheel: pushed off the line of its centre of mass, the vessel turns as much as it
            // rises, and answers with less mass. A suspension at the end of a rover, alone on the ground while the
            // rest of the vessel stands on other parts, carries the whole vessel by the share and sets in motion a
            // fraction of it.
            VesselInertia inertia = VesselInertia.Of(suspension.vessel);
            if (inertia != null)
            {
                share = Mathf.Min(share, (float)inertia.EffectiveMass(point, normal));
            }
            if (share <= 0f)
            {
                return;
            }
            bool massBased = Settings.Mode == DamperMode.MassBased;
            float stockSpring = StockSpring(suspension);
            float damper = massBased ? MassBasedDamper(suspension, stockSpring, share) : stockDamper;

            float fade = Fade(cos);
            bool isFaded = fade < 1f;
            bool wasFaded = isFaded ? !faded.Add(suspension) : faded.Remove(suspension);
            damper *= fade;

            // A damper c slows a mass m by c * v / m * dt in a physics step of length dt: above m / dt, the step does
            // not stop the motion, it reverses it, and the next step reverses it again, faster. With the axis leaning
            // by an angle a on the surface hit, the compression the ray reads moves about 1 / cos^2 a faster than the
            // part: the mass the damper sees along the ray is m cos^2 a. The mass is the vessel's taken as rigid, but
            // a wheel at the end of trusses first sets in motion less than that, its joints giving way: the damper
            // takes off at most half the speed in a step.
            float limit = share * Mathf.Max(cos, 0f) * Mathf.Max(cos, 0f) / (2f * Time.fixedDeltaTime);
            bool isLimited = damper > limit;
            bool wasLimited;
            if (isLimited)
            {
                damper = limit;
                wasLimited = !limited.Add(suspension);
            }
            else if (offGround)
            {
                // Off the ground, the damper on the wheel is not the game's: one lowered earlier stays marked, so that
                // the game's own damper is set back at the next contact.
                wasLimited = false;
            }
            else
            {
                wasLimited = limited.Remove(suspension);
            }

            // MassBased replaces every damper. Minimal leaves the wheel alone unless it changes it, or has just stopped:
            // the game only sets a spring or a damper that differs from the wheel's by more than 3 or 1.05, and would
            // otherwise keep the mod's.
            if (massBased || isLimited || wasLimited || isFaded || wasFaded)
            {
                wheel.wheelCollider.damperRate = damper;
            }
            if (isFaded || wasFaded)
            {
                wheel.wheelCollider.springRate = stockSpring * fade;
            }

            if (Log.IsDebugEnabled)
            {
                LogChange(suspension, wheel, share, stockDamper, damper, fade, isLimited, wasLimited, isFaded, wasFaded,
                    offGround);
            }
        }

        /// <summary>
        /// Returns the damper sized from the spring and the mass the suspension carries, with the damping ratio the
        /// part's settings and the player's tweakable ask for, whatever the vessel's mass.
        /// </summary>
        private static float MassBasedDamper(ModuleWheelSuspension suspension, float spring, float mass)
        {
            // The stock damper is sqrt(spring * damperRatio * damperFudge) * damperTweakable, where damperFudge only
            // grows with the whole vessel's mass once it is large. The damping ratio of a spring k holding a mass m
            // with a damper c is c / (2 * sqrt(k * m)): putting the mass in place of damperFudge makes it
            // sqrt(damperRatio) * damperTweakable / 2 for every vessel, 0.74 for a landing leg left to its settings.
            return Mathf.Sqrt(Mathf.Max(spring * suspension.damperRatio * mass, 0f)) * suspension.damperTweakable;
        }

        /// <summary>
        /// Returns the cosine of the angle between the suspension's axis and the surface its wheel touches, or the
        /// local vertical when it touches none: 1 when the axis stands square on it, 0 when it lies flat on it,
        /// negative when it points away from it. Also gives that surface's normal, or the local vertical, and the
        /// point the wheel touches it at, or the wheel's position.
        /// </summary>
        private static float LeanCosine(ModuleWheelSuspension suspension, KSPWheelController wheel,
            out Vector3 normal, out Vector3 point)
        {
            // The wheel reads its compression along a ray cast down its axis, and that ray hits whatever collider lies
            // on its way, another vessel's side included: the lean that counts is the one on the surface hit. Off the
            // ground, the local vertical stands for the ground the wheel will touch first.
            VehicleBase.WheelState state = wheel.wheelState != null && wheel.wheelState.Length > 0
                ? wheel.wheelState[0] : null;
            if (state != null && state.grounded && state.hit.normal != Vector3.zero)
            {
                normal = state.hit.normal;
                point = state.hit.point;
            }
            else
            {
                point = wheel.wheelCollider.transform.position;
                normal = FlightGlobals.getUpAxis(suspension.vessel.mainBody, point);
            }
            return Vector3.Dot(wheel.SuspensionAxis, normal);
        }

        /// <summary>
        /// Returns the share of its spring and damper a suspension keeps for <paramref name="cos"/>, the cosine of the
        /// lean of its axis: all of them up to <see cref="FullUpToDegrees"/>, then less and less, none when the axis
        /// lies flat on the surface or points away from it.
        /// </summary>
        private static float Fade(float cos)
        {
            // The more the axis leans on the surface the ray hits, the more the ray grazes it, and the more a small
            // turn of the part moves the point it hits: the compression it reads, and so the force of its spring and
            // damper, change about 1 / cos^2 of the lean faster than the part moves. The square of the cosine, past
            // the angle where the suspension works in full, takes that back.
            if (cos >= CosFullUpTo)
            {
                return 1f;
            }
            if (cos <= 0f)
            {
                return 0f;
            }
            float ratio = cos / CosFullUpTo;
            return ratio * ratio;
        }

        /// <summary>Returns how many suspensions the vessel has, and how many have their wheel on the ground.</summary>
        private static SuspensionCount Count(Vessel vessel)
        {
            // Every suspension of a vessel asks at each step: count them once per vessel and step.
            if (Time.fixedTime != countStep)
            {
                counts.Clear();
                countStep = Time.fixedTime;
            }
            SuspensionCount count;
            if (counts.TryGetValue(vessel, out count))
            {
                return count;
            }
            SuspensionLoadBalancer balancer = vessel.FindVesselModuleImplementing<SuspensionLoadBalancer>();
            List<ModuleWheelSuspension> suspensions = balancer != null ? Suspensions(balancer) : null;
            // The balancer lists them once the vessel has started; until then, look them up.
            if (suspensions == null || suspensions.Count == 0)
            {
                suspensions = vessel.FindPartModulesImplementing<ModuleWheelSuspension>();
            }
            count.All = suspensions.Count;
            for (int i = 0; i < suspensions.Count; i++)
            {
                KSPWheelController wheel = Wheel(suspensions[i]);
                if (wheel != null && wheel.IsGrounded)
                {
                    count.Grounded++;
                }
            }
            counts[vessel] = count;
            return count;
        }

        /// <summary>
        /// Writes a Debug line when a suspension starts or stops being faded or limited, and, in MassBased mode, the
        /// first time a suspension is sized.
        /// </summary>
        private static void LogChange(ModuleWheelSuspension suspension, KSPWheelController wheel, float share,
            float stockDamper, float damper, float fade, bool isLimited, bool wasLimited, bool isFaded, bool wasFaded,
            bool offGround)
        {
            Part part = suspension.part;
            string name = $"{part.partInfo?.name} ({part.persistentId}) of {suspension.vessel.vesselName}";
            string where = offGround ? ", wheel off the ground" : "";
            string from = "KSP computed";
            if (isFaded && !wasFaded)
            {
                Log.Debug($"Suspension of {name} faded to {fade:0.###} for the lean of its axis{where}");
            }
            else if (!isFaded && wasFaded)
            {
                Log.Debug($"Suspension of {name} no longer faded");
            }
            if (isLimited && !wasLimited)
            {
                Log.Debug($"Damper of {name} limited to {damper:0.###} for {share:0.###} t carried{where}, {from} " +
                    $"{stockDamper:0.###}");
            }
            else if (!isLimited && wasLimited)
            {
                Log.Debug($"Damper of {name} no longer limited, {damper:0.###} for {share:0.###} t carried");
            }
            if (Settings.Mode == DamperMode.MassBased && sized.Add(suspension))
            {
                Log.Debug($"Damper of {name} sized to {damper:0.###} for {share:0.###} t carried{where} and a spring " +
                    $"of {wheel.wheelCollider.springRate:0.###}, {from} {stockDamper:0.###}");
            }
        }
    }
}
