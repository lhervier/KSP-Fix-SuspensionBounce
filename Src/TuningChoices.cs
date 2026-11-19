using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace com.github.lhervier.ksp.fix.suspensionbounce
{
    /// <summary>
    /// What the player chose in the suspension window, kept from one scene to the next for the session: the body and the
    /// mass to predict for, the compression and damping ratio to aim at, and the suspensions to leave out. Also the
    /// actions the window, the launch check and the test API share.
    /// </summary>
    internal static class TuningChoices
    {
        /// <summary>The body to predict for; the home body until the player picks another.</summary>
        public static CelestialBody Body;

        /// <summary>Whether to predict for <see cref="Mass"/> rather than the vessel's own mass.</summary>
        public static bool CustomMass;

        /// <summary>The mass typed by the player, in t.</summary>
        public static float Mass = 1f;

        /// <summary>Whether the shares follow the vessel's centre of mass, rather than the mass shared equally.</summary>
        public static bool UseCentre = true;

        /// <summary>The compression aimed at, as a fraction of the travel; the setting's default until changed.</summary>
        public static float Compression = -1f;

        /// <summary>The damping ratio given; the setting's default until changed.</summary>
        public static float Damping = -1f;

        /// <summary>The suspensions left out, by persistent id of their part.</summary>
        public static readonly HashSet<uint> LeftOut = new HashSet<uint>();

        /// <summary>The vessel whose body <see cref="Body"/> was last set to in flight.</summary>
        private static System.Guid bodyOf;

        /// <summary>
        /// Fills in what the player has not chosen yet from the settings and the game. In flight, the body is the active
        /// vessel's, each time the active vessel changes; the player may pick another for it afterwards.
        /// </summary>
        public static void Defaults()
        {
            Vessel vessel = HighLogic.LoadedSceneIsFlight ? FlightGlobals.ActiveVessel : null;
            if (vessel != null && vessel.id != bodyOf)
            {
                bodyOf = vessel.id;
                Body = vessel.mainBody;
            }
            if (Body == null)
            {
                Body = FlightGlobals.GetHomeBody();
            }
            if (Compression <= 0f)
            {
                Compression = FixedTuning.Compression;
            }
            if (Damping <= 0f)
            {
                Damping = FixedTuning.Damping;
            }
        }

        /// <summary>The bodies a vessel can stand on, in the game's order.</summary>
        public static List<CelestialBody> Bodies()
        {
            return FlightGlobals.Bodies.Where(b => b != null && b.hasSolidSurface).ToList();
        }

        /// <summary>The parts of the vessel being built, or of the active vessel in flight; empty elsewhere.</summary>
        public static List<Part> Parts()
        {
            if (HighLogic.LoadedSceneIsEditor && EditorLogic.fetch != null && EditorLogic.fetch.ship != null)
            {
                return EditorLogic.fetch.ship.parts;
            }
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (HighLogic.LoadedSceneIsFlight && vessel != null && vessel.loaded)
            {
                return vessel.parts;
            }
            return new List<Part>();
        }

        /// <summary>The mass of the vessel being built, or of the active vessel, in t.</summary>
        public static double VesselMass()
        {
            if (HighLogic.LoadedSceneIsEditor && EditorLogic.fetch != null && EditorLogic.fetch.ship != null)
            {
                return EditorLogic.fetch.ship.GetTotalMass();
            }
            Vessel vessel = FlightGlobals.ActiveVessel;
            return vessel != null ? vessel.totalMass : 0;
        }

        /// <summary>The centre of mass of the vessel being built, or of the active vessel; null when there is none.
        /// </summary>
        public static Vector3? VesselCentre()
        {
            if (HighLogic.LoadedSceneIsEditor)
            {
                return EditorLogic.RootPart != null
                    ? EditorMarker_CoM.findCenterOfMass(EditorLogic.RootPart) : (Vector3?)null;
            }
            Vessel vessel = FlightGlobals.ActiveVessel;
            return vessel != null ? vessel.CoM : (Vector3?)null;
        }

        /// <summary>
        /// Predicts and sets the springs of <paramref name="chosen"/> as the only suspensions carrying the vessel, for
        /// the choices made, with <paramref name="mass"/> t or the vessel's mass when it is not positive. Returns what
        /// was done, for the player.
        /// </summary>
        public static string Auto(List<ModuleSuspensionTuning> chosen, double mass)
        {
            Defaults();
            if (chosen.Count == 0)
            {
                return "No suspension chosen";
            }
            if (mass <= 0)
            {
                mass = VesselMass();
            }
            Vector3? centre = UseCentre ? VesselCentre() : null;
            List<FixedTuning.Prediction> predictions = FixedTuning.Predict(chosen, mass, centre,
                FixedTuning.SurfaceGravity(Body), Compression);
            if (predictions.Count == 0)
            {
                return "Nothing set: no usable suspension, or no mass";
            }
            string what = $"auto for {Body.bodyName}, {mass:0.###} t over {predictions.Count}" +
                (centre.HasValue ? ", by the centre of mass" : ", shared equally");
            Set(predictions, what, false);
            return $"Set {predictions.Count} suspensions: {what}";
        }

        /// <summary>
        /// Gives each suspension never set of <paramref name="parts"/> the spring predicted for the vessel standing on
        /// all its suspensions, on the chosen body, with its own mass and centre of mass. Returns how many were set.
        /// </summary>
        public static int SetUnset(List<Part> parts)
        {
            Defaults();
            List<ModuleSuspensionTuning> all = FixedTuning.TuningsOf(parts);
            List<FixedTuning.Prediction> predictions = FixedTuning.Predict(all, VesselMass(), VesselCentre(),
                FixedTuning.SurfaceGravity(Body), Compression);
            string what = $"default for {Body.bodyName}, {VesselMass():0.###} t";
            return Set(predictions, what, true);
        }

        /// <summary>Writes the predictions, only on suspensions never set when <paramref name="unsetOnly"/>.</summary>
        private static int Set(List<FixedTuning.Prediction> predictions, string what, bool unsetOnly)
        {
            int set = 0;
            foreach (FixedTuning.Prediction p in predictions)
            {
                if (unsetOnly && p.Tuning.IsTuned)
                {
                    continue;
                }
                p.Tuning.Set(p.Spring, Damping, p.Share, what);
                set++;
            }
            Log.Info($"Suspensions {what}: {set} set{FixedTuning.Describe(predictions)}");
            if (HighLogic.LoadedSceneIsEditor && EditorLogic.fetch != null && EditorLogic.fetch.ship != null)
            {
                GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
            }
            return set;
        }
    }
}
