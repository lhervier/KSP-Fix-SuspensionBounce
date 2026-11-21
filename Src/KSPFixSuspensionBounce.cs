using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using com.github.lhervier.ksp.fix.suspensions.damperbounce;
using com.github.lhervier.ksp.fix.suspensions.endstopslide;
using com.github.lhervier.ksp.fix.suspensions.fixedtuning;
using com.github.lhervier.ksp.fix.suspensions.vesselbodies;

namespace com.github.lhervier.ksp.fix.suspensions
{
    /// <summary>
    /// Installs the mod once, when KSP starts: each of its fixes that settings.cfg switches on, and the read of the
    /// vessels' bodies they share.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class KSPFixSuspensionBounce : MonoBehaviour
    {
        private const string HarmonyId = "com.github.lhervier.ksp.fix.suspensions";

        private void Start()
        {
            Settings.Load();
            string version = typeof(KSPFixSuspensionBounce).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            Log.Info($"Version {version}, log level {Log.Level}");
            // The read of the vessels' bodies that DamperBounce and EndStopSlide share.
            VesselBodies.Install();
            if (FixedTuning.Enabled)
            {
                try
                {
                    FixedTuning.Install(new Harmony(HarmonyId + ".fixedtuning"));
                    Log.Info($"FixedTuning installed: compression {FixedTuning.Compression}, damping {FixedTuning.Damping}");
                }
                catch (Exception e)
                {
                    Log.Error($"Could not install FixedTuning, the game sets the suspensions: {e}");
                }
            }
            if (DamperBounce.Enabled)
            {
                try
                {
                    DamperBounce.Install(new Harmony(HarmonyId + ".damperbounce"));
                    Log.Info("DamperBounce installed");
                }
                catch (Exception e)
                {
                    Log.Error($"Could not install DamperBounce, the dampers are the ones KSP sets, as without it: {e}");
                }
            }
            if (EndStopSlide.Enabled)
            {
                try
                {
                    EndStopSlide.Install(new Harmony(HarmonyId + ".endstopslide"));
                    Log.Info("EndStopSlide installed");
                }
                catch (Exception e)
                {
                    Log.Error($"Could not install EndStopSlide, the tyres read the spring alone: {e}");
                }
            }
            if (CostMeasure.Enabled)
            {
                CostMeasure.Install(new Harmony(HarmonyId + ".costmeasure"));
            }
        }
    }
}
