using System.Collections.Generic;
using ModuleWheels;
using UnityEngine;

namespace com.github.lhervier.ksp.fix.suspensions.fixedtuning
{
    /// <summary>
    /// The spring and damping the player sets on a part's suspension, in place of the game's two multipliers and its
    /// automatic mode. Added by ModuleManager to every part with a <see cref="ModuleWheelSuspension"/>; saved with the
    /// craft and the vessel. A spring of 0 means the suspension was never set: the fix predicts a setting for it at the
    /// first physics step it sees it in flight (see <see cref="FixedTuning"/>).
    /// </summary>
    public class ModuleSuspensionTuning : PartModule
    {
        /// <summary>The spring rate, in kN/m. 0 until the suspension is set.</summary>
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Spring",
            guiUnits = " kN/m", guiFormat = "0.##")]
        [UI_FloatRange(scene = UI_Scene.All, minValue = 1f, maxValue = 100f, stepIncrement = 1f,
            affectSymCounterparts = UI_Scene.All)]
        public float springRate = 0f;

        /// <summary>
        /// The damping ratio: 1 stops the wheel without a bounce, 0.7 lets it overshoot a little, below 0.3 it bounces.
        /// </summary>
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Damping ratio",
            guiFormat = "0.00")]
        [UI_FloatRange(scene = UI_Scene.All, minValue = 0.05f, maxValue = 1.5f, stepIncrement = 0.05f,
            affectSymCounterparts = UI_Scene.All)]
        public float dampingRatio = 0.7f;

        /// <summary>
        /// The mass the suspension was set to carry, in t, which turns the damping ratio into a damper; 0 when unknown
        /// (spring typed by hand on a suspension never set), the vessel's mass shared among its suspensions then.
        /// </summary>
        [KSPField(isPersistant = true)]
        public float tunedMass = 0f;

        /// <summary>What the last setting was made for, as the player reads it: the body, the mass, how.</summary>
        [KSPField(isPersistant = true)]
        public string tunedFor = "";

        /// <summary>The spring slider's ends, as factors of the spring predicted for the suspension.</summary>
        private const float SoftestFactor = 0.05f;
        private const float StiffestFactor = 2f;

        /// <summary>Whether the part's window shows the spring and the damping ratio.</summary>
        private bool shown;

        /// <summary>Whether the suspension has a setting of the fix's.</summary>
        public bool IsTuned => springRate > 0f;

        /// <summary>The part's suspension, which this module sets.</summary>
        public ModuleWheelSuspension Suspension { get; private set; }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            Suspension = part.FindModuleImplementing<ModuleWheelSuspension>();
            shown = FixedTuning.Enabled && Suspension != null;
            Fields[nameof(springRate)].guiActive = shown;
            Fields[nameof(springRate)].guiActiveEditor = shown;
            Fields[nameof(dampingRatio)].guiActive = shown;
            Fields[nameof(dampingRatio)].guiActiveEditor = shown;
            if (!shown)
            {
                return;
            }
            UpdateSpringRange();
            // The game's own multipliers and automatic mode no longer set anything.
            Suspension.Fields["springTweakable"].guiActive = false;
            Suspension.Fields["springTweakable"].guiActiveEditor = false;
            Suspension.Fields["damperTweakable"].guiActive = false;
            Suspension.Fields["damperTweakable"].guiActiveEditor = false;
            BaseEvent toggle = Suspension.Events["EvtAutoSpringDamperToggle"];
            if (toggle != null)
            {
                toggle.guiActive = false;
                toggle.guiActiveEditor = false;
            }
        }

        /// <summary>Keeps the spring slider's range up to date with the vessel while the part's window is open.</summary>
        private void Update()
        {
            if (shown && part.PartActionWindow != null)
            {
                UpdateSpringRange();
            }
        }

        /// <summary>Sets the suspension, and records what for.</summary>
        public void Set(float spring, float damping, float mass, string what)
        {
            springRate = spring;
            dampingRatio = damping;
            tunedMass = mass;
            tunedFor = what;
            if (shown)
            {
                UpdateSpringRange();
            }
        }

        /// <summary>
        /// Sets the range of the spring's slider, from <see cref="SoftestFactor"/> to <see cref="StiffestFactor"/>
        /// times the spring predicted for the suspension, widened to hold the spring set: never down to 0 once the
        /// suspension is set.
        /// </summary>
        private void UpdateSpringRange()
        {
            float predicted = PredictedSpring();
            // About a hundred steps up to the prediction, on round values.
            float step = Round(predicted / 100f);
            float softest = Mathf.Max(step, Mathf.Floor(SoftestFactor * predicted / step) * step);
            float stiffest = Mathf.Ceil(StiffestFactor * predicted / step) * step;
            // The game's slider writes back into the field any value outside its range, as soon as the value changes
            // (UIPartActionFloatRange): the spring set must always lie within it. A suspension never set (0) opens the
            // range down to 0, and the first spring picked closes it again.
            softest = Mathf.Min(softest, springRate);
            stiffest = Mathf.Max(stiffest, springRate);
            BaseField field = Fields[nameof(springRate)];
            foreach (UI_Control control in new[] { field.uiControlEditor, field.uiControlFlight })
            {
                UI_FloatRange range = control as UI_FloatRange;
                if (range != null)
                {
                    range.minValue = softest;
                    range.maxValue = stiffest;
                    range.stepIncrement = step;
                }
            }
        }

        /// <summary>
        /// Returns the spring predicted for this suspension, in kN/m, as for a suspension never set: the vessel's mass
        /// shared equally among its suspensions, at the default compression, on the body the vessel is at in flight,
        /// or the one chosen in the suspension window in the editor. Without a prediction (no mass yet, part not
        /// attached), the spring set, else a tenth of the one that holds the part's maximum load on Kerbin at the end
        /// of its travel.
        /// </summary>
        private float PredictedSpring()
        {
            List<Part> parts;
            double mass;
            CelestialBody body;
            if (HighLogic.LoadedSceneIsFlight && vessel != null)
            {
                parts = vessel.parts;
                mass = vessel.totalMass;
                body = vessel.mainBody;
            }
            else
            {
                parts = TuningChoices.Parts();
                mass = TuningChoices.VesselMass();
                body = TuningChoices.Body != null ? TuningChoices.Body : FlightGlobals.GetHomeBody();
            }
            if (body != null)
            {
                List<FixedTuning.Prediction> predictions = FixedTuning.Predict(FixedTuning.TuningsOf(parts), mass,
                    null, FixedTuning.SurfaceGravity(body), FixedTuning.Compression);
                foreach (FixedTuning.Prediction p in predictions)
                {
                    if (p.Tuning == this && p.Spring > 0f)
                    {
                        return p.Spring;
                    }
                }
            }
            if (springRate > 0f)
            {
                return springRate;
            }
            float travel = Suspension.suspensionDistance * part.rescaleFactor;
            return Suspension.maximumLoad > 0f && travel > 0f
                ? Suspension.maximumLoad * (float)PhysicsGlobals.GravitationalAcceleration / travel / 10f : 50f;
        }

        /// <summary>Rounds a step to one significant figure, at least 0.01.</summary>
        private static float Round(float step)
        {
            if (step <= 0.01f)
            {
                return 0.01f;
            }
            float power = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(step)));
            return Mathf.Round(step / power) * power;
        }
    }
}
