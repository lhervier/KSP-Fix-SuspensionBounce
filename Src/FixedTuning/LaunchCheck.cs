using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace com.github.lhervier.ksp.fix.suspensions.fixedtuning
{
    /// <summary>
    /// Stops a launch from the editor while some suspensions of the vessel have no setting, and asks the player to set
    /// them: for the body chosen in the suspension window, from the vessel's mass and centre of mass; or to launch them
    /// as they are, the fix setting them at the first physics step of the flight, from the vessel's mass shared equally
    /// among its suspensions.
    /// </summary>
    internal static class LaunchCheck
    {
        private static readonly MethodInfo LaunchVessel =
            AccessTools.Method(typeof(EditorLogic), nameof(EditorLogic.launchVessel), new[] { typeof(string) });

        /// <summary>Set to let the next launch through, once the player has answered.</summary>
        private static bool answered;

        /// <summary>Whether the question is on screen: the suspension window, drawn above it, steps aside.</summary>
        public static bool Asking { get; internal set; }

        public static void Install(Harmony harmony)
        {
            if (LaunchVessel == null)
            {
                Log.Warning("EditorLogic.launchVessel(string) not found: no check of the suspensions at launch");
                return;
            }
            harmony.Patch(LaunchVessel, prefix: new HarmonyMethod(typeof(LaunchCheck), nameof(LaunchVesselPrefix)));
        }

        /// <summary>
        /// Lets the launch go when every suspension is set, or once the player has answered; otherwise shows the
        /// question, and stops it.
        /// </summary>
        private static bool LaunchVesselPrefix(EditorLogic __instance, string siteName)
        {
            if (!FixedTuning.IsActive || answered || __instance.ship == null)
            {
                answered = false;
                return true;
            }
            int all = 0, unset = 0;
            foreach (ModuleSuspensionTuning t in FixedTuning.TuningsOf(__instance.ship.parts))
            {
                all++;
                if (!t.IsTuned)
                {
                    unset++;
                }
            }
            if (unset == 0)
            {
                return true;
            }
            TuningChoices.Defaults();
            string body = TuningChoices.Body.bodyName;
            string message = $"{unset} of the {all} suspensions of this vessel have no setting.\n\n" +
                $"Set them for {body}, from the vessel's mass and centre of mass, at " +
                $"{100f * TuningChoices.Compression:0} % compression and a damping ratio of " +
                $"{TuningChoices.Damping:0.00}? The suspension window, from the toolbar, picks another body, mass or " +
                "set of suspensions.\n\nLaunched as they are, the fix sets them at the first physics step of the " +
                "flight, from the vessel's mass shared equally among its suspensions.";
            Asking = true;
            PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new MultiOptionDialog("KSPFixSuspensionBounceLaunch", message, "Suspensions not set", HighLogic.UISkin,
                    new DialogGUIButton($"Set them for {body} and launch", () =>
                    {
                        TuningChoices.SetUnset(__instance.ship.parts);
                        Relaunch(__instance, siteName);
                    }, true),
                    new DialogGUIButton("Launch as they are", () => Relaunch(__instance, siteName), true),
                    new DialogGUIButton("Cancel", () => Asking = false, true)),
                false, HighLogic.UISkin);
            return false;
        }

        private static void Relaunch(EditorLogic editor, string siteName)
        {
            Asking = false;
            answered = true;
            editor.launchVessel(siteName);
        }
    }
}
