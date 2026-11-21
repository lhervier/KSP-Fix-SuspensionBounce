using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace com.github.lhervier.ksp.fix.suspensions.vesselbodies
{
    /// <summary>
    /// Keeps contacts of a vessel within their limits for the coming physics step, the vessel taken as one rigid body.
    /// <para>
    /// A caller hands, once per vessel and step, its contacts: each a point on a body, the direction a force may push
    /// it along, how fast the gap of the contact opens, and how much of that gap the coming step may close. The solver
    /// takes the vessel as one rigid body (its bodies read by <see cref="VesselBodies"/>), pushed by the forces known to
    /// act on it over the last step (KSP's gravity and its modules' forces, recorded here, and those the caller knows),
    /// and finds the least forces, none pulling, that keep every contact from closing more than its gap by the end of
    /// the coming step, all at once. It applies them to the bodies, and checks by the vessel's change of momentum over
    /// the step what other forces pushed it (<see cref="Balance.unexplainedNormal"/>).
    /// </para>
    /// </summary>
    public static class ContactSolver
    {
        // Knobs, public so that they can be changed in game (KSP-Util-MCPServer set_member).

        /// <summary>
        /// Weight of the size of the forces in their solve, relative to the contacts' scale: four contacts give more
        /// forces than the three equations (one force, two torques) need, and this picks the evenest.
        /// </summary>
        public static float Regularization = 0.1f;

        /// <summary>Most Gauss-Seidel sweeps of each pass of the solve.</summary>
        public static int Iterations = 30;

        /// <summary>A pass of the solve ends once no force changed by more than this over a sweep (kN).</summary>
        public static float Tolerance = 1e-4f;

        /// <summary>
        /// Passes of the solve: each makes up the shortfall the regularisation leaves, by a factor of about the
        /// regularisation.
        /// </summary>
        public static int Passes = 4;

        /// <summary>
        /// One contact of a vessel. The caller sets, for each step, where and along what its force pushes, how its gap
        /// opens and how much of it the step may close; the solver sets the force.
        /// </summary>
        public class Contact
        {
            /// <summary>Force the solver applies along <see cref="axis"/> for the coming physics step (kN).</summary>
            public float held;

            /// <summary>The body the force is applied to.</summary>
            internal Rigidbody body;
            /// <summary>Where the force is applied, and the contact's velocity is read.</summary>
            internal Vector3 point;
            /// <summary>The direction, a unit vector, along which the force pushes.</summary>
            internal Vector3 axis;
            /// <summary>
            /// A velocity's component on this vector is how fast the gap of the contact opens (positive) or closes.
            /// </summary>
            internal Vector3 rate;
            /// <summary>How much of its gap the contact may close by the end of the coming step (m), none negative.</summary>
            internal float gap;

            /// <summary>When <see cref="held"/> was found, and the force as applied, for the check of the next step.</summary>
            internal float heldTime = -1f;
            internal Vector3 heldForce;
            internal Vector3 heldPoint;
        }

        /// <summary>A force known to act on the vessel over the last physics step, and where.</summary>
        internal struct KnownForce
        {
            public Vector3 force;
            public Vector3 point;
        }

        /// <summary>What the solver found for one vessel at the last physics step, kept for reading in game.</summary>
        public class Balance
        {
            public string vesselName;
            /// <summary>
            /// Force along the contacts' mean normal that pushed the vessel at the last step besides the known forces and
            /// the solver's: the vessel's change of momentum that they do not explain (kN). Near zero while the solver
            /// holds the contacts alone.
            /// </summary>
            public float unexplainedNormal;
            /// <summary>Sum of the forces the solver applies for the coming step (kN).</summary>
            public float heldNormal;
            /// <summary>How many contacts the solver pushes for the coming step.</summary>
            public int holding;
            public float totalMass;
            /// <summary>Whether the vessel was followed at the last step.</summary>
            public bool watching;

            internal Vessel vessel;
            internal float time = -1f;
            /// <summary>The bodies followed, and their velocity at the end of the step before (same index).</summary>
            internal readonly List<Rigidbody> bodies = new List<Rigidbody>();
            internal readonly List<Vector3> velocities = new List<Vector3>();
            internal float statesTime = -1f;
            /// <summary>The vessel's acceleration KSP applied at the last two steps, the latest second.</summary>
            internal float accelerationTime0 = -1f, accelerationTime1 = -1f;
            internal Vector3 acceleration0, acceleration1;
        }

        /// <summary>Forces a part's modules handed its body for one physics step (FlightIntegrator.Integrate).</summary>
        private struct Applied
        {
            public float time;
            public Vector3 force;
            /// <summary>Torque about the body's centre of mass.</summary>
            public Vector3 torque;
        }

        public static readonly List<Balance> Balances = new List<Balance>();

        private static readonly Dictionary<Vessel, Balance> balances = new Dictionary<Vessel, Balance>();

        /// <summary>
        /// For each body whose part's modules handed it a force or a torque, what they handed at the last two physics
        /// steps, the latest second. A body missing here, or without an entry for a step, got none.
        /// </summary>
        private static readonly Dictionary<Rigidbody, Applied[]> applied = new Dictionary<Rigidbody, Applied[]>();

        private static readonly MethodInfo Integrate =
            AccessTools.DeclaredMethod(typeof(FlightIntegrator), "Integrate", new[] { typeof(Part) });

        /// <summary>Whether the forces KSP hands the bodies are recorded at this step: the caller is on.</summary>
        private static Func<bool> recording = () => true;

        private static void IntegratePrefix(Part part)
        {
            IntegratePrefixBody(part);
        }

        /// <summary>
        /// Patches KSP's force step, to record what it hands each body while <paramref name="active"/> says the caller
        /// is on, and forgets everything at each change of scene.
        /// </summary>
        /// <exception cref="MissingMemberException">A member the solver reads is missing from the game.</exception>
        internal static void Install(Harmony harmony, Func<bool> active)
        {
            if (Integrate == null)
            {
                throw new MissingMemberException("FlightIntegrator.Integrate");
            }
            recording = active;
            harmony.Patch(Integrate, prefix: new HarmonyMethod(typeof(ContactSolver), nameof(IntegratePrefix)));
            GameEvents.onGameSceneLoadRequested.Add(scene => Clear());
        }

        private static void Clear()
        {
            Balances.Clear();
            balances.Clear();
            applied.Clear();
        }

        /// <summary>
        /// Records what KSP is about to hand the part's bodies for the coming physics step: the vessel's acceleration
        /// (gravity and the frame's), once per vessel that has a balance, and the forces and torques the part's modules
        /// gathered, for the parts that have any.
        /// </summary>
        private static void IntegratePrefixBody(Part part)
        {
            if (!recording() || part == null)
            {
                return;
            }
            Vessel vessel = part.vessel;
            if (vessel == null)
            {
                return;
            }
            Balance balance;
            if (part == vessel.rootPart && balances.TryGetValue(vessel, out balance))
            {
                float now = Time.fixedTime;
                if (balance.accelerationTime1 != now)
                {
                    balance.accelerationTime0 = balance.accelerationTime1;
                    balance.acceleration0 = balance.acceleration1;
                }
                balance.accelerationTime1 = now;
                balance.acceleration1 = vessel.precalc.integrationAccel;
            }
            if (part.rb == null || (part.forces.Count == 0 && part.force.sqrMagnitude == 0.0 && part.torque.sqrMagnitude == 0.0))
            {
                return;
            }
            Vector3 force = part.force;
            Vector3 torque = part.torque;
            if (part.forces.Count > 0)
            {
                Vector3 centre = part.rb.worldCenterOfMass;
                for (int i = 0; i < part.forces.Count; i++)
                {
                    Vector3 f = part.forces[i].force;
                    force += f;
                    torque += Vector3.Cross((Vector3)part.forces[i].pos - centre, f);
                }
            }
            Applied[] history;
            if (!applied.TryGetValue(part.rb, out history))
            {
                history = new Applied[2];
                applied[part.rb] = history;
            }
            float time = Time.fixedTime;
            if (history[1].time != time)
            {
                history[0] = history[1];
            }
            history[1] = new Applied { time = time, force = force, torque = torque };
        }

        /// <summary>
        /// Returns the force and torque the part's modules handed <paramref name="body"/> for the physics step that
        /// started at <paramref name="time"/>; zero when they handed none.
        /// </summary>
        private static Applied AppliedAt(Rigidbody body, float time)
        {
            Applied[] history;
            if (applied.TryGetValue(body, out history))
            {
                for (int i = 1; i >= 0; i--)
                {
                    if (Mathf.Abs(history[i].time - time) < Time.fixedDeltaTime * 0.25f)
                    {
                        return history[i];
                    }
                }
            }
            return default(Applied);
        }

        /// <summary>
        /// Returns the balance of <paramref name="vessel"/>, created at its first call, for its contacts to be solved at
        /// this physics step (<see cref="Idle"/> or <see cref="Solve"/>); null when it was already returned at this
        /// step: a vessel's contacts are solved once per step.
        /// </summary>
        internal static Balance Begin(Vessel vessel)
        {
            Balance balance;
            if (!balances.TryGetValue(vessel, out balance))
            {
                balance = new Balance { vessel = vessel, vesselName = vessel.vesselName };
                balances[vessel] = balance;
                Balances.Add(balance);
            }
            float now = Time.fixedTime;
            if (balance.time == now)
            {
                return null;
            }
            balance.time = now;
            return balance;
        }

        /// <summary>
        /// Leaves the vessel of <paramref name="balance"/> unfollowed at this step: no force applied, and nothing kept
        /// for the check of the next step.
        /// </summary>
        internal static void Idle(Balance balance)
        {
            balance.heldNormal = 0f;
            balance.holding = 0;
            balance.watching = false;
            balance.unexplainedNormal = 0f;
            balance.statesTime = -1f;
        }

        /// <summary>
        /// Follows the vessel of <paramref name="balance"/> at this step: reads all its bodies, sums the forces known to
        /// act on it over the last step (gravity, the forces KSP's modules applied, and <paramref name="known"/>), and
        /// checks what else pushed it then, along <paramref name="normal"/> (<see cref="Balance.unexplainedNormal"/>),
        /// the forces of the last step counted for every one of <paramref name="contacts"/>. When
        /// <paramref name="hold"/>, finds and applies the forces of <paramref name="touching"/> for the coming step
        /// (<see cref="HoldContacts"/>).
        /// </summary>
        internal static void Solve(Balance balance, List<Contact> contacts, List<Contact> touching, List<KnownForce> known,
            Vector3 normal, bool hold)
        {
            float now = Time.fixedTime;
            float dt = Time.fixedDeltaTime;
            balance.heldNormal = 0f;
            balance.holding = 0;
            balance.watching = true;

            // The bodies of the vessel as they end the last step, read once per step for all the fixes (VesselBodies):
            // their mass and inertia kept from the steps before, their velocities read now.
            bool followed = Mathf.Abs(now - balance.statesTime - dt) < dt * 0.25f;
            VesselBodies read = VesselBodies.Of(balance.vessel);
            read.ReadMotion();
            VesselBodies.Body[] bodiesNow = read.bodies;
            int count = read.count;
            float mass = 0f;
            Vector3 centre = Vector3.zero;
            for (int i = 0; i < count; i++)
            {
                mass += bodiesNow[i].mass;
                centre += bodiesNow[i].mass * bodiesNow[i].centre;
            }
            balance.totalMass = mass;
            if (mass > 0f)
            {
                centre /= mass;
            }

            // KSP's acceleration of the last step. KSP's update of the parts may run before or after the caller in a
            // step: either record may be the one of the step before.
            Vector3 acceleration;
            bool accelerationKnown = true;
            if (Mathf.Abs(balance.accelerationTime1 - (now - dt)) < dt * 0.25f)
            {
                acceleration = balance.acceleration1;
            }
            else if (Mathf.Abs(balance.accelerationTime0 - (now - dt)) < dt * 0.25f)
            {
                acceleration = balance.acceleration0;
            }
            else
            {
                acceleration = Vector3.zero;
                accelerationKnown = false;
            }

            // The forces known to act on the vessel at the last step, and its momentum change over it when its bodies
            // were followed then.
            bool complete = count > 0 && accelerationKnown && followed && balance.bodies.Count == count;
            for (int i = 0; complete && i < count; i++)
            {
                complete = ReferenceEquals(balance.bodies[i], bodiesNow[i].body);
            }
            Vector3 knownForce = Vector3.zero;
            Vector3 knownTorque = Vector3.zero;
            Vector3 change = Vector3.zero;
            for (int i = 0; i < count; i++)
            {
                ref VesselBodies.Body b = ref bodiesNow[i];
                Applied a = AppliedAt(b.body, now - dt);
                Vector3 f = b.mass * acceleration + a.force;
                knownForce += f;
                knownTorque += Vector3.Cross(b.centre - centre, f) + a.torque;
                if (complete)
                {
                    change += b.mass * (b.velocity - balance.velocities[i]) / dt;
                }
            }
            for (int i = 0; i < known.Count; i++)
            {
                KnownForce k = known[i];
                knownForce += k.force;
                knownTorque += Vector3.Cross(k.point - centre, k.force);
            }

            // What else pushed the vessel at the last step: the momentum change that neither the known forces nor the
            // forces the solver applied then explain.
            balance.unexplainedNormal = 0f;
            if (complete && normal != Vector3.zero)
            {
                Vector3 unexplained = change - knownForce;
                for (int i = 0; i < contacts.Count; i++)
                {
                    Contact c = contacts[i];
                    if (Mathf.Abs(now - c.heldTime - dt) < dt * 0.25f)
                    {
                        unexplained -= c.heldForce;
                    }
                }
                balance.unexplainedNormal = Vector3.Dot(unexplained, normal.normalized);
            }

            if (hold && accelerationKnown && mass > 0f && touching.Count > 0)
            {
                HoldContacts(balance, touching, bodiesNow, count, centre, knownForce, knownTorque, now, dt);
            }

            // Kept for the next step.
            if (balance.bodies.Count != count)
            {
                balance.bodies.Clear();
                balance.velocities.Clear();
                for (int i = 0; i < count; i++)
                {
                    balance.bodies.Add(null);
                    balance.velocities.Add(Vector3.zero);
                }
            }
            for (int i = 0; i < count; i++)
            {
                balance.bodies[i] = bodiesNow[i].body;
                balance.velocities[i] = bodiesNow[i].velocity;
            }
            balance.statesTime = now;
        }

        private static float[] holdA = new float[8 * 8];
        private static float[] holdB = new float[8];
        private static float[] holdF = new float[8];
        private static float[] holdBefore = new float[8];
        private static Vector3[] holdArm = new Vector3[8];
        private static Vector3[] holdTurn = new Vector3[8];
        private static Vector3[] holdRow = new Vector3[8];

        /// <summary>
        /// Holds the <paramref name="touching"/> contacts within their gaps for the coming physics step. The vessel is
        /// taken as one rigid body (the first <paramref name="count"/> of <paramref name="bodies"/>, about
        /// <paramref name="centre"/>, their velocities read for this step), pushed by the <paramref name="known"/> forces
        /// and <paramref name="knownTorque"/> of the last step. Finds the least forces, none pulling, that keep every
        /// contact from closing more than its gap by the end of the coming step, all at once; applies them to the
        /// contacts' bodies at their points, and records them in
        /// <see cref="Contact.held"/>, <see cref="Balance.heldNormal"/> and <see cref="Balance.holding"/>. A contact
        /// resting at the end of its gap gets its share of the load; a contact opening, or far from closing, gets
        /// nothing; a contact closing fast is slowed to just close its gap.
        /// </summary>
        private static void HoldContacts(Balance balance, List<Contact> touching, VesselBodies.Body[] bodies, int count,
            Vector3 centre, Vector3 known, Vector3 knownTorque, float now, float dt)
        {
            int n = touching.Count;
            if (holdB.Length < n)
            {
                holdA = new float[n * n];
                holdB = new float[n];
                holdF = new float[n];
                holdBefore = new float[n];
                holdArm = new Vector3[n];
                holdTurn = new Vector3[n];
                holdRow = new Vector3[n];
            }

            // The vessel as one rigid body: mass, velocity, inertia about the centre of mass, angular velocity.
            float mass = balance.totalMass;
            Vector3 momentum = Vector3.zero;
            Vector3 angularMomentum = Vector3.zero;
            Sym3 inertia = default(Sym3);
            for (int i = 0; i < count; i++)
            {
                ref VesselBodies.Body b = ref bodies[i];
                Vector3 offset = b.centre - centre;
                Vector3 p = b.mass * b.velocity;
                momentum += p;
                angularMomentum += b.spin + Vector3.Cross(offset, p);
                inertia.AddPrincipal(b.frame, b.inertia);
                inertia.AddPoint(b.mass, offset);
            }
            Sym3 inverse;
            if (!inertia.TryInverse(out inverse))
            {
                return;
            }
            Vector3 velocity = momentum / mass;
            Vector3 spin = inverse.Times(angularMomentum);
            Vector3 acceleration = known / mass;
            Vector3 angularAcceleration = inverse.Times(knownTorque);

            // Per contact k: its opening speed at the end of the step, rhs[k] - gap/dt + dt * sum_j A[k, j] F[j], must
            // stay at least -gap/dt, the speed that just closes its gap.
            float[] a = holdA, rhs = holdB, f = holdF;
            float trace = 0f;
            for (int k = 0; k < n; k++)
            {
                Contact c = touching[k];
                // The force pushes along the axis; the gap opens at the speed along Contact.rate.
                Vector3 arm = c.point - centre;
                holdArm[k] = Vector3.Cross(arm, c.axis);
                holdTurn[k] = inverse.Times(holdArm[k]);
                holdRow[k] = Vector3.Cross(arm, c.rate);
                float gap = c.gap;
                float separating = Vector3.Dot(c.rate, velocity + Vector3.Cross(spin, arm));
                rhs[k] = separating + dt * (Vector3.Dot(c.rate, acceleration) + Vector3.Dot(holdRow[k], angularAcceleration)) +
                    gap / dt;
                // Start from the force found at the step before, when there is one.
                f[k] = Mathf.Abs(now - c.heldTime - dt) < dt * 0.25f ? c.held : 0f;
            }
            for (int k = 0; k < n; k++)
            {
                Vector3 rate = touching[k].rate;
                for (int j = 0; j < n; j++)
                {
                    a[k * n + j] = Vector3.Dot(rate, touching[j].axis) / mass +
                        Vector3.Dot(holdRow[k], holdTurn[j]);
                }
                trace += a[k * n + k];
            }
            float lambda = Regularization * trace / n;

            // Projected Gauss-Seidel, in passes: no contact pulls. The first pass pulls the forces towards zero, which
            // picks the evenest answer among the many but leaves them short by about the regularisation; each next pass
            // pulls them towards the answer of the pass before instead of zero, which keeps its sharing and makes up a
            // further part of the shortfall (short by about the regularisation to the power of the passes). Nothing is
            // kept from the step before but the starting point, so the sharing cannot drift from step to step: a wheel
            // pair across a diagonal left to carry the whole vessel makes the tyres slip.
            float[] target = holdBefore;
            for (int k = 0; k < n; k++)
            {
                target[k] = 0f;
            }
            for (int pass = 0; pass < Passes; pass++)
            {
                Sweep(n, dt, lambda);
                for (int k = 0; k < n; k++)
                {
                    target[k] = f[k];
                }
            }

            for (int k = 0; k < n; k++)
            {
                Contact c = touching[k];
                c.held = f[k];
                c.heldTime = now;
                c.heldForce = c.axis * f[k];
                c.heldPoint = c.point;
                if (f[k] > 0f && c.body != null)
                {
                    c.body.AddForceAtPosition(c.heldForce, c.heldPoint, ForceMode.Force);
                    balance.heldNormal += f[k];
                    balance.holding++;
                }
            }
        }

        /// <summary>
        /// Runs at most <see cref="Iterations"/> sweeps of projected Gauss-Seidel, until no force changes by more than
        /// <see cref="Tolerance"/>, on the forces of <see cref="HoldContacts"/>: <see cref="holdF"/> (n of them, also the
        /// starting point) such that (A + lambda) F = -rhs / dt + lambda * target, none negative, with A in
        /// <see cref="holdA"/>, rhs in <see cref="holdB"/> and the target in <see cref="holdBefore"/>.
        /// </summary>
        private static void Sweep(int n, float dt, float lambda)
        {
            float[] a = holdA, rhs = holdB, f = holdF, target = holdBefore;
            for (int iteration = 0; iteration < Iterations; iteration++)
            {
                float change = 0f;
                for (int k = 0; k < n; k++)
                {
                    float s = -rhs[k] / dt + lambda * target[k];
                    for (int j = 0; j < n; j++)
                    {
                        if (j != k)
                        {
                            s -= a[k * n + j] * f[j];
                        }
                    }
                    float value = s / (a[k * n + k] + lambda);
                    value = float.IsNaN(value) || value < 0f ? 0f : value;
                    change = Mathf.Max(change, Mathf.Abs(value - f[k]));
                    f[k] = value;
                }
                // Converged: the start from the step before usually leaves only a few sweeps to do.
                if (change <= Tolerance)
                {
                    break;
                }
            }
        }
    }
}
