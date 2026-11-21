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
        [UI_FloatEdit(scene = UI_Scene.All, minValue = 0f, maxValue = 100000f, incrementLarge = 10f,
            incrementSmall = 1f, incrementSlide = 0.05f, sigFigs = 2, unit = " kN/m",
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

        /// <summary>Whether the suspension has a setting of the fix's.</summary>
        public bool IsTuned => springRate > 0f;

        /// <summary>The part's suspension, which this module sets.</summary>
        public ModuleWheelSuspension Suspension { get; private set; }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            Suspension = part.FindModuleImplementing<ModuleWheelSuspension>();
            bool shown = FixedTuning.Enabled && Suspension != null;
            Fields[nameof(springRate)].guiActive = shown;
            Fields[nameof(springRate)].guiActiveEditor = shown;
            Fields[nameof(dampingRatio)].guiActive = shown;
            Fields[nameof(dampingRatio)].guiActiveEditor = shown;
            if (!shown)
            {
                return;
            }
            // The arrows step by a quarter and a fortieth of the spring that holds the part's maximum load on Kerbin at
            // the end of its travel: a few kN/m for a small leg, hundreds for a large gear.
            float travel = Suspension.suspensionDistance * part.rescaleFactor;
            float reference = Suspension.maximumLoad > 0f && travel > 0f
                ? Suspension.maximumLoad * (float)PhysicsGlobals.GravitationalAcceleration / travel : 50f;
            foreach (UI_Control control in new[] { Fields[nameof(springRate)].uiControlEditor,
                Fields[nameof(springRate)].uiControlFlight })
            {
                UI_FloatEdit edit = control as UI_FloatEdit;
                if (edit != null)
                {
                    edit.incrementLarge = Round(reference / 4f);
                    edit.incrementSmall = Round(reference / 40f);
                    edit.incrementSlide = Round(reference / 400f);
                }
            }
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

        /// <summary>Sets the suspension, and records what for.</summary>
        public void Set(float spring, float damping, float mass, string what)
        {
            springRate = spring;
            dampingRatio = damping;
            tunedMass = mass;
            tunedFor = what;
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
