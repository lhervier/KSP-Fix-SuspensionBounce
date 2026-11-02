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
    }
}
