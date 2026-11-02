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
    /// Sizes the damper of each suspension so that it can no longer throw its vessel. Two stock flaws are taken care
    /// of. ModuleWheelSuspension.SuspensionSpringUpdate derives the damper from the spring alone, and the spring from a
    /// weight that SuspensionLoadBalancer never lets fall below half a ton: a light vessel gets the damper of a heavy
    /// one, which reverses its speed at each physics step instead of slowing it. And the game only sizes a suspension
    /// while its wheel touches the ground, and keeps the last spring and damper while it does not, whatever happens to
    /// the vessel's mass meanwhile.
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

        /// <summary>The damper SuspensionSpringUpdate last computed, before it decides whether to set it.</summary>
        private static readonly AccessTools.FieldRef<ModuleWheelSuspension, float> StockDamper =
            AccessTools.FieldRefAccess<ModuleWheelSuspension, float>("damper");

        /// <summary>The suspensions of a vessel, as SuspensionLoadBalancer lists them to share its weight.</summary>
        private static readonly AccessTools.FieldRef<SuspensionLoadBalancer, List<ModuleWheelSuspension>> Suspensions =
            AccessTools.FieldRefAccess<SuspensionLoadBalancer, List<ModuleWheelSuspension>>("suspensionModules");

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
            // The set holds no suspension of a scene that is gone.
            GameEvents.onGameSceneLoadRequested.Add(scene => limited.Clear());
        }

        /// <summary>
        /// Replaces the damper SuspensionSpringUpdate has just set on the wheel collider with the one the fix sizes,
        /// for the share of the vessel the suspension carries among the grounded ones. The game calls it at
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
        /// Sizes the damper of a suspension whose wheel is off the ground, which the game leaves as it was at the last
        /// contact, for the share of the vessel it will carry once all the suspensions of the vessel are on the ground.
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
            // ModuleCargoPart.MakePartSettle gives it for 3 s. The wheel pushes with them at the first step of the next
            // contact, before SuspensionSpringUpdate sizes them again. Each suspension will then carry its share of the
            // vessel: the share among all of them, the smallest it can be when they all touch.
            SuspensionCount count = Count(vessel);
            float share = (float)vessel.totalMass / Mathf.Max(count.All, 1);
            // The wheel's own damper may already hold what the mod set at the previous step: start again from the
            // game's.
            Apply(__instance, wheel, share, StockDamper(__instance), true);
        }

        /// <summary>
        /// Sets on the wheel collider the damper the fix sizes for a suspension carrying <paramref name="share"/> tons,
        /// or the mass the vessel sets in motion at its wheel when that is less: <paramref name="stockDamper"/>, the
        /// damper the game last computed, kept under what one physics step can take.
        /// </summary>
        private static void Apply(ModuleWheelSuspension suspension, KSPWheelController wheel, float share,
            float stockDamper, bool offGround)
        {
            Vector3 normal, point;
            Contact(suspension, wheel, out normal, out point);

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
            float damper = stockDamper;

            // A damper c slows a mass m by c * v / m * dt in a physics step of length dt: above m / dt, the step does
            // not stop the motion, it reverses it, and the next step reverses it again, faster. The mass is the
            // vessel's taken as rigid, but a wheel at the end of trusses first sets in motion less than that, its
            // joints giving way: the damper takes off at most half the speed in a step.
            float limit = share / (2f * Time.fixedDeltaTime);
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

            // Leave the wheel alone unless the fix changes it, or has just stopped: the game only sets a damper that
            // differs from the wheel's by more than 1.05, and would otherwise keep the mod's.
            if (isLimited || wasLimited)
            {
                wheel.wheelCollider.damperRate = damper;
            }

            if (Log.IsDebugEnabled)
            {
                LogChange(suspension, share, stockDamper, damper, isLimited, wasLimited, offGround);
            }
        }

        /// <summary>
        /// Gives the point the wheel touches the surface at and that surface's normal, or, when it touches none, the
        /// wheel's position and the local vertical.
        /// </summary>
        private static void Contact(ModuleWheelSuspension suspension, KSPWheelController wheel,
            out Vector3 normal, out Vector3 point)
        {
            // The wheel reads its compression along a ray cast down its axis, and that ray hits whatever collider lies
            // on its way, another vessel's side included. Off the ground, the local vertical stands for the ground the
            // wheel will touch first.
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
        /// Writes a Debug line when a suspension starts or stops being limited.
        /// </summary>
        private static void LogChange(ModuleWheelSuspension suspension, float share, float stockDamper, float damper,
            bool isLimited, bool wasLimited, bool offGround)
        {
            Part part = suspension.part;
            string name = $"{part.partInfo?.name} ({part.persistentId}) of {suspension.vessel.vesselName}";
            string where = offGround ? ", wheel off the ground" : "";
            if (isLimited && !wasLimited)
            {
                Log.Debug($"Damper of {name} limited to {damper:0.###} for {share:0.###} t carried{where}, " +
                    $"KSP computed {stockDamper:0.###}");
            }
            else if (!isLimited && wasLimited)
            {
                Log.Debug($"Damper of {name} no longer limited, {damper:0.###} for {share:0.###} t carried");
            }
        }
    }
}
