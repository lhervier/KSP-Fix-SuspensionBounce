using System.Collections.Generic;
using System.Globalization;
using KSP.UI.Screens;
using ModuleWheels;
using UnityEngine;
using VehiclePhysics;
using com.github.lhervier.ksp.fix.suspensions.vesselbodies;

namespace com.github.lhervier.ksp.fix.suspensions.fixedtuning
{
    /// <summary>
    /// The suspension window, opened from the toolbar: lists the vessel's suspensions with their setting, lets the
    /// player choose which to set, and sets them: Auto predicts their springs for a body and a mass, in the editor and
    /// in flight; Level, in flight, measures them on the ground. One window for the editor and one for the flight, the
    /// same code.
    /// </summary>
    public abstract class TuningWindow : MonoBehaviour
    {
        private const int WindowId = 0x53424654;
        private const string LockId = "KSPFixSuspensionBounce_window";
        private const float RefreshPeriod = 0.5f;

        // Static: whether the window is open, and where, hold from one scene to the next.
        private static bool visible;
        private static Rect windowRect = new Rect(300f, 80f, 600f, 0f);
        private static Texture2D icon;

        private ApplicationLauncherButton button;
        private bool locked;
        private float nextRefresh;
        private readonly List<ModuleSuspensionTuning> tunings = new List<ModuleSuspensionTuning>();
        private Vector2 scroll;
        private string status = "";
        private string massText;
        private string compressionText;
        private string dampingText;

        /// <summary>The scenes the toolbar button shows in.</summary>
        protected abstract ApplicationLauncher.AppScenes Scenes { get; }

        private void Start()
        {
            if (!FixedTuning.Enabled)
            {
                Destroy(this);
                return;
            }
            // A question left open by a scene change no longer hides the window.
            LaunchCheck.Asking = false;
            TuningChoices.Defaults();
            ShowChoices();
            GameEvents.onGUIApplicationLauncherReady.Add(AddButton);
            if (ApplicationLauncher.Ready)
            {
                AddButton();
            }
        }

        private void OnDestroy()
        {
            GameEvents.onGUIApplicationLauncherReady.Remove(AddButton);
            if (button != null && ApplicationLauncher.Instance != null)
            {
                ApplicationLauncher.Instance.RemoveModApplication(button);
            }
            Unlock();
        }

        // An instance method: GameEvents refuse static handlers.
        private void AddButton()
        {
            if (button != null || ApplicationLauncher.Instance == null)
            {
                return;
            }
            button = ApplicationLauncher.Instance.AddModApplication(() => visible = true, () => visible = false,
                null, null, null, null, Scenes, Icon());
            if (visible)
            {
                button.SetTrue(false);
            }
        }

        private void OnGUI()
        {
            if (!visible || LaunchCheck.Asking)
            {
                Unlock();
                return;
            }
            GUI.skin = HighLogic.Skin;
            windowRect = GUILayout.Window(WindowId, windowRect, DrawWindow, "Suspensions");
            // In the editor, a click on the window would also pick or drop parts behind it.
            if (HighLogic.LoadedSceneIsEditor && windowRect.Contains(Event.current.mousePosition))
            {
                if (!locked)
                {
                    InputLockManager.SetControlLock(ControlTypes.EDITOR_LOCK, LockId);
                    locked = true;
                }
            }
            else
            {
                Unlock();
            }
        }

        private void Unlock()
        {
            if (locked)
            {
                InputLockManager.RemoveControlLock(LockId);
                locked = false;
            }
        }

        private void DrawWindow(int id)
        {
            // The list changes only on a layout pass: the passes of one frame must draw the same controls.
            if (Event.current.type == EventType.Layout)
            {
                if (Time.unscaledTime >= nextRefresh)
                {
                    nextRefresh = Time.unscaledTime + RefreshPeriod;
                    tunings.Clear();
                    tunings.AddRange(FixedTuning.TuningsOf(TuningChoices.Parts()));
                }
                tunings.RemoveAll(t => t == null || t.part == null);
                TuningChoices.Defaults();
            }
            bool flight = HighLogic.LoadedSceneIsFlight;

            GUILayout.BeginVertical();
            DrawPrediction();
            GUILayout.Space(6f);
            DrawList(flight);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Auto", GUILayout.Width(120f)))
            {
                ReadFields();
                status = TuningChoices.Auto(Chosen(), TuningChoices.CustomMass ? TuningChoices.Mass : 0);
            }
            if (flight && GUILayout.Button("Level on the ground", GUILayout.Width(170f)))
            {
                ReadFields();
                status = Leveller.Begin(FlightGlobals.ActiveVessel, Chosen(), TuningChoices.Compression,
                    TuningChoices.Damping) ?? "Levelling started";
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close", GUILayout.Width(70f)))
            {
                visible = false;
                if (button != null)
                {
                    button.SetFalse(false);
                }
            }
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(status))
            {
                GUILayout.Label(status);
            }
            if (flight && Leveller.Current != null)
            {
                GUILayout.Label("Levelling: " + Leveller.Current.Status);
                foreach (string line in Leveller.Current.Report)
                {
                    GUILayout.Label("  " + line);
                }
            }
            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        /// <summary>The body, the mass, the compression and the damping ratio Auto predicts with.</summary>
        private void DrawPrediction()
        {
            List<CelestialBody> bodies = TuningChoices.Bodies();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Body", GUILayout.Width(110f));
            if (GUILayout.Button("<", GUILayout.Width(30f)) && bodies.Count > 0)
            {
                TuningChoices.Body = bodies[(bodies.IndexOf(TuningChoices.Body) - 1 + bodies.Count) % bodies.Count];
            }
            GUILayout.Label(TuningChoices.Body.bodyName, GUILayout.Width(90f));
            if (GUILayout.Button(">", GUILayout.Width(30f)) && bodies.Count > 0)
            {
                TuningChoices.Body = bodies[(bodies.IndexOf(TuningChoices.Body) + 1) % bodies.Count];
            }
            GUILayout.Label($"g {Format((float)FixedTuning.SurfaceGravity(TuningChoices.Body), "0.##")} m/s²");
            if (HighLogic.LoadedSceneIsFlight && FlightGlobals.ActiveVessel != null &&
                GUILayout.Button("Here", GUILayout.Width(60f)))
            {
                TuningChoices.Body = FlightGlobals.ActiveVessel.mainBody;
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Mass", GUILayout.Width(110f));
            TuningChoices.CustomMass = GUILayout.Toggle(TuningChoices.CustomMass, " typed:", GUILayout.Width(80f));
            if (TuningChoices.CustomMass)
            {
                massText = GUILayout.TextField(massText, GUILayout.Width(70f));
                GUILayout.Label("t", GUILayout.Width(20f));
            }
            GUILayout.Label($"(vessel {Format((float)TuningChoices.VesselMass(), "0.###")} t)");
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            TuningChoices.UseCentre = GUILayout.Toggle(TuningChoices.UseCentre,
                " Share by the vessel's centre of mass (off: the mass shared equally)");

            GUILayout.BeginHorizontal();
            GUILayout.Label("Compression", GUILayout.Width(110f));
            compressionText = GUILayout.TextField(compressionText, GUILayout.Width(50f));
            GUILayout.Label("%", GUILayout.Width(30f));
            GUILayout.Label("Damping ratio", GUILayout.Width(110f));
            dampingText = GUILayout.TextField(dampingText, GUILayout.Width(50f));
            GUILayout.Label("(1: no bounce)");
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        /// <summary>The suspensions, each with its toggle and its setting, and in flight its contact.</summary>
        private void DrawList(bool flight)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Suspensions ({Chosen().Count} of {tunings.Count} chosen)");
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("All", GUILayout.Width(50f)))
            {
                foreach (ModuleSuspensionTuning t in tunings)
                {
                    TuningChoices.LeftOut.Remove(t.part.persistentId);
                }
            }
            if (GUILayout.Button("None", GUILayout.Width(50f)))
            {
                foreach (ModuleSuspensionTuning t in tunings)
                {
                    TuningChoices.LeftOut.Add(t.part.persistentId);
                }
            }
            GUILayout.EndHorizontal();
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(Mathf.Clamp(30f * tunings.Count + 12f, 42f, 312f)));
            foreach (ModuleSuspensionTuning t in tunings)
            {
                uint pid = t.part.persistentId;
                GUILayout.BeginHorizontal();
                bool chosen = GUILayout.Toggle(!TuningChoices.LeftOut.Contains(pid), "", GUILayout.Width(22f));
                if (chosen)
                {
                    TuningChoices.LeftOut.Remove(pid);
                }
                else
                {
                    TuningChoices.LeftOut.Add(pid);
                }
                GUILayout.Label(t.part.partInfo.title, GUILayout.Width(230f));
                GUILayout.Label(t.IsTuned ? $"{Format(t.springRate, "0.##")} kN/m" : "not set", GUILayout.Width(90f));
                GUILayout.Label($"ζ {Format(t.dampingRatio, "0.00")}", GUILayout.Width(55f));
                if (flight)
                {
                    GUILayout.Label(Contact(t), GUILayout.Width(150f));
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }

        /// <summary>Where the suspension's wheel is: in the air, or its compression and load on the ground.</summary>
        private static string Contact(ModuleSuspensionTuning t)
        {
            ModuleWheelSuspension s = t.Suspension;
            KSPWheelController wheel = s != null ? WheelContacts.Controller(s) : null;
            if (wheel == null || !wheel.enabled || wheel.wheelState == null || wheel.wheelState.Length == 0 ||
                wheel.wheelState[0].wheelCol == null)
            {
                return "folded";
            }
            VPWheelCollider col = wheel.wheelState[0].wheelCol;
            WheelHit hit;
            float travel, compression;
            if (!WheelContacts.Read(col, out hit, out travel, out compression) || travel <= 0f)
            {
                return "in the air";
            }
            return $"{Format(100f * compression, "0")} %, {Format(hit.force, "0.##")} kN";
        }

        /// <summary>The suspensions listed and not left out.</summary>
        private List<ModuleSuspensionTuning> Chosen()
        {
            return tunings.FindAll(t => !TuningChoices.LeftOut.Contains(t.part.persistentId));
        }

        /// <summary>Reads the typed values into the choices, keeping the previous ones where a value does not parse.
        /// </summary>
        private void ReadFields()
        {
            float value;
            if (Parse(massText, out value) && value > 0f)
            {
                TuningChoices.Mass = value;
            }
            if (Parse(compressionText, out value) && value > 0f && value < 100f)
            {
                TuningChoices.Compression = value / 100f;
            }
            if (Parse(dampingText, out value) && value > 0f)
            {
                TuningChoices.Damping = value;
            }
            ShowChoices();
        }

        /// <summary>Shows the typed mass, compression and damping ratio of the choices in their fields.</summary>
        internal void ShowChoices()
        {
            massText = Format(TuningChoices.Mass, "0.###");
            compressionText = Format(100f * TuningChoices.Compression, "0");
            dampingText = Format(TuningChoices.Damping, "0.00");
        }

        private static bool Parse(string text, out float value)
        {
            return float.TryParse((text ?? "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture,
                out value);
        }

        private static string Format(float value, string format)
        {
            return value.ToString(format, CultureInfo.InvariantCulture);
        }

        /// <summary>The toolbar icon, drawn once: a spring between two plates.</summary>
        private static Texture2D Icon()
        {
            if (icon != null)
            {
                return icon;
            }
            const int size = 38;
            icon = new Texture2D(size, size, TextureFormat.ARGB32, false);
            Color clear = new Color(0f, 0f, 0f, 0f);
            for (int x = 0; x < size; x++)
            {
                for (int y = 0; y < size; y++)
                {
                    icon.SetPixel(x, y, clear);
                }
            }
            Color white = Color.white;
            Line(icon, 8, 5, 30, 5, white);
            Line(icon, 8, 33, 30, 33, white);
            int[] xs = { 19, 28, 10, 28, 10, 28, 10, 19 };
            for (int i = 0; i + 1 < xs.Length; i++)
            {
                int y0 = 5 + i * 4, y1 = 5 + (i + 1) * 4;
                Line(icon, xs[i], y0, xs[i + 1], y1, white);
            }
            icon.Apply();
            return icon;
        }

        /// <summary>Draws a line two pixels thick.</summary>
        private static void Line(Texture2D texture, int x0, int y0, int x1, int y1, Color color)
        {
            int steps = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0), 1);
            for (int i = 0; i <= steps; i++)
            {
                int x = Mathf.RoundToInt(Mathf.Lerp(x0, x1, (float)i / steps));
                int y = Mathf.RoundToInt(Mathf.Lerp(y0, y1, (float)i / steps));
                for (int dx = 0; dx <= 1; dx++)
                {
                    for (int dy = 0; dy <= 1; dy++)
                    {
                        if (x + dx < texture.width && y + dy < texture.height)
                        {
                            texture.SetPixel(x + dx, y + dy, color);
                        }
                    }
                }
            }
        }
    }

    /// <summary>The suspension window in the editors.</summary>
    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    public class TuningWindowEditor : TuningWindow
    {
        protected override ApplicationLauncher.AppScenes Scenes =>
            ApplicationLauncher.AppScenes.VAB | ApplicationLauncher.AppScenes.SPH;
    }

    /// <summary>The suspension window in flight.</summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class TuningWindowFlight : TuningWindow
    {
        protected override ApplicationLauncher.AppScenes Scenes => ApplicationLauncher.AppScenes.FLIGHT;
    }
}
