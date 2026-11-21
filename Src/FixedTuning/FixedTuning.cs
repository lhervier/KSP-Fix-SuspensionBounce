using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using ModuleWheels;
using UnityEngine;
using com.github.lhervier.ksp.fix.suspensions.damperbounce;

namespace com.github.lhervier.ksp.fix.suspensions.fixedtuning
{
    /// <summary>
    /// Replaces the game's spring and damper, recomputed at each physics step from the wheel's compression and the
    /// vessel's mass, with the setting the player gives each suspension (<see cref="ModuleSuspensionTuning"/>): a spring
    /// in kN/m and a damping ratio, which nothing changes behind the player's back, not a docking, not a burn, not a
    /// wheel leaving the ground. The player sets them by hand, or has them predicted for a body and a mass
    /// (<see cref="Predict"/>), or levelled on the ground (<see cref="Leveller"/>). A suspension never set gets, at the
    /// first physics step the fix sees it, the prediction for its vessel's mass shared equally among all the vessel's
    /// suspensions.
    /// </summary>
    internal static class FixedTuning
    {
        /// <summary>Whether the fix is installed. Read from settings.cfg.</summary>
        public static bool Enabled = true;

        /// <summary>
        /// Whether the installed fix sets the suspensions; false gives them back to the game and the other fixes, as
        /// without it. Switched in game with set_member, to compare both on the same session.
        /// </summary>
        public static bool Active = true;

        /// <summary>Whether the fix is installed and sets the suspensions.</summary>
        public static bool IsActive => Enabled && Active;

        /// <summary>
        /// The fraction of its travel a wheel sinks to under its share of the weight, offered by default when predicting
        /// or levelling. Read from settings.cfg.
        /// </summary>
        public static float Compression = 0.33f;

        /// <summary>
        /// The damping ratio offered by default, and given to a suspension predicted without the player. It is the ratio
        /// the wheel gets once DamperBounce has made its damper implicit on the mass the wheel's part moves (see
        /// <see cref="DamperBounce"/>). Read from settings.cfg.
        /// </summary>
        public static float Damping = 0.7f;

        /// <summary>
        /// How many times the springs' righting moment must exceed the weight's tipping moment when the vessel leans:
        /// the sinking is lowered below the compression asked for until it does. 0 leaves it. Read from settings.cfg.
        /// </summary>
        public static float TipMargin = 2f;

        /// <summary>
        /// The most of m / dt, the most an implicit damper can give on the mass m it moves, the damper aims at when the
        /// damping ratio asks for more.
        /// </summary>
        private const double ImplicitCeiling = 0.9;

        private static readonly MethodInfo SuspensionSpringUpdate =
            AccessTools.DeclaredMethod(typeof(ModuleWheelSuspension), "SuspensionSpringUpdate");

        private static readonly MethodInfo FixedUpdate =
            AccessTools.DeclaredMethod(typeof(ModuleWheelSuspension), "FixedUpdate");

        internal static readonly AccessTools.FieldRef<ModuleWheelSubmodule, KSPWheelController> Wheel =
            AccessTools.FieldRefAccess<ModuleWheelSubmodule, KSPWheelController>("wheel");

        private static readonly AccessTools.FieldRef<ModuleWheelSuspension, float> StockSpring =
            AccessTools.FieldRefAccess<ModuleWheelSuspension, float>("spring");

        private static readonly AccessTools.FieldRef<ModuleWheelSuspension, float> StockDamper =
            AccessTools.FieldRefAccess<ModuleWheelSuspension, float>("damper");

        /// <summary>The tuning module of each suspension met in flight, null for a part without one.</summary>
        private static readonly Dictionary<ModuleWheelSuspension, ModuleSuspensionTuning> tunings =
            new Dictionary<ModuleWheelSuspension, ModuleSuspensionTuning>();

        /// <summary>
        /// The suspensions never set that a prediction was already made for, set or not by it (a broken wheel is left
        /// out of a prediction): each is predicted once, not again at every step.
        /// </summary>
        private static readonly HashSet<ModuleSuspensionTuning> predicted = new HashSet<ModuleSuspensionTuning>();

        /// <summary>
        /// Forgets that a suspension was predicted: once its setting is removed, it is predicted again at the next step
        /// it is seen, as a suspension never set.
        /// </summary>
        internal static void Forget(ModuleSuspensionTuning tuning)
        {
            predicted.Remove(tuning);
        }

        /// <summary>Whether a part without a tuning module was already reported.</summary>
        private static bool missingReported;

        /// <exception cref="MissingMemberException">A member the fix patches is missing from the game.</exception>
        public static void Install(Harmony harmony)
        {
            if (SuspensionSpringUpdate == null || FixedUpdate == null)
            {
                throw new MissingMemberException("ModuleWheelSuspension.SuspensionSpringUpdate or FixedUpdate");
            }
            harmony.Patch(SuspensionSpringUpdate,
                prefix: new HarmonyMethod(typeof(FixedTuning), nameof(SuspensionSpringUpdatePrefix)));
            harmony.Patch(FixedUpdate, prefix: new HarmonyMethod(typeof(FixedTuning), nameof(FixedUpdatePrefix)));
            LaunchCheck.Install(harmony);
            GameEvents.onGameSceneLoadRequested.Add(scene =>
            {
                tunings.Clear();
                predicted.Clear();
            });
        }

        /// <summary>Returns the tuning module of a suspension's part, or null when it has none.</summary>
        internal static ModuleSuspensionTuning Tuning(ModuleWheelSuspension suspension)
        {
            ModuleSuspensionTuning tuning;
            if (!tunings.TryGetValue(suspension, out tuning))
            {
                tuning = suspension.part != null ? suspension.part.FindModuleImplementing<ModuleSuspensionTuning>() : null;
                tunings[suspension] = tuning;
                if (tuning == null && !missingReported)
                {
                    missingReported = true;
                    Log.Warning($"{suspension.part?.partInfo?.name} has a suspension without the tuning module " +
                        "ModuleManager adds: the game sets it, as without the fix. Is ModuleManager installed?");
                }
            }
            return tuning;
        }

        /// <summary>Skips the game's setting of a suspension the fix sets.</summary>
        private static bool SuspensionSpringUpdatePrefix(ModuleWheelSuspension __instance)
        {
            if (!Active)
            {
                return true;
            }
            ModuleSuspensionTuning tuning = Tuning(__instance);
            return tuning == null || !tuning.IsTuned;
        }

        /// <summary>
        /// Sets the suspension's spring and damper to the player's setting, in the module's own fields, which
        /// DamperBounce starts from, and which the game hands to a wheel it sets up again, and on the wheel collider:
        /// the spring always, the damper only while DamperBounce is off, DamperBounce writing its own bound of it
        /// otherwise. Predicts a setting for the suspensions of the vessel never set, at the first step it sees one.
        /// </summary>
        private static void FixedUpdatePrefix(ModuleWheelSuspension __instance)
        {
            if (!Active || !HighLogic.LoadedSceneIsFlight || __instance.part == null || __instance.part.packed)
            {
                return;
            }
            KSPWheelController wheel = Wheel(__instance);
            Vessel vessel = __instance.vessel;
            // A retracted leg's wheel is switched off: nothing to set, and a write would wake its body for nothing.
            if (wheel == null || !wheel.enabled || wheel.wheelCollider == null || vessel == null)
            {
                return;
            }
            ModuleSuspensionTuning tuning = Tuning(__instance);
            if (tuning == null)
            {
                return;
            }
            if (!tuning.IsTuned)
            {
                if (!predicted.Contains(tuning))
                {
                    PredictUnset(vessel);
                }
                if (!tuning.IsTuned)
                {
                    return;
                }
            }
            float spring = tuning.springRate;
            float share = tuning.tunedMass > 0f ? tuning.tunedMass : SharedMass(vessel);
            float effective, movedMass;
            bool capped;
            float damper = Damper(spring, tuning.dampingRatio, share, __instance.part, out effective, out capped,
                out movedMass);
            StockSpring(__instance) = spring;
            StockDamper(__instance) = damper;
            // The game sets the spring back to 0 when the vessel's part count changes, or it docks: write at every step.
            if (wheel.wheelCollider.springRate != spring)
            {
                wheel.wheelCollider.springRate = spring;
            }
            // DamperBounce writes its own bound of this damper at every step, on the ground and off it: writing it here
            // too would change the wheel's damper twice a step.
            if (!DamperBounce.Enabled && wheel.wheelCollider.damperRate != damper)
            {
                wheel.wheelCollider.damperRate = damper;
            }
        }

        /// <summary>The vessel's mass shared equally among all its suspensions, in t.</summary>
        private static float SharedMass(Vessel vessel)
        {
            int count = 0;
            foreach (Part part in vessel.parts)
            {
                if (part.FindModuleImplementing<ModuleWheelSuspension>() != null)
                {
                    count++;
                }
            }
            return (float)vessel.totalMass / Mathf.Max(count, 1);
        }

        /// <summary>
        /// Returns the damper to write for a spring <paramref name="spring"/> carrying <paramref name="share"/> t at the
        /// damping ratio <paramref name="damping"/>, aimed at 2 ζ √(k m) (<paramref name="effective"/>). While
        /// DamperBounce is on, it returns the damper that DamperBounce turns into that aim when it makes it implicit on
        /// <paramref name="movedMass"/>, the mass the wheel's part moves in a step, the aim held to at most
        /// <see cref="ImplicitCeiling"/> of what that implicit damper can give (<paramref name="capped"/>); while it is
        /// off, it returns the aim itself, <paramref name="movedMass"/> 0.
        /// </summary>
        internal static float Damper(float spring, float damping, float share, Part part, out float effective,
            out bool capped, out float movedMass)
        {
            double aimed = 2.0 * damping * Math.Sqrt(Math.Max(spring, 0f) * Math.Max(share, 0f));
            double damper = aimed;
            capped = false;
            movedMass = 0f;
            if (part != null && DamperBounce.Enabled)
            {
                // DamperBounce sets c / (1 + c dt / m) on the wheel, m the mass its part moves within a step: the
                // damper that gives the effective one aimed at is c = e / (1 - e dt / m), while e < m / dt.
                movedMass = DamperBounce.MovedMass(part);
                if (movedMass > 0f)
                {
                    double dt = Time.fixedDeltaTime;
                    double most = movedMass / dt;
                    if (aimed >= ImplicitCeiling * most)
                    {
                        aimed = ImplicitCeiling * most;
                        capped = true;
                    }
                    damper = aimed / (1.0 - aimed * dt / movedMass);
                }
            }
            effective = (float)aimed;
            return (float)damper;
        }

        /// <summary>
        /// Predicts a setting for each suspension of the vessel never set: from all its suspensions, its mass shared
        /// equally among them, on the body it orbits, at the default compression and damping ratio. The suspensions
        /// already set keep theirs. Each suspension never set is predicted once, whether the prediction sets it or not.
        /// </summary>
        private static void PredictUnset(Vessel vessel)
        {
            List<ModuleSuspensionTuning> all = TuningsOf(vessel.parts);
            CelestialBody body = vessel.mainBody;
            // No centre of mass: shared equally, as the window's Auto does with its centre of mass option off.
            List<Prediction> predictions = Predict(all, vessel.totalMass, null, SurfaceGravity(body), Compression);
            string what = $"predicted in flight for {body.bodyName}, {vessel.totalMass:0.###} t over " +
                $"{predictions.Count}, shared equally";
            int set = 0;
            foreach (Prediction p in predictions)
            {
                if (!p.Tuning.IsTuned)
                {
                    p.Tuning.Set(p.Spring, Damping, p.Share, what);
                    set++;
                }
            }
            foreach (ModuleSuspensionTuning t in all)
            {
                if (!t.IsTuned)
                {
                    predicted.Add(t);
                }
            }
            Log.Info($"{vessel.vesselName}: {set} suspensions never set, {what}{Describe(predictions)}");
        }

        /// <summary>The tuning modules of the given parts, in their order.</summary>
        internal static List<ModuleSuspensionTuning> TuningsOf(IEnumerable<Part> parts)
        {
            List<ModuleSuspensionTuning> result = new List<ModuleSuspensionTuning>();
            foreach (Part part in parts)
            {
                ModuleSuspensionTuning tuning = part.FindModuleImplementing<ModuleSuspensionTuning>();
                if (tuning != null && part.FindModuleImplementing<ModuleWheelSuspension>() != null)
                {
                    result.Add(tuning);
                }
            }
            return result;
        }

        /// <summary>The gravity at the body's sea level, in m/s².</summary>
        internal static double SurfaceGravity(CelestialBody body)
        {
            return body.GeeASL * PhysicsGlobals.GravitationalAcceleration;
        }

        /// <summary>The setting predicted for one suspension.</summary>
        internal struct Prediction
        {
            public ModuleSuspensionTuning Tuning;
            /// <summary>The share of the mass the suspension carries, in t.</summary>
            public float Share;
            /// <summary>The spring that sinks it to the compression asked for under that share, in kN/m.</summary>
            public float Spring;
            /// <summary>The compression the spring was sized for, lower than asked when the vessel would tip.</summary>
            public float Compression;
        }

        /// <summary>One suspension that carries the vessel, where it stands, and along which axis it works.</summary>
        private struct Carrier
        {
            public ModuleSuspensionTuning Tuning;
            public ModuleWheelSuspension Suspension;
            public Vector3 Position;
            public Vector3 Down;
        }

        /// <summary>
        /// Predicts the spring of each suspension in <paramref name="tunings"/> for the vessel standing on them alone,
        /// on a flat ground perpendicular to their mean axis, under a gravity <paramref name="gravity"/>: each carries
        /// the share of <paramref name="mass"/> that balances it about <paramref name="centre"/>, or an equal share
        /// without a centre, and gets the spring that sinks it to <paramref name="compression"/> of its travel under
        /// that share, its own weight left out as PhysX leaves it out. With a centre, the sinking is lowered until the
        /// springs keep the vessel from tipping with <see cref="TipMargin"/>. Folded legs count, with their deployed
        /// pose; broken wheels do not.
        /// </summary>
        internal static List<Prediction> Predict(List<ModuleSuspensionTuning> tunings, double mass, Vector3? centre,
            double gravity, double compression)
        {
            List<Prediction> result = new List<Prediction>();
            List<Carrier> carriers = new List<Carrier>();
            Vector3 axis = Vector3.zero;
            foreach (ModuleSuspensionTuning t in tunings)
            {
                ModuleWheelSuspension s = t.Suspension ?? t.part.FindModuleImplementing<ModuleWheelSuspension>();
                if (s == null)
                {
                    continue;
                }
                ModuleWheelDamage damage = t.part.FindModuleImplementing<ModuleWheelDamage>();
                if (damage != null && damage.isDamaged)
                {
                    continue;
                }
                Vector3 position, down;
                DeployedPose(t.part, out position, out down);
                carriers.Add(new Carrier { Tuning = t, Suspension = s, Position = position, Down = down });
                axis += down;
            }
            if (carriers.Count == 0 || mass <= 0 || gravity <= 0)
            {
                return result;
            }
            if (axis.sqrMagnitude < 1e-6f)
            {
                axis = carriers[0].Down;
            }
            axis.Normalize();
            double[] shares = centre.HasValue
                ? Shares(carriers, centre.Value, axis, mass)
                : EqualShares(carriers.Count, mass);

            double tipSinking = double.PositiveInfinity;
            if (centre.HasValue)
            {
                // A spring sunk by x under its share F has k = F g / x. Leaning by a small angle, the springs right the
                // vessel with a moment sum(k r^2) = g / x sum(F r^2), r the wheel's distance across the tilt axis, and
                // its weight tips it with M g h, h the height of the centre of mass above the wheels: it stands while
                // x < sum(F r^2) / (M h), along the weakest tilt axis.
                Vector3 e1 = Vector3.Cross(axis, Mathf.Abs(axis.x) < 0.9f ? Vector3.right : Vector3.forward).normalized;
                Vector3 e2 = Vector3.Cross(axis, e1);
                double fuu = 0, fvv = 0, fuv = 0, height = 0;
                for (int i = 0; i < carriers.Count; i++)
                {
                    Vector3 r = carriers[i].Position - centre.Value;
                    double u = Vector3.Dot(r, e1), v = Vector3.Dot(r, e2);
                    fuu += shares[i] * u * u; fvv += shares[i] * v * v; fuv += shares[i] * u * v;
                    // The vessel tips about its contacts: below the top of the suspension by its travel and the wheel's
                    // radius, fully extended, the highest the wheel can stand.
                    ModuleWheelSuspension s = carriers[i].Suspension;
                    ModuleWheelBase wheelBase = s.part.FindModuleImplementing<ModuleWheelBase>();
                    double reach = (s.suspensionDistance + (wheelBase != null ? wheelBase.radius : 0f)) * s.part.rescaleFactor;
                    height += shares[i] * (Vector3.Dot(r, axis) + reach) / mass;
                }
                double weakest = 0.5 * (fuu + fvv) - Math.Sqrt(0.25 * (fuu - fvv) * (fuu - fvv) + fuv * fuv);
                // On fewer than three contacts out of line, nothing the springs do keeps the vessel from tipping: no
                // bound.
                if (height > 0.01 && TipMargin > 0 && weakest > 1e-6 * mass)
                {
                    tipSinking = weakest / (TipMargin * mass * height);
                }
            }

            for (int i = 0; i < carriers.Count; i++)
            {
                ModuleWheelSuspension s = carriers[i].Suspension;
                double cos = Math.Max(Vector3.Dot(carriers[i].Down, axis), 0.1f);
                double travel = Math.Max(s.suspensionDistance * s.part.rescaleFactor, 0.01);
                double sinking = Math.Min(compression * travel, tipSinking);
                // The spring pushes along its axis and the ground takes the part along its normal: cos of it holds the
                // weight.
                double load = shares[i] * gravity / cos;
                result.Add(new Prediction
                {
                    Tuning = carriers[i].Tuning,
                    Share = (float)shares[i],
                    Spring = SpringFor(load, SprungMass(s) * gravity, sinking, travel),
                    Compression = (float)(sinking / travel),
                });
            }
            return result;
        }

        /// <summary>
        /// Returns the spring, in kN/m, that sinks a suspension of travel <paramref name="travel"/> by
        /// <paramref name="sinking"/> under <paramref name="load"/> kN, the wheel's own weight
        /// <paramref name="wheelWeight"/> kN left out. VPWheelCollider sets the collider's rest point at
        /// t = sprungMass g / (k travel), kept within [0.1, 1], and PhysX adds no weight of its own (KSP applies gravity
        /// itself): the spring pushes k (x - t travel). Unbound, k = (load + sprungMass g) / x; where t would fall under
        /// 0.1, k = load / (x - 0.1 travel).
        /// </summary>
        internal static float SpringFor(double load, double wheelWeight, double sinking, double travel)
        {
            double spring = (load + wheelWeight) / Math.Max(sinking, 0.001);
            if (wheelWeight < RestFloor * spring * travel)
            {
                spring = load / Math.Max(sinking - RestFloor * travel, 0.01 * travel);
            }
            return (float)spring;
        }

        /// <summary>The least rest point VPWheelCollider gives a wheel collider, as a fraction of its travel.</summary>
        internal const double RestFloor = 0.1;

        /// <summary>
        /// Returns how far, in m, a spring <paramref name="spring"/> kN/m of travel <paramref name="travel"/> sinks under
        /// <paramref name="load"/> kN, the wheel's own weight <paramref name="wheelWeight"/> kN left out: the inverse of
        /// <see cref="SpringFor"/>, load / k + t travel.
        /// </summary>
        internal static double SinkingUnder(double load, double wheelWeight, double spring, double travel)
        {
            double rest = Math.Min(Math.Max(wheelWeight / (spring * travel), RestFloor), 1.0);
            return load / spring + rest * travel;
        }

        /// <summary>
        /// Returns the mass of the wheel's collider, in t, whose weight the spring does not carry: the mass of the
        /// suspension's part, its resources included, as the game knows it in the editor and in flight alike.
        /// </summary>
        internal static float SprungMass(ModuleWheelSuspension suspension)
        {
            // Not WheelCollider.sprungMass: at the first physics step of a vessel, before Unity has applied the part's
            // mass to the collider, it still holds Unity's default of 1 t, which made the spring of a 50 kg wheel ten
            // times too stiff. The collider's sprung mass is the part's mass once applied.
            Part part = suspension.part;
            return part.mass + part.GetResourceMass();
        }

        /// <summary>
        /// Gives where the suspension's wheel collider stands, and its axis, in world space, as they are once the leg
        /// is deployed: from the measure of the part's prefab for a deployable part, from the collider's transform
        /// otherwise, from the part's own when it has none.
        /// </summary>
        private static void DeployedPose(Part part, out Vector3 position, out Vector3 down)
        {
            LegGeometryProbe.Pose[] measured;
            if (part.FindModuleImplementing<ModuleWheelDeployment>() != null &&
                LegGeometryProbe.Measured.TryGetValue(part.partInfo.name, out measured))
            {
                position = part.transform.TransformPoint(measured[0].Position);
                down = part.transform.rotation * measured[0].Rotation * Vector3.down;
                return;
            }
            ModuleWheelBase wheelBase = part.FindModuleImplementing<ModuleWheelBase>();
            Transform host = wheelBase == null ? null
                : wheelBase.wheelColliderHost != null ? wheelBase.wheelColliderHost
                : part.FindModelTransform(wheelBase.wheelColliderTransformName);
            if (host == null)
            {
                position = part.transform.position;
                down = -part.transform.up;
                return;
            }
            position = host.position;
            down = -host.up;
        }

        /// <summary><paramref name="mass"/> shared equally among <paramref name="n"/> suspensions.</summary>
        private static double[] EqualShares(int n, double mass)
        {
            double[] shares = new double[n];
            for (int i = 0; i < n; i++)
            {
                shares[i] = mass / n;
            }
            return shares;
        }

        /// <summary>
        /// Shares <paramref name="mass"/> among the carriers so that the vessel stands balanced on them, on a ground
        /// perpendicular to <paramref name="axis"/>: each share is a + b u + c v of the carrier's position (u, v) about
        /// the centre of mass in the ground's plane, as identical springs under a rigid plate share it. A share below 0
        /// (centre of mass outside the carriers) is set to 0 and the others scaled back to the mass.
        /// </summary>
        private static double[] Shares(List<Carrier> carriers, Vector3 centre, Vector3 axis, double mass)
        {
            int n = carriers.Count;
            double[] shares = new double[n];
            Vector3 e1 = Vector3.Cross(axis, Mathf.Abs(axis.x) < 0.9f ? Vector3.right : Vector3.forward).normalized;
            Vector3 e2 = Vector3.Cross(axis, e1);
            double[] u = new double[n], v = new double[n];
            double su = 0, sv = 0, suu = 0, svv = 0, suv = 0;
            for (int i = 0; i < n; i++)
            {
                Vector3 r = carriers[i].Position - centre;
                u[i] = Vector3.Dot(r, e1);
                v[i] = Vector3.Dot(r, e2);
                su += u[i]; sv += v[i]; suu += u[i] * u[i]; svv += v[i] * v[i]; suv += u[i] * v[i];
            }
            // Solve [n su sv; su suu suv; sv suv svv] (a b c) = (mass 0 0).
            double det = n * (suu * svv - suv * suv) - su * (su * svv - suv * sv) + sv * (su * suv - suu * sv);
            double scale = Math.Max(suu + svv, 1e-6);
            if (n >= 3 && Math.Abs(det) > 1e-6 * scale * scale * n)
            {
                double a = mass * (suu * svv - suv * suv) / det;
                double b = mass * -(su * svv - suv * sv) / det;
                double c = mass * (su * suv - suu * sv) / det;
                for (int i = 0; i < n; i++)
                {
                    shares[i] = a + b * u[i] + c * v[i];
                }
            }
            else
            {
                // On a line, or fewer than 3: balance along the line's direction only, or share equally.
                double lineDet = n * (suu + svv) - (su * su + sv * sv);
                for (int i = 0; i < n; i++)
                {
                    shares[i] = n > 1 && lineDet > 1e-9
                        ? mass * ((suu + svv) - (su * u[i] + sv * v[i])) / lineDet
                        : mass / n;
                }
            }
            double sum = 0;
            for (int i = 0; i < n; i++)
            {
                shares[i] = Math.Max(shares[i], 0);
                sum += shares[i];
            }
            for (int i = 0; i < n; i++)
            {
                // A wheel the balance leaves without weight still gets a little, should it touch all the same.
                shares[i] = sum > 0 ? Math.Max(shares[i] * mass / sum, 0.01 * mass / n) : mass / n;
            }
            return shares;
        }

        /// <summary>One line per prediction, for the log.</summary>
        internal static string Describe(List<Prediction> predictions)
        {
            StringBuilder text = new StringBuilder();
            foreach (Prediction p in predictions)
            {
                text.Append($"\n  {p.Tuning.part.partInfo.name} #{p.Tuning.part.persistentId}: share {p.Share:0.###} t, " +
                    $"spring {p.Spring:0.##} kN/m at {100f * p.Compression:0} %, damping ratio {p.Tuning.dampingRatio:0.00}");
            }
            return text.ToString();
        }
    }
}
