using System.Collections.Generic;
using System.Text;

namespace com.github.lhervier.ksp.fix.suspensions.fixedtuning
{
    /// <summary>
    /// What the suspension window does, callable from outside the game (the MCP server's call_method), for the test
    /// campaigns: on the vessel being built in the editor, or the active vessel in flight, every suspension at once.
    /// </summary>
    public static class SuspensionTuningApi
    {
        /// <summary>
        /// Predicts and sets every suspension for <paramref name="body"/> (its name; empty for the one chosen in the
        /// window), <paramref name="mass"/> t (0 or less: the vessel's), shared by the centre of mass or equally, at
        /// <paramref name="compression"/> of the travel and the damping ratio <paramref name="damping"/>. Returns what
        /// was done.
        /// </summary>
        public static string Auto(string body, double mass, bool useCentre, double compression, double damping)
        {
            if (!FixedTuning.Enabled)
            {
                return "The fixed tuning is off (settings.cfg)";
            }
            TuningChoices.Defaults();
            if (!string.IsNullOrEmpty(body))
            {
                CelestialBody found = FlightGlobals.GetBodyByName(body);
                if (found == null)
                {
                    return $"No body named {body}";
                }
                TuningChoices.Body = found;
            }
            TuningChoices.UseCentre = useCentre;
            TuningChoices.Compression = (float)compression;
            TuningChoices.Damping = (float)damping;
            return TuningChoices.Auto(FixedTuning.TuningsOf(TuningChoices.Parts()), mass);
        }

        /// <summary>
        /// Starts levelling every suspension of the active vessel to <paramref name="compression"/> of its travel, with
        /// the damping ratio <paramref name="damping"/>. Returns "started", or why it cannot start; then
        /// <see cref="LevelStatus"/> tells how it goes.
        /// </summary>
        public static string Level(double compression, double damping)
        {
            if (!FixedTuning.Enabled)
            {
                return "The fixed tuning is off (settings.cfg)";
            }
            return Leveller.Begin(FlightGlobals.ActiveVessel, FixedTuning.TuningsOf(TuningChoices.Parts()),
                (float)compression, (float)damping) ?? "started";
        }

        /// <summary>Where the levelling is, or how it ended, then one line per suspension: "running: ..." while it runs.
        /// </summary>
        public static string LevelStatus()
        {
            Leveller current = Leveller.Current;
            if (current == null)
            {
                return "none";
            }
            return (current.Running ? "running: " : "done: ") + current.Status +
                (current.Report.Count > 0 ? "\n" + string.Join("\n", current.Report) : "");
        }

        /// <summary>Removes the setting of every suspension, as on a vessel built without the fix.</summary>
        public static string Clear()
        {
            int count = 0;
            foreach (ModuleSuspensionTuning t in FixedTuning.TuningsOf(TuningChoices.Parts()))
            {
                t.Set(0f, FixedTuning.Damping, 0f, "");
                FixedTuning.Forget(t);
                count++;
            }
            return $"{count} suspensions cleared";
        }

        /// <summary>One line per suspension: part, persistent id, spring, damping ratio, mass carried, what for.</summary>
        public static string Describe()
        {
            List<ModuleSuspensionTuning> tunings = FixedTuning.TuningsOf(TuningChoices.Parts());
            StringBuilder text = new StringBuilder($"{tunings.Count} suspensions");
            foreach (ModuleSuspensionTuning t in tunings)
            {
                text.Append($"\n{t.part.partInfo.name} #{t.part.persistentId}: spring {t.springRate:0.##} kN/m, " +
                    $"damping ratio {t.dampingRatio:0.00}, mass {t.tunedMass:0.###} t, {t.tunedFor}");
            }
            return text.ToString();
        }
    }
}
