using System;
using System.Reflection;
using HarmonyLib;
using ModuleWheels;
using UnityEngine;

namespace com.github.lhervier.ksp.fix.suspensionbounce
{
    /// <summary>
    /// Gives back their spring at once to the wheels the game rebuilds when two vessels dock or undock, so that a vessel
    /// standing on its wheels no longer jumps. ModuleWheelBase.onDockingComplete and onVesselUndocking destroy and
    /// recreate the Unity wheel collider of every wheel of both vessels, with Unity's default spring (rest point at half
    /// the travel), then set the spring rate to 0; VPWheelCollider then moves the rest point back by 0.02 of the travel a
    /// step only: some twenty steps during which each leg pushes the vessel up (21 cm measured on a refueller of 19 t on
    /// four LT-2, docked to a base on the Mun). Right after the game's handler, the wheel collider gets the spring, the
    /// damper and the rest point it had before.
    /// </summary>
    internal static class DockingWheelReset
    {
        /// <summary>Whether the fix is installed. Read from settings.cfg.</summary>
        public static bool Enabled = true;

        private static readonly MethodInfo OnDockingComplete =
            AccessTools.DeclaredMethod(typeof(ModuleWheelBase), "onDockingComplete");

        private static readonly MethodInfo OnVesselUndocking =
            AccessTools.DeclaredMethod(typeof(ModuleWheelBase), "onVesselUndocking");

        private static readonly AccessTools.FieldRef<ModuleWheelSuspension, float> StockSpring =
            AccessTools.FieldRefAccess<ModuleWheelSuspension, float>("spring");

        private static readonly AccessTools.FieldRef<ModuleWheelSuspension, float> StockDamper =
            AccessTools.FieldRefAccess<ModuleWheelSuspension, float>("damper");

        /// <exception cref="MissingMemberException">A member the fix patches is missing from the game.</exception>
        public static void Install(Harmony harmony)
        {
            if (OnDockingComplete == null || OnVesselUndocking == null)
            {
                throw new MissingMemberException("ModuleWheelBase.onDockingComplete or onVesselUndocking");
            }
            HarmonyMethod postfix = new HarmonyMethod(typeof(DockingWheelReset), nameof(RebuiltPostfix));
            harmony.Patch(OnDockingComplete, postfix: postfix);
            harmony.Patch(OnVesselUndocking, postfix: postfix);
        }

        /// <summary>
        /// Sets on the wheel collider the game has just rebuilt the spring and damper of the part's suspension, the
        /// player's setting or the game's last one, and the rest point VPWheelCollider would aim at for them.
        /// </summary>
        private static void RebuiltPostfix(ModuleWheelBase __instance)
        {
            Part part = __instance.part;
            ModuleWheelSuspension suspension = part != null ? part.FindModuleImplementing<ModuleWheelSuspension>() : null;
            if (suspension == null || !HighLogic.LoadedSceneIsFlight)
            {
                return;
            }
            KSPWheelController wheel = FixedTuning.Wheel(suspension);
            if (wheel == null || wheel.wheelCollider == null)
            {
                return;
            }
            // The handler left the wheel alone when the docking did not concern its vessel.
            if (wheel.wheelCollider.springRate != 0f)
            {
                return;
            }
            ModuleSuspensionTuning tuning = FixedTuning.IsActive ? FixedTuning.Tuning(suspension) : null;
            float spring = tuning != null && tuning.IsTuned ? tuning.springRate : StockSpring(suspension);
            float damper = StockDamper(suspension);
            if (spring <= 0f)
            {
                return;
            }
            wheel.wheelCollider.springRate = spring;
            if (damper > 0f)
            {
                wheel.wheelCollider.damperRate = damper;
            }
            WheelCollider collider = wheel.wheelCollider.GetWheelCollider();
            if (collider == null)
            {
                return;
            }
            // As VPWheelCollider sizes it: at least stiff enough to hold the wheel's own weight within the travel, rest
            // point at the wheel's own weight over the spring, within [0.1, 1] of the travel.
            float travel = collider.suspensionDistance > 0f ? collider.suspensionDistance
                : suspension.suspensionDistance * part.rescaleFactor;
            float sprungMass = collider.sprungMass > 0f ? collider.sprungMass : part.mass;
            float gravity = (float)FlightGlobals.getGeeForceAtPosition(part.transform.position).magnitude;
            if (travel <= 0f)
            {
                return;
            }
            float sprungForce = sprungMass * gravity;
            JointSpring joint = collider.suspensionSpring;
            joint.spring = Mathf.Max(spring, sprungForce / travel, 0.1f);
            if (damper > 0f)
            {
                joint.damper = damper;
            }
            joint.targetPosition = Mathf.Clamp(sprungForce / joint.spring / travel, 0.1f, 1f);
            collider.suspensionSpring = joint;
            Log.Debug($"{part.partInfo.name} #{part.persistentId}: wheel collider rebuilt by a docking, spring " +
                $"{spring:0.##} and rest point {joint.targetPosition:0.###} given back");
        }
    }
}
