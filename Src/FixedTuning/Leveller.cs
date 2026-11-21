using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using ModuleWheels;
using UnityEngine;
using VehiclePhysics;
using com.github.lhervier.ksp.fix.suspensions.endstopslide;

namespace com.github.lhervier.ksp.fix.suspensions.fixedtuning
{
    /// <summary>
    /// Levels a vessel standing still on the ground: measures, over a second, the load and the compression of each
    /// chosen suspension whose wheel touches the ground, and gives it the spring that sinks it to the compression asked
    /// for under that load; waits for the vessel to settle, and measures again, until every wheel is within
    /// <see cref="Tolerance"/> of its target or after <see cref="MaxPasses"/> passes. A suspension whose wheel does not
    /// touch is left as it is. The load is the one PhysX applies, whatever else holds the vessel up: a rover docked to a
    /// base standing on its own struts keeps the spring that carries the rover alone.
    /// </summary>
    internal class Leveller : MonoBehaviour
    {
        /// <summary>Physics steps a measure is averaged over.</summary>
        public static int SampleSteps = 50;

        /// <summary>Physics steps left for the vessel to settle after its springs change.</summary>
        public static int SettleSteps = 100;

        /// <summary>The most measures, and settings, a levelling makes.</summary>
        public static int MaxPasses = 5;

        /// <summary>How far from its target, in fraction of the travel, a wheel counts as levelled.</summary>
        public static float Tolerance = 0.03f;

        /// <summary>Compression from which a wheel is taken as on its end-stop, where the spring's force says little.
        /// </summary>
        private const float Bottomed = 0.97f;

        /// <summary>Slope, in degrees, and distance of a contact to the plane of the others, in m, from which the
        /// levelling warns that the vessel will not stand level on a flat ground.</summary>
        private const float SlopeWarning = 5f;
        private const float FlatnessWarning = 0.05f;

        /// <summary>The levelling running, or the last one of the scene; null before any.</summary>
        public static Leveller Current { get; private set; }

        /// <summary>Whether the levelling is still measuring or waiting.</summary>
        public bool Running { get; private set; }

        /// <summary>Where the levelling is, or how it ended, for the player.</summary>
        public string Status { get; private set; } = "";

        /// <summary>One line per suspension, and the warnings, for the player.</summary>
        public List<string> Report { get; } = new List<string>();

        private Vessel vessel;
        private List<ModuleSuspensionTuning> tunings;
        private float target;
        private float damping;

        /// <summary>
        /// Starts levelling <paramref name="vessel"/>'s suspensions among <paramref name="chosen"/> to
        /// <paramref name="compression"/> of their travel, with the damping ratio <paramref name="dampingRatio"/>.
        /// Returns null when started, or why it cannot start.
        /// </summary>
        public static string Begin(Vessel vessel, List<ModuleSuspensionTuning> chosen, float compression,
            float dampingRatio)
        {
            if (Current != null && Current.Running)
            {
                return "A levelling is already running";
            }
            if (vessel == null || !vessel.loaded || vessel.packed)
            {
                return "The vessel is not in physics";
            }
            if (!vessel.Landed)
            {
                return "The vessel is not landed";
            }
            if (vessel.srfSpeed > 0.2)
            {
                return $"The vessel is moving ({vessel.srfSpeed:0.##} m/s)";
            }
            if (chosen.Count == 0)
            {
                return "No suspension chosen";
            }
            if (compression <= 0.05f || compression >= 0.95f)
            {
                return "The compression must lie between 5 and 95 %";
            }
            if (Current != null)
            {
                Destroy(Current.gameObject);
            }
            Leveller leveller = new GameObject("KSPFixSuspensionBounce.Leveller").AddComponent<Leveller>();
            leveller.vessel = vessel;
            leveller.tunings = new List<ModuleSuspensionTuning>(chosen);
            leveller.target = compression;
            leveller.damping = dampingRatio;
            leveller.Running = true;
            Current = leveller;
            leveller.StartCoroutine(leveller.Run());
            return null;
        }

        /// <summary>The sums one suspension gathers over a measure.</summary>
        private class Measure
        {
            public ModuleSuspensionTuning Tuning;
            public int Grounded;
            public double Force;
            public double Compression;
            public Vector3 Point;
            public Vector3 Axis;
            /// <summary>The spring the wheel collider has, the game's for a suspension never set.</summary>
            public float Spring;
            /// <summary>The travel of the suspension, in m, as the wheel collider runs it.</summary>
            public float Travel;
        }

        private IEnumerator Run()
        {
            WaitForFixedUpdate wait = new WaitForFixedUpdate();
            for (int pass = 1; pass <= MaxPasses; pass++)
            {
                Status = $"Pass {pass}: measuring";
                List<Measure> measures = new List<Measure>();
                foreach (ModuleSuspensionTuning t in tunings)
                {
                    measures.Add(new Measure { Tuning = t });
                }
                for (int step = 0; step < SampleSteps; step++)
                {
                    yield return wait;
                    if (vessel == null || vessel.packed)
                    {
                        Finish("Stopped: the vessel left physics");
                        yield break;
                    }
                    foreach (Measure m in measures)
                    {
                        Read(m);
                    }
                }

                double gravity = FlightGlobals.getGeeForceAtPosition(vessel.CoM).magnitude;
                Report.Clear();
                Warn(measures);
                bool levelled = true;
                int changed = 0;
                int touching = 0;
                string what = $"levelled on {vessel.mainBody.bodyName}";
                foreach (Measure m in measures)
                {
                    ModuleSuspensionTuning t = m.Tuning;
                    ModuleWheelSuspension s = t.Suspension;
                    string name = t.part.partInfo.title;
                    if (s == null || m.Grounded < SampleSteps)
                    {
                        Report.Add($"{name}: {(m.Grounded == 0 ? "not on the ground" : "touches now and then")}, left at " +
                            $"{Format(t.springRate)} kN/m");
                        continue;
                    }
                    touching++;
                    double compression = m.Compression / m.Grounded;
                    double force = m.Force / m.Grounded;
                    double travel = Math.Max(m.Travel, 0.01);
                    float spring;
                    string note;
                    if (compression >= Bottomed)
                    {
                        // On its end-stop the spring's force no longer tells the load: twice as stiff, and measure again.
                        spring = Math.Max(m.Spring, 0.1f) * 2f;
                        note = "on its end-stop, spring doubled";
                        levelled = false;
                    }
                    else if (force <= 0.001)
                    {
                        Report.Add($"{name}: touches without carrying, left at {Format(t.springRate)} kN/m");
                        continue;
                    }
                    else if (Math.Abs(compression - target) <= Tolerance)
                    {
                        spring = t.IsTuned ? t.springRate : m.Spring;
                        note = "levelled";
                    }
                    else
                    {
                        // The sinking the spring gives under this load, as the prediction models it, and the one read:
                        // their difference, a few cm on some legs, is carried over to the sinking aimed at.
                        double wheelWeight = FixedTuning.SprungMass(s) * gravity;
                        double offset = FixedTuning.SinkingUnder(force, wheelWeight, Math.Max(m.Spring, 0.1f), travel)
                            - compression * travel;
                        double aimed = target * travel + offset;
                        // However stiff the spring, VPWheelCollider keeps its rest point at 10 % of the travel at least.
                        double lowest = FixedTuning.RestFloor * travel;
                        if (aimed <= lowest + 0.002 * travel)
                        {
                            spring = t.IsTuned ? t.springRate : m.Spring;
                            note = $"cannot sink less than about {100.0 * (lowest - offset) / travel:0} %, left as it is";
                        }
                        else
                        {
                            // At most 4 times stiffer or softer a pass: the next pass measures again.
                            spring = Mathf.Clamp(FixedTuning.SpringFor(force, wheelWeight, aimed, travel),
                                m.Spring / 4f, m.Spring * 4f);
                            levelled = false;
                            note = $"{Format(m.Spring)} -> {Format(spring)} kN/m";
                        }
                    }
                    Report.Add($"{name}: compression {100.0 * compression:0} %, load {force:0.###} kN, {note}");
                    if (spring != t.springRate || t.dampingRatio != damping)
                    {
                        float carried = gravity > 0 ? (float)(force / gravity) : t.tunedMass;
                        t.Set(spring, damping, carried, what);
                        changed++;
                    }
                }
                if (touching == 0)
                {
                    Finish("Nothing levelled: no chosen wheel stays on the ground");
                    yield break;
                }
                if (levelled)
                {
                    Finish($"Levelled in {pass} pass{(pass > 1 ? "es" : "")}");
                    yield break;
                }
                Log.Info($"Levelling {vessel.vesselName}, pass {pass}: {changed} springs changed\n  " +
                    string.Join("\n  ", Report));
                Status = $"Pass {pass}: {changed} springs changed, settling";
                for (int step = 0; step < SettleSteps; step++)
                {
                    yield return wait;
                    if (vessel == null || vessel.packed)
                    {
                        Finish("Stopped: the vessel left physics");
                        yield break;
                    }
                }
            }
            Finish($"Not levelled after {MaxPasses} passes: see each wheel below");
        }

        /// <summary>Adds the suspension's contact at this step to its measure.</summary>
        private static void Read(Measure m)
        {
            ModuleWheelSuspension s = m.Tuning.Suspension;
            KSPWheelController wheel = s != null ? FixedTuning.Wheel(s) : null;
            VehicleBase.WheelState state = wheel != null && wheel.enabled && wheel.wheelState != null &&
                wheel.wheelState.Length > 0 ? wheel.wheelState[0] : null;
            VPWheelCollider col = state != null ? state.wheelCol : null;
            if (col == null)
            {
                return;
            }
            WheelHit hit;
            float travel = col.lastRuntimeSuspensionTravel;
            if (!col.GetGroundHit(out hit) || travel <= 0f)
            {
                return;
            }
            m.Grounded++;
            m.Force += hit.force;
            m.Compression += EndStopSlide.ContactDepth(col, hit.point, travel) / travel;
            m.Point = hit.point;
            m.Axis = col.cachedTransform.up;
            m.Spring = col.springRate;
            m.Travel = travel;
        }

        /// <summary>
        /// Warns when the ground the wheels stand on is not flat: a vessel levelled on a slope, or with a wheel on a
        /// bump, stands crooked once on a flat ground.
        /// </summary>
        private void Warn(List<Measure> measures)
        {
            List<Measure> grounded = measures.FindAll(m => m.Grounded == SampleSteps);
            if (grounded.Count == 0)
            {
                return;
            }
            Vector3 up = FlightGlobals.getUpAxis(vessel.mainBody, vessel.CoM);
            Vector3 axis = Vector3.zero;
            Vector3 centre = Vector3.zero;
            foreach (Measure m in grounded)
            {
                axis += m.Axis;
                centre += m.Point;
            }
            axis.Normalize();
            centre /= grounded.Count;
            float slope = Vector3.Angle(axis, up);
            if (slope > SlopeWarning)
            {
                Report.Add($"Warning: the wheels lean by {slope:0.#}°: on a slope, the downhill wheels get stiffer " +
                    "springs, and the vessel will lean on a flat ground");
            }
            if (grounded.Count < 4)
            {
                return;
            }
            // The plane through the contacts that fits them best, along the wheels' axis: h = a + b u + c v.
            Vector3 e1 = Vector3.Cross(axis, Mathf.Abs(axis.x) < 0.9f ? Vector3.right : Vector3.forward).normalized;
            Vector3 e2 = Vector3.Cross(axis, e1);
            int n = grounded.Count;
            double su = 0, sv = 0, suu = 0, svv = 0, suv = 0, sh = 0, suh = 0, svh = 0;
            double[] u = new double[n], v = new double[n], h = new double[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 r = grounded[i].Point - centre;
                u[i] = Vector3.Dot(r, e1); v[i] = Vector3.Dot(r, e2); h[i] = Vector3.Dot(r, axis);
                su += u[i]; sv += v[i]; suu += u[i] * u[i]; svv += v[i] * v[i]; suv += u[i] * v[i];
                sh += h[i]; suh += u[i] * h[i]; svh += v[i] * h[i];
            }
            double det = n * (suu * svv - suv * suv) - su * (su * svv - suv * sv) + sv * (su * suv - suu * sv);
            if (Math.Abs(det) < 1e-9)
            {
                return;
            }
            // Cramer's rule on [n su sv; su suu suv; sv suv svv] (a b c) = (sh suh svh).
            double a = (sh * (suu * svv - suv * suv) - su * (suh * svv - suv * svh) + sv * (suh * suv - suu * svh)) / det;
            double b = (n * (suh * svv - suv * svh) - sh * (su * svv - suv * sv) + sv * (su * svh - suh * sv)) / det;
            double c = (n * (suu * svh - suh * suv) - su * (su * svh - suh * sv) + sh * (su * suv - suu * sv)) / det;
            double worst = 0;
            string worstName = "";
            for (int i = 0; i < n; i++)
            {
                double off = Math.Abs(h[i] - (a + b * u[i] + c * v[i]));
                if (off > worst)
                {
                    worst = off;
                    worstName = grounded[i].Tuning.part.partInfo.title;
                }
            }
            if (worst > FlatnessWarning)
            {
                Report.Add($"Warning: the ground is not flat, {worstName} stands {100 * worst:0} cm off the plane of " +
                    "the others: the vessel will stand crooked on a flat ground");
            }
        }

        private void Finish(string status)
        {
            Status = status;
            Running = false;
            Log.Info($"Levelling {(vessel != null ? vessel.vesselName : "?")}: {status}\n  " + string.Join("\n  ", Report));
        }

        private static string Format(float value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
