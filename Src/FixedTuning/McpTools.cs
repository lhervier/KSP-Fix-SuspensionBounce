using System.Collections.Generic;
using UnityEngine;

namespace com.github.lhervier.ksp.fix.suspensions.fixedtuning
{
    /// <summary>
    /// What the suspension window offers, as tools of KSP-Util-MCPServer when it is installed: reading the settings,
    /// and its buttons Auto and Level on the ground with every suspension of the vessel being built or of the active
    /// vessel chosen, with the same choices and the same bounds as the window.
    /// </summary>
    internal static class McpTools
    {
        [McpTool("suspension_tuning_read",
            "Reads the suspensions as the Suspensions window of Suspension Bounce Fix lists them, for the vessel being " +
            "built or the active vessel: per suspension, part, id, spring kN/m, damping ratio, mass set for, what for; " +
            "the window's choices; the levelling.")]
        internal static object Read()
        {
            if (!FixedTuning.Enabled)
            {
                return Off();
            }
            TuningChoices.Defaults();
            List<object> suspensions = new List<object>();
            foreach (ModuleSuspensionTuning t in FixedTuning.TuningsOf(TuningChoices.Parts()))
            {
                suspensions.Add(new Dictionary<string, object>
                {
                    { "part", t.part.partInfo.title },
                    { "partId", (double)t.part.flightID },
                    { "set", t.IsTuned },
                    { "spring", (double)t.springRate },
                    { "dampingRatio", (double)t.dampingRatio },
                    { "tunedMass", (double)t.tunedMass },
                    { "tunedFor", t.tunedFor }
                });
            }
            Leveller levelling = Leveller.Current;
            return new Dictionary<string, object>
            {
                { "active", FixedTuning.IsActive },
                { "body", TuningChoices.Body != null ? TuningChoices.Body.bodyName : "" },
                { "massTyped", TuningChoices.CustomMass ? (object)(double)TuningChoices.Mass : null },
                { "vesselMass", TuningChoices.VesselMass() },
                { "byCentre", TuningChoices.UseCentre },
                { "compressionPct", 100.0 * TuningChoices.Compression },
                { "dampingRatio", (double)TuningChoices.Damping },
                { "suspensions", suspensions },
                { "levelling", levelling == null ? "none" : (levelling.Running ? "running: " : "done: ") + levelling.Status },
                { "levellingReport", levelling == null ? new List<string>() : new List<string>(levelling.Report) }
            };
        }

        [McpTool("suspension_tuning_auto",
            "Sets every suspension as the Suspensions window's All then Auto do: body (name; empty: kept), mass typed in t " +
            "(0: the vessel's), byCentre (false: shared equally), compressionPct (0 to 100 excluded; 0: kept), " +
            "dampingRatio (0: kept). Returns what was done, then the read.")]
        internal static object Auto(string body = "", double mass = 0, bool byCentre = true, double compressionPct = 0,
            double dampingRatio = 0)
        {
            if (!FixedTuning.Enabled)
            {
                return Off();
            }
            string refused = Choose(body, mass, byCentre, compressionPct, dampingRatio);
            if (refused != null)
            {
                return Done(refused);
            }
            return Done(TuningChoices.Auto(FixedTuning.TuningsOf(TuningChoices.Parts()),
                TuningChoices.CustomMass ? TuningChoices.Mass : 0));
        }

        [McpTool("suspension_tuning_level",
            "Starts levelling every suspension of the active vessel on the ground, as the Suspensions window's All then " +
            "Level on the ground do: compressionPct (0 to 100 excluded; 0: kept), dampingRatio (0: kept). Returns whether it " +
            "started, then the read; read again until it is done.")]
        internal static object Level(double compressionPct = 0, double dampingRatio = 0)
        {
            if (!FixedTuning.Enabled)
            {
                return Off();
            }
            if (!HighLogic.LoadedSceneIsFlight || FlightGlobals.ActiveVessel == null)
            {
                return Done("Levelling needs a vessel in flight");
            }
            string refused = Choose("", TuningChoices.CustomMass ? TuningChoices.Mass : 0, TuningChoices.UseCentre,
                compressionPct, dampingRatio);
            if (refused != null)
            {
                return Done(refused);
            }
            return Done(Leveller.Begin(FlightGlobals.ActiveVessel, FixedTuning.TuningsOf(TuningChoices.Parts()),
                TuningChoices.Compression, TuningChoices.Damping) ?? "Levelling started");
        }

        /// <summary>
        /// Makes the window's choices those given, as the player would type them, and shows them in the window if it is
        /// open. Returns why they are refused, the choices left unchanged, or null.
        /// </summary>
        private static string Choose(string body, double mass, bool byCentre, double compressionPct, double dampingRatio)
        {
            TuningChoices.Defaults();
            CelestialBody found = null;
            if (!string.IsNullOrEmpty(body))
            {
                found = FlightGlobals.GetBodyByName(body);
                if (found == null || !found.hasSolidSurface)
                {
                    return $"No body with a surface named {body}";
                }
            }
            // The window's own bounds: it ignores a compression outside them, and a mass or a damping ratio not above 0.
            if (compressionPct < 0 || compressionPct >= 100)
            {
                return "compressionPct must be above 0 and below 100, as in the window";
            }
            if (mass < 0 || dampingRatio < 0)
            {
                return "mass and dampingRatio must be above 0, or 0 to keep them";
            }
            if (found != null)
            {
                TuningChoices.Body = found;
            }
            TuningChoices.CustomMass = mass > 0;
            if (mass > 0)
            {
                TuningChoices.Mass = (float)mass;
            }
            TuningChoices.UseCentre = byCentre;
            if (compressionPct > 0)
            {
                TuningChoices.Compression = (float)(compressionPct / 100.0);
            }
            if (dampingRatio > 0)
            {
                TuningChoices.Damping = (float)dampingRatio;
            }
            // Every suspension chosen, as the window's All button does.
            TuningChoices.LeftOut.Clear();
            TuningWindow window = Object.FindObjectOfType<TuningWindow>();
            if (window != null)
            {
                window.ShowChoices();
            }
            return null;
        }

        /// <summary>What a tool did, followed by the read of the suspensions.</summary>
        private static object Done(string what)
        {
            Dictionary<string, object> result = new Dictionary<string, object> { { "result", what } };
            object read = Read();
            Dictionary<string, object> fields = read as Dictionary<string, object>;
            if (fields != null)
            {
                foreach (KeyValuePair<string, object> field in fields)
                {
                    result[field.Key] = field.Value;
                }
            }
            return result;
        }

        /// <summary>The answer of every tool while the fixed tuning is off.</summary>
        private static object Off()
        {
            return new Dictionary<string, object>
            {
                { "active", false },
                { "result", "The fixed tuning is off (fixedTuning = false in settings.cfg)" }
            };
        }
    }
}
