using System;
using HarmonyLib;
using UnityEngine;

namespace com.github.lhervier.ksp.fix.suspensionbounce
{
    /// <summary>
    /// Installs the fix in the mode the settings choose, once, when KSP starts: from then on, every suspension of a
    /// vessel in flight gets a damper sized for the mass it carries, as <see cref="DamperMode"/> describes.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class KSPFixSuspensionBounce : MonoBehaviour
    {
        private const string HarmonyId = "com.github.lhervier.ksp.fix.suspensionbounce";

        private void Start()
        {
            Settings.Load();
            Log.Info($"Version {typeof(KSPFixSuspensionBounce).Assembly.GetName().Version}, mode {Settings.Mode}, " +
                $"log level {Log.Level}");
            if (Settings.Mode == DamperMode.Stock)
            {
                return;
            }
            try
            {
                SuspensionDamper.Install(new Harmony(HarmonyId));
                Log.Info($"Suspension damper fix installed, mode {Settings.Mode}");
            }
            catch (Exception e)
            {
                Log.Error("Could not install the suspension damper fix, the dampers are the ones KSP sets, as " +
                    $"without it: {e}");
            }
        }
    }
}
