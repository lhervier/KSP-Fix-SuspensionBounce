using System;
using HarmonyLib;
using UnityEngine;

namespace com.github.lhervier.ksp.fix.suspensionbounce
{
    /// <summary>
    /// Installs the fix once, when KSP starts: from then on, every suspension of a vessel in flight keeps the damper KSP
    /// sets, unless it is stiffer than the mass it carries can take in one physics step without bouncing back.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class KSPFixSuspensionBounce : MonoBehaviour
    {
        private const string HarmonyId = "com.github.lhervier.ksp.fix.suspensionbounce";

        private void Start()
        {
            Settings.Load();
            Log.Info($"Version {typeof(KSPFixSuspensionBounce).Assembly.GetName().Version}, log level {Log.Level}");
            // The read of the vessels' bodies that the damper and end-stop fixes share.
            VesselBodies.Install();
            if (SpringGravityCurve.Enabled)
            {
                // Ahead of the damper fix, which starts from the damper the spring fix sets.
                try
                {
                    SpringGravityCurve.Install(new Harmony(HarmonyId + ".gravitycurve"));
                    Log.Info("Spring gravity curve fix installed");
                }
                catch (Exception e)
                {
                    Log.Error($"Could not install the spring gravity curve fix, the springs are the game's: {e}");
                }
            }
            if (SuspensionDamper.Enabled)
            {
                try
                {
                    SuspensionDamper.Install(new Harmony(HarmonyId));
                    Log.Info("Suspension damper fix installed");
                }
                catch (Exception e)
                {
                    Log.Error("Could not install the suspension damper fix, the dampers are the ones KSP sets, as " +
                        $"without it: {e}");
                }
            }
            if (BumpStopLoad.Enabled)
            {
                try
                {
                    BumpStopLoad.Install(new Harmony(HarmonyId + ".bumpstop"));
                    Log.Info("Bump-stop load fix installed");
                }
                catch (Exception e)
                {
                    Log.Error($"Could not install the bump-stop load fix, the tyres read the spring alone: {e}");
                }
            }
            if (CostMeasure.Enabled)
            {
                CostMeasure.Install(new Harmony(HarmonyId + ".costmeasure"));
            }
        }
    }
}
