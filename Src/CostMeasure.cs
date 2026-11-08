using System;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using ModuleWheels;
using UnityEngine;
using VehiclePhysics;

namespace com.github.lhervier.ksp.fix.suspensionbounce
{
    /// <summary>
    /// Measures what the mod costs per physics step, and what the game's wheels cost beside it, and writes it to
    /// KSP.log every <see cref="WindowSeconds"/> seconds of flight.
    /// <para>
    /// Off by default (<c>measureCost</c> in settings.cfg): it is then never installed, and the mod runs exactly as if
    /// this file did not exist. When on, it adds Harmony patches of its own around the fix's patches and around the
    /// game's wheel methods, and changes nothing they do.
    /// </para>
    /// <para>
    /// The fix's patches run at Harmony's normal priority. The measure starts a clock in a patch of higher priority,
    /// which Harmony runs just before them, and stops it in one of lower priority, run just after: what lies between
    /// is the fix's time (and that of any other mod patching the same methods at a priority in between). The game's
    /// wheels are timed the same way, from the first patch to the last of each of their methods, the original
    /// included; the fix's time spent inside them is taken out.
    /// </para>
    /// </summary>
    internal static class CostMeasure
    {
        /// <summary>Whether to install the measure. Read from settings.cfg, false unless the player sets it.</summary>
        public static bool Enabled = false;

        /// <summary>How many seconds of real time each line of KSP.log covers.</summary>
        public const float WindowSeconds = 10f;

        // Priorities, higher first. The fix's patches run at Priority.Normal (400).
        private const int WheelsStart = Priority.First;
        private const int FixStart = Priority.VeryHigh;
        private const int FixStop = Priority.VeryLow;
        private const int WheelsStop = Priority.Last;

        /// <summary>The game's wheel methods, each timed as a whole, the fix's patches on it included.</summary>
        private static readonly MethodInfo[] WheelMethods =
        {
            AccessTools.DeclaredMethod(typeof(VehicleBase), "FixedUpdate"),
            AccessTools.DeclaredMethod(typeof(ModuleWheelSuspension), "FixedUpdate"),
            AccessTools.DeclaredMethod(typeof(ModuleWheelDamage), "FixedUpdate"),
        };

        /// <summary>Patched by the damper and spring fixes, inside ModuleWheelSuspension.FixedUpdate.</summary>
        private static readonly MethodInfo[] SuspensionPostfixed =
        {
            AccessTools.DeclaredMethod(typeof(ModuleWheelSuspension), "SuspensionSpringUpdate"),
            AccessTools.DeclaredMethod(typeof(ModuleWheelSuspension), "FixedUpdate"),
        };

        /// <summary>Patched by the end-stop fix, inside VehicleBase.FixedUpdate or ModuleWheelDamage.FixedUpdate.</summary>
        private static readonly MethodInfo[] EndStopPostfixed =
        {
            AccessTools.DeclaredMethod(typeof(VehicleBase), "CalculateSuspensionForces"),
            AccessTools.DeclaredMethod(typeof(VehicleBase), "CalculateDownforce"),
            AccessTools.DeclaredMethod(typeof(VehicleBase), "ApplyTireForce"),
            AccessTools.DeclaredMethod(typeof(ModuleWheelDamage), "FixedUpdate"),
        };

        private static readonly MethodInfo DamageFixedUpdate =
            AccessTools.DeclaredMethod(typeof(ModuleWheelDamage), "FixedUpdate");

        /// <summary>Prefixed by the end-stop fix, once per part and step: outside the game's wheel methods.</summary>
        private static readonly MethodInfo Integrate =
            AccessTools.DeclaredMethod(typeof(FlightIntegrator), "Integrate", new[] { typeof(Part) });

        /// <summary>The end-stop solver, run only at the steps a vessel needs it.</summary>
        private static readonly MethodInfo HoldEndStops = AccessTools.DeclaredMethod(typeof(BumpStopLoad), "HoldEndStops");

        private static readonly MethodInfo SuspensionFixedUpdate =
            AccessTools.DeclaredMethod(typeof(ModuleWheelSuspension), "FixedUpdate");

        // Totals of the current window, in Stopwatch ticks.
        private static long wheelsTicks, suspensionTicks, endStopsTicks, integrateTicks, solverTicks;
        private static int steps, suspensionCalls, solverCalls;
        private static long fixStart, solverStart;

        /// <summary>
        /// Adds the measuring patches, and the component that writes the lines. Never throws: a member missing from
        /// the game leaves the measure off, with an error in KSP.log, and the fix as it is.
        /// </summary>
        public static void Install(Harmony harmony)
        {
            try
            {
                foreach (MethodInfo method in WheelMethods)
                {
                    Patch(harmony, method, nameof(WheelPrefix), WheelsStart, nameof(WheelPostfix), WheelsStop);
                }
                Patch(harmony, SuspensionFixedUpdate, nameof(CountSuspension), WheelsStart, null, 0);
                foreach (MethodInfo method in SuspensionPostfixed)
                {
                    Patch(harmony, method, null, 0, nameof(StartFix), FixStart);
                    Patch(harmony, method, null, 0, nameof(StopSuspension), FixStop);
                }
                foreach (MethodInfo method in EndStopPostfixed)
                {
                    Patch(harmony, method, null, 0, nameof(StartFix), FixStart);
                    Patch(harmony, method, null, 0, nameof(StopEndStops), FixStop);
                }
                Patch(harmony, DamageFixedUpdate, nameof(StartFix), FixStart, null, 0);
                Patch(harmony, DamageFixedUpdate, nameof(StopEndStops), FixStop, null, 0);
                Patch(harmony, Integrate, nameof(StartFix), FixStart, null, 0);
                Patch(harmony, Integrate, nameof(StopIntegrate), FixStop, null, 0);
                Patch(harmony, HoldEndStops, nameof(StartSolver), Priority.Normal, nameof(StopSolver), Priority.Normal);
            }
            catch (Exception e)
            {
                harmony.UnpatchAll(harmony.Id);
                Log.Error($"Could not install the cost measure, nothing is measured: {e}");
                return;
            }
            GameObject writer = new GameObject("KSPFixSuspensionBounce.CostMeasure");
            UnityEngine.Object.DontDestroyOnLoad(writer);
            writer.AddComponent<Writer>();
            Log.Info($"Cost measure installed: one line every {WindowSeconds:F0} s of flight");
        }

        /// <exception cref="MissingMemberException">The method is missing from the game.</exception>
        private static void Patch(Harmony harmony, MethodInfo method, string prefix, int prefixPriority, string postfix,
            int postfixPriority)
        {
            if (method == null)
            {
                throw new MissingMemberException("a method the measure times");
            }
            harmony.Patch(method,
                prefix: prefix == null ? null : new HarmonyMethod(typeof(CostMeasure).GetMethod(prefix,
                    BindingFlags.NonPublic | BindingFlags.Static), prefixPriority),
                postfix: postfix == null ? null : new HarmonyMethod(typeof(CostMeasure).GetMethod(postfix,
                    BindingFlags.NonPublic | BindingFlags.Static), postfixPriority));
        }

        private static void WheelPrefix(out long __state)
        {
            __state = Stopwatch.GetTimestamp();
        }

        private static void WheelPostfix(long __state)
        {
            wheelsTicks += Stopwatch.GetTimestamp() - __state;
        }

        private static void CountSuspension()
        {
            suspensionCalls++;
        }

        private static void StartFix()
        {
            fixStart = Stopwatch.GetTimestamp();
        }

        private static void StopSuspension()
        {
            suspensionTicks += Stopwatch.GetTimestamp() - fixStart;
        }

        private static void StopEndStops()
        {
            endStopsTicks += Stopwatch.GetTimestamp() - fixStart;
        }

        private static void StopIntegrate()
        {
            integrateTicks += Stopwatch.GetTimestamp() - fixStart;
        }

        private static void StartSolver()
        {
            solverStart = Stopwatch.GetTimestamp();
        }

        private static void StopSolver()
        {
            solverTicks += Stopwatch.GetTimestamp() - solverStart;
            solverCalls++;
        }

        /// <summary>Counts the physics steps of flight, and writes one line per window.</summary>
        private class Writer : MonoBehaviour
        {
            private float windowStart = -1f;

            private void FixedUpdate()
            {
                if (!HighLogic.LoadedSceneIsFlight)
                {
                    Reset();
                    windowStart = -1f;
                    return;
                }
                float now = Time.realtimeSinceStartup;
                if (windowStart < 0f)
                {
                    Reset();
                    windowStart = now;
                }
                steps++;
                if (now - windowStart < WindowSeconds)
                {
                    return;
                }
                double micros = 1e6 / Stopwatch.Frequency / steps;
                double suspension = suspensionTicks * micros;
                double endStops = (endStopsTicks + integrateTicks) * micros;
                // The fix's patches, but those of FlightIntegrator.Integrate, run inside the wheel methods.
                double wheels = (wheelsTicks - suspensionTicks - endStopsTicks) * micros;
                Vessel active = FlightGlobals.ActiveVessel;
                // The total is the figure to compare between runs with and without the fixes: the first read of a
                // body's pose in a step pays for syncing the scene's transforms, and whoever reads first pays it.
                Log.Info(string.Format(
                    "Cost over {0} physics steps ({1:F0} s), {2:F1} suspensions per step, active vessel {3}: " +
                    "wheels and mod {4:F1} µs/step, of which the game's wheels {5:F1} and the mod {6:F1} " +
                    "(dampers and springs {7:F1}; end-stops {8:F1}, of which force records {9:F1} and the solver " +
                    "{10:F1}, run {11} times)",
                    steps, now - windowStart, (float)suspensionCalls / steps,
                    active == null ? "none" : "'" + active.vesselName + "'", wheels + suspension + endStops, wheels,
                    suspension + endStops, suspension, endStops, integrateTicks * micros, solverTicks * micros,
                    solverCalls));
                Reset();
                windowStart = now;
            }

            private static void Reset()
            {
                wheelsTicks = suspensionTicks = endStopsTicks = integrateTicks = solverTicks = 0;
                steps = suspensionCalls = solverCalls = 0;
            }
        }
    }
}
