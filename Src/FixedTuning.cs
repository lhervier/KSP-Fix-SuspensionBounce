using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using ModuleWheels;
using UnityEngine;

namespace com.github.lhervier.ksp.fix.suspensionbounce
{
    /// <summary>
    /// Prototype: replaces the game's spring and damper, recomputed at each physics step from the wheel's compression,
    /// with a setting that only depends on the vessel: its mass, its centre of mass, where its wheels stand once
    /// deployed, and the surface gravity of the body it orbits. Every wheel is assumed to touch a flat ground
    /// perpendicular to the wheels' mean axis; each carries the share of the weight that balances the vessel, and gets
    /// the spring that sinks it to a set fraction of its travel under that share, and the damper of a set damping ratio.
    /// The same setting holds in the air and on the ground, at every step.
    /// </summary>
    internal static class FixedTuning
    {
        /// <summary>Whether the fix is installed. Read from settings.cfg.</summary>
        public static bool Enabled = false;

        /// <summary>
        /// Whether the installed fix sets the suspensions; false gives them back to the game and the other fixes, as
        /// without it. Switched in game with set_member, to compare both on the same session.
        /// </summary>
        public static bool Active = true;

        // Investigation switches (prototype only), set in game with set_member: skip the game's setting, write the
        // module's spring and damper fields, write the wheel collider's spring and damper, compute at all.
        public static bool SkipStock = true;
        public static bool WriteFields = true;
        public static bool WriteWheel = true;
        public static bool ComputeAtAll = true;

        /// <summary>Whether the fix is installed and sets the suspensions.</summary>
        public static bool IsActive => Enabled && Active;

        /// <summary>The fraction of its travel a wheel sinks to under its share of the weight. Read from settings.cfg.
        /// </summary>
        public static float Compression = 0.5f;

        /// <summary>
        /// The damping ratio of each wheel on its share of the mass, as the wheel gets it once the damper fix has made
        /// it implicit on the mass the wheel's part moves (see <see cref="SuspensionDamper"/>). Read from settings.cfg.
        /// </summary>
        public static float Damping = 0.3f;

        /// <summary>
        /// The most of m / dt, the most an implicit damper can give on the mass m it moves, the damper aims at when the
        /// damping ratio asks for more.
        /// </summary>
        private const double ImplicitCeiling = 0.9;

        /// <summary>
        /// How many times the springs' righting moment must exceed the weight's tipping moment when the vessel leans:
        /// the sinking is lowered below <see cref="Compression"/> until it does. 0 leaves it. Read from settings.cfg,
        /// switched in game with set_member.
        /// </summary>
        public static float TipMargin = 2f;

        /// <summary>
        /// The wheels that carry the vessel, by persistent id, separated by commas or spaces; empty for all of them.
        /// Stands for the player's list until there is a way to pick it in game: set it with set_member. A vessel with
        /// none of these ids uses all its wheels.
        /// </summary>
        public static string Wheels = "";

        private static readonly MethodInfo SuspensionSpringUpdate =
            AccessTools.DeclaredMethod(typeof(ModuleWheelSuspension), "SuspensionSpringUpdate");

        private static readonly MethodInfo FixedUpdate =
            AccessTools.DeclaredMethod(typeof(ModuleWheelSuspension), "FixedUpdate");

        private static readonly AccessTools.FieldRef<ModuleWheelSubmodule, KSPWheelController> Wheel =
            AccessTools.FieldRefAccess<ModuleWheelSubmodule, KSPWheelController>("wheel");

        private static readonly AccessTools.FieldRef<ModuleWheelSuspension, float> StockSpring =
            AccessTools.FieldRefAccess<ModuleWheelSuspension, float>("spring");

        private static readonly AccessTools.FieldRef<ModuleWheelSuspension, float> StockDamper =
            AccessTools.FieldRefAccess<ModuleWheelSuspension, float>("damper");

        /// <summary>The spring and damper of one suspension, and what they were sized for.</summary>
        private struct Setting
        {
            public float Spring;
            public float Damper;
            public float Share;
            /// <summary>The damper the wheel gets once the damper fix has made it implicit, and whether the damping
            /// ratio asked for more than it can give.</summary>
            public float Effective;
            public bool Capped;
            public float MovedMass;
            /// <summary>The sprung mass of the wheel's collider, whose weight the spring does not carry.</summary>
            public float SprungMass;
        }

        /// <summary>The settings of each vessel's suspensions, computed for the physics step <see cref="step"/>.</summary>
        private static readonly Dictionary<Vessel, Dictionary<ModuleWheelSuspension, Setting>> settings =
            new Dictionary<Vessel, Dictionary<ModuleWheelSuspension, Setting>>();
        private static float step = -1f;

        /// <summary>What was last logged for each vessel, to log again only when the setting changes.</summary>
        private static readonly Dictionary<Guid, List<float>> logged = new Dictionary<Guid, List<float>>();

        /// <summary>Whether both lists have the same length and every value within 2 % of its counterpart.</summary>
        private static bool Close(List<float> a, List<float> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }
            for (int i = 0; i < a.Count; i++)
            {
                if (Math.Abs(a[i] - b[i]) > 0.02f * Math.Max(Math.Abs(a[i]), Math.Abs(b[i])))
                {
                    return false;
                }
            }
            return true;
        }

        private static string parsedWheels;
        private static readonly HashSet<uint> chosen = new HashSet<uint>();

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
            GameEvents.onGameSceneLoadRequested.Add(scene => logged.Clear());
        }

        /// <summary>Skips the game's setting: the fix sets the spring and damper before it, at every step.</summary>
        private static bool SuspensionSpringUpdatePrefix()
        {
            return !(Active && SkipStock);
        }

        /// <summary>
        /// Sets the suspension's spring and damper to the vessel's setting, on the wheel collider and in the module's
        /// own fields, which the damper fix starts from, and which the game hands to a wheel it sets up again.
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
            if (!ComputeAtAll)
            {
                return;
            }
            Dictionary<ModuleWheelSuspension, Setting> vesselSettings = Of(vessel);
            Setting setting;
            if (vesselSettings == null || !vesselSettings.TryGetValue(__instance, out setting))
            {
                return;
            }
            if (WriteFields)
            {
                StockSpring(__instance) = setting.Spring;
                StockDamper(__instance) = setting.Damper;
            }
            if (!WriteWheel)
            {
                return;
            }
            // The game sets the spring back to 0 when the vessel's part count changes, or it docks: write at every step.
            if (wheel.wheelCollider.springRate != setting.Spring)
            {
                wheel.wheelCollider.springRate = setting.Spring;
            }
            // The damper fix writes its own bound of this damper at every step, on the ground and off it: writing it
            // here too would change the wheel's damper twice a step.
            bool damperFixWrites = SuspensionDamper.OnGround && SuspensionDamper.InAir;
            if (!damperFixWrites && wheel.wheelCollider.damperRate != setting.Damper)
            {
                wheel.wheelCollider.damperRate = setting.Damper;
            }
        }

        /// <summary>Returns the settings of the vessel's suspensions for this physics step, computed once per step.
        /// </summary>
        private static Dictionary<ModuleWheelSuspension, Setting> Of(Vessel vessel)
        {
            if (Time.fixedTime != step)
            {
                settings.Clear();
                step = Time.fixedTime;
            }
            Dictionary<ModuleWheelSuspension, Setting> result;
            if (!settings.TryGetValue(vessel, out result))
            {
                result = Compute(vessel);
                settings[vessel] = result;
            }
            return result;
        }

        /// <summary>One suspension that carries the vessel, where it stands, and along which axis it works.</summary>
        private struct Carrier
        {
            public ModuleWheelSuspension Suspension;
            public Vector3 Position;
            public Vector3 Down;
        }

        private static Dictionary<ModuleWheelSuspension, Setting> Compute(Vessel vessel)
        {
            ParseWheels();
            List<ModuleWheelSuspension> all = vessel.FindPartModulesImplementing<ModuleWheelSuspension>();
            bool anyChosen = false;
            foreach (ModuleWheelSuspension s in all)
            {
                anyChosen |= chosen.Contains(s.part.persistentId);
            }
            List<Carrier> carriers = new List<Carrier>();
            Vector3 axis = Vector3.zero;
            foreach (ModuleWheelSuspension s in all)
            {
                ModuleWheelDamage damage = s.part.FindModuleImplementing<ModuleWheelDamage>();
                if (damage != null && damage.isDamaged)
                {
                    continue;
                }
                if (anyChosen && !chosen.Contains(s.part.persistentId))
                {
                    continue;
                }
                // A folded leg carries nothing: the game switches its wheel off below 30 % of its travel out. It counts
                // from the step its wheel is switched on, with its deployed pose: deploying or folding it sets the
                // vessel again.
                KSPWheelController wheel = Wheel(s);
                if (wheel == null || !wheel.enabled)
                {
                    continue;
                }
                Vector3 position, down;
                if (!DeployedPose(s.part, out position, out down))
                {
                    continue;
                }
                carriers.Add(new Carrier { Suspension = s, Position = position, Down = down });
                axis += down;
            }
            Dictionary<ModuleWheelSuspension, Setting> result = new Dictionary<ModuleWheelSuspension, Setting>();
            if (carriers.Count == 0 || axis.sqrMagnitude < 1e-6f)
            {
                return result;
            }
            axis.Normalize();
            double mass = vessel.totalMass;
            double[] shares = Shares(carriers, vessel.CoM, axis, mass);
            // The gravity where the vessel stands, the one ModuleWheelBase hands its wheels: 12 % under the surface
            // value on the highlands of Minmus.
            double g = vessel.gravityForPos.magnitude;
            if (g <= 0)
            {
                g = vessel.mainBody.GeeASL * PhysicsGlobals.GravitationalAcceleration;
            }

            // A spring sunk by x under its share F has k = F g / x. Leaning by a small angle, the springs right the
            // vessel with a moment sum(k r^2) = g / x sum(F r^2), r the wheel's distance across the tilt axis, and its
            // weight tips it with M g h, h the height of the centre of mass above the wheels: it stands while
            // x < sum(F r^2) / (M h), along the weakest tilt axis.
            Vector3 e1 = Vector3.Cross(axis, Mathf.Abs(axis.x) < 0.9f ? Vector3.right : Vector3.forward).normalized;
            Vector3 e2 = Vector3.Cross(axis, e1);
            double fuu = 0, fvv = 0, fuv = 0, height = 0;
            for (int i = 0; i < carriers.Count; i++)
            {
                Vector3 r = carriers[i].Position - vessel.CoM;
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
            // On fewer than three contacts out of line, nothing the springs do keeps the vessel from tipping: no bound.
            double tipSinking = height > 0.01 && TipMargin > 0 && weakest > 1e-6 * mass
                ? weakest / (TipMargin * mass * height) : double.PositiveInfinity;
            for (int i = 0; i < carriers.Count; i++)
            {
                ModuleWheelSuspension s = carriers[i].Suspension;
                double cos = Math.Max(Vector3.Dot(carriers[i].Down, axis), 0.1f);
                double travel = s.suspensionDistance * s.part.rescaleFactor;
                double share = shares[i];
                // The game's sliders, set by hand, multiply the setting; left to the game, they count as 1.
                double springFactor = s.autoSpringDamper ? 1.0 : s.springTweakable;
                double damperFactor = s.autoSpringDamper ? 1.0 : s.damperTweakable;
                double sinking = Math.Min(Compression * Math.Max(travel, 0.01), tipSinking);
                // The spring pushes along its axis and the ground takes the part along its normal: cos of it holds the weight.
                double load = share * g / cos;
                // VPWheelCollider sets the collider's rest point at t = sprungMass g / (k travel), kept within
                // [0.1, 1], and PhysX adds no weight of its own (KSP applies gravity itself): the spring pushes
                // k (x - t travel). Unbound, k = (load + sprungMass g) / x; where t would fall under 0.1,
                // k = load / (x - 0.1 travel).
                float sprungMass = SprungMass(Wheel(s));
                double sprungWeight = sprungMass * g;
                double spring = (load + sprungWeight) / Math.Max(sinking, 0.001);
                if (sprungWeight < 0.1 * spring * travel)
                {
                    spring = load / Math.Max(sinking - 0.1 * travel, 0.01 * Math.Max(travel, 0.01));
                }
                spring *= springFactor;
                double effective = 2.0 * Damping * Math.Sqrt(spring * share) * damperFactor;
                double damper = effective;
                bool capped = false;
                float movedMass = 0f;
                if (SuspensionDamper.OnGround || SuspensionDamper.InAir)
                {
                    // The damper fix sets c / (1 + c dt / m) on the wheel, m the mass its part moves within a step: the
                    // damper that gives the effective one aimed at is c = e / (1 - e dt / m), while e < m / dt.
                    movedMass = SuspensionDamper.MovedMass(s.part);
                    if (movedMass > 0f)
                    {
                        double dt = Time.fixedDeltaTime;
                        double most = movedMass / dt;
                        if (effective >= ImplicitCeiling * most)
                        {
                            effective = ImplicitCeiling * most;
                            capped = true;
                        }
                        damper = effective / (1.0 - effective * dt / movedMass);
                    }
                }
                result[s] = new Setting
                {
                    Spring = (float)spring, Damper = (float)damper, Share = (float)share,
                    Effective = (float)effective, Capped = capped, MovedMass = movedMass, SprungMass = sprungMass,
                };
            }
            LogSetting(vessel, result, carriers.Count, all.Count, g,
                $"height {height:0.###} m, weakest sum(F r^2)/M {weakest / mass:0.####} m2, tip sinking {tipSinking:0.###} m");
            return result;
        }

        /// <summary>
        /// Returns the sprung mass Unity gives the wheel's collider, in t, or 0 when it has none.
        /// </summary>
        private static float SprungMass(KSPWheelController wheel)
        {
            WheelCollider collider = wheel != null && wheel.wheelCollider != null
                ? wheel.wheelCollider.GetComponent<WheelCollider>() : null;
            return collider != null ? collider.sprungMass : 0f;
        }

        /// <summary>
        /// Gives where the suspension's wheel collider stands, and its axis, in world space, as they are once the leg
        /// is deployed: from the measure of the part's prefab for a deployable part, from the live collider otherwise.
        /// Returns false when the part has no wheel collider to read.
        /// </summary>
        private static bool DeployedPose(Part part, out Vector3 position, out Vector3 down)
        {
            position = down = Vector3.zero;
            LegGeometryProbe.Pose[] measured;
            if (part.FindModuleImplementing<ModuleWheelDeployment>() != null &&
                LegGeometryProbe.Measured.TryGetValue(part.partInfo.name, out measured))
            {
                position = part.transform.TransformPoint(measured[0].Position);
                down = part.transform.rotation * measured[0].Rotation * Vector3.down;
                return true;
            }
            ModuleWheelBase wheelBase = part.FindModuleImplementing<ModuleWheelBase>();
            if (wheelBase == null || wheelBase.wheelColliderHost == null)
            {
                return false;
            }
            position = wheelBase.wheelColliderHost.position;
            down = -wheelBase.wheelColliderHost.up;
            return true;
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
                    double s = n > 1 && lineDet > 1e-9
                        ? mass * ((suu + svv) - (su * u[i] + sv * v[i])) / lineDet
                        : mass / n;
                    shares[i] = s;
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

        private static void ParseWheels()
        {
            if (Wheels == parsedWheels)
            {
                return;
            }
            parsedWheels = Wheels;
            chosen.Clear();
            foreach (string item in (Wheels ?? "").Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                uint id;
                if (uint.TryParse(item, out id))
                {
                    chosen.Add(id);
                }
            }
            Log.Info($"Fixed tuning: wheels chosen '{Wheels}', {chosen.Count} ids");
        }

        /// <summary>
        /// Logs the vessel's setting when a spring, a damper or a moved mass differs by more than 2 % from the one
        /// last logged.
        /// </summary>
        private static void LogSetting(Vessel vessel, Dictionary<ModuleWheelSuspension, Setting> result, int carriers,
            int all, double g, string geometry)
        {
            List<float> values = new List<float>();
            StringBuilder text = new StringBuilder();
            foreach (KeyValuePair<ModuleWheelSuspension, Setting> pair in result)
            {
                values.Add(pair.Value.Spring);
                values.Add(pair.Value.Damper);
                values.Add(pair.Value.MovedMass);
                text.Append($"\n  {pair.Key.part.partInfo.name} #{pair.Key.part.persistentId}: share " +
                    $"{pair.Value.Share:0.###} t, sprung {pair.Value.SprungMass:0.###} t, spring {pair.Value.Spring:0.#}, damper {pair.Value.Damper:0.##} " +
                    $"(effective {pair.Value.Effective:0.##} on {pair.Value.MovedMass:0.###} t moved" +
                    (pair.Value.Capped ? ", capped: the damping ratio asks for more" : "") + ")");
            }
            List<float> previous;
            if (logged.TryGetValue(vessel.id, out previous) && Close(previous, values))
            {
                return;
            }
            logged[vessel.id] = values;
            Log.Info($"Fixed tuning of {vessel.vesselName}: {vessel.totalMass:0.###} t, g {g:0.###}, {carriers} of " +
                $"{all} wheels carry, {geometry}{text}");
        }
    }
}
