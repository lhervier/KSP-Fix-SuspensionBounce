using System;
using System.Collections.Generic;
using UnityEngine;

namespace com.github.lhervier.ksp.fix.suspensionbounce
{
    /// <summary>
    /// The rigidbodies of a vessel, read from Unity once per vessel and physics step for every fix that takes the
    /// vessel as one rigid body: the dampers (<see cref="VesselInertia"/>) and the end-stops (<see cref="BumpStopLoad"/>).
    /// <para>
    /// A body's mass, its principal moments of inertia and their axes in the body only change with its resources and
    /// the vessel's structure: they are kept from step to step, and read again at once when the body's mass is no
    /// longer the one KSP gives it from its part's fields, or when the body is not the one read before at its place in
    /// the vessel, and in any case every <see cref="RefreshSteps"/> steps. A body new or whose mass changed is read once
    /// more at the next step: Unity scales its inertia to a new mass only once the physics has run. The centre of mass
    /// and the rotation are read at every step, the velocities only at the steps a fix asks for them
    /// (<see cref="ReadMotion"/>).
    /// </para>
    /// </summary>
    public sealed class VesselBodies
    {
        /// <summary>How many physics steps a body's mass and inertia are kept before being read again.</summary>
        public static int RefreshSteps = 50;

        /// <summary>One rigidbody of the vessel.</summary>
        internal struct Body
        {
            public Rigidbody body;
            public Transform transform;

            // Kept from step to step: read at readTime.
            public float mass;
            /// <summary>The principal moments of inertia.</summary>
            public Vector3 inertia;
            /// <summary>The principal axes of inertia, in the body's axes.</summary>
            public Quaternion inertiaRotation;
            public float readTime;
            /// <summary>Whether to read them again at the next step.</summary>
            public bool again;

            // Read at every step.
            public Vector3 centre;
            /// <summary>The principal axes of inertia, in world axes.</summary>
            public Quaternion frame;

            // Read by ReadMotion only.
            public Vector3 velocity;
            /// <summary>The angular momentum about the body's centre of mass, in world axes.</summary>
            public Vector3 spin;

            // EXPERIMENT: read by ReadPoseMotion only (PoseMotion.AnchorReadsPositions).
            public Vector3 poseVelocity;
            public Vector3 poseSpin;
        }

        private static readonly Dictionary<Vessel, VesselBodies> vessels = new Dictionary<Vessel, VesselBodies>();

        /// <summary>The bodies, in the order of the vessel's parts, each part's own body before its servo body.</summary>
        internal Body[] bodies = new Body[16];
        internal int count;

        // The step and the floating origin of the last read, and which read the inertia was computed from.
        private float time = -1f;
        private Vector3d origin;
        private int reads;
        private int inertiaRead = -1;
        private VesselInertia inertia;
        private int motionRead = -1;
        private int poseMotionRead = -1;

        // The vessel as one rigid body, computed at the read rigidRead.
        private int rigidRead = -1;
        private bool rigidValid;
        private Vector3 rigidCentre, rigidVelocity, rigidAngularVelocity;

        // The bodies of the wheels on the ground: those marked at the step groundedTime, and those of the step before.
        private HashSet<Rigidbody> groundedNow = new HashSet<Rigidbody>();
        private HashSet<Rigidbody> groundedLast = new HashSet<Rigidbody>();
        private float groundedTime = -1f;

        // The bodies of groundedLast as one rigid body, computed at the read groundRead.
        private int groundRead = -1;
        private bool groundValid;
        private Vector3 groundCentre, groundVelocity, groundAngularVelocity;

        /// <summary>Forgets the vessels of a scene that is gone, and those destroyed.</summary>
        internal static void Install()
        {
            GameEvents.onGameSceneLoadRequested.Add(scene => vessels.Clear());
            GameEvents.onVesselDestroy.Add(vessel => vessels.Remove(vessel));
        }

        /// <summary>
        /// Returns the bodies of <paramref name="vessel"/> with their centre of mass and rotation for the current
        /// physics step, read at the first call of the step (or again when the floating origin moved since).
        /// </summary>
        internal static VesselBodies Of(Vessel vessel)
        {
            VesselBodies found;
            if (!vessels.TryGetValue(vessel, out found))
            {
                found = new VesselBodies();
                vessels[vessel] = found;
            }
            found.Read(vessel);
            return found;
        }

        /// <summary>
        /// Returns the vessel's inertia as one rigid body for the current step, from all its bodies; null when it has
        /// none. Call on what <see cref="Of"/> returned in the same step.
        /// </summary>
        internal VesselInertia Inertia()
        {
            if (inertiaRead != reads)
            {
                inertia = VesselInertia.Compute(this);
                inertiaRead = reads;
            }
            return inertia;
        }

        /// <summary>
        /// Reads the velocity and the angular momentum of every body now. Call on what <see cref="Of"/> returned in the
        /// same step.
        /// </summary>
        internal void ReadMotion()
        {
            if (motionRead == reads)
            {
                return;
            }
            motionRead = reads;
            for (int i = 0; i < count; i++)
            {
                ref Body b = ref bodies[i];
                b.velocity = b.body.velocity;
                b.spin = Spin(b.frame, b.inertia, b.body.angularVelocity);
            }
        }

        /// <summary>
        /// EXPERIMENT: reads the velocity and the angular momentum of every body from its poses (PoseMotion), its
        /// Rigidbody velocities where it has no pose of the step before. Call on what <see cref="Of"/> returned in the
        /// same step.
        /// </summary>
        internal void ReadPoseMotion()
        {
            if (poseMotionRead == reads)
            {
                return;
            }
            poseMotionRead = reads;
            for (int i = 0; i < count; i++)
            {
                ref Body b = ref bodies[i];
                Vector3 c, v, w;
                if (!PoseMotion.Velocity(b.body, out c, out v, out w))
                {
                    v = b.body.velocity;
                    w = b.body.angularVelocity;
                }
                else
                {
                    PoseMotion.Uses++;
                }
                b.poseVelocity = v;
                b.poseSpin = Spin(b.frame, b.inertia, w);
            }
        }

        /// <summary>
        /// Gives the motion of the vessel taken as one rigid body at the current step, from the velocities of all its
        /// bodies: its centre of mass, the velocity of that centre, and the angular velocity that carries its angular
        /// momentum about it, in world axes. Returns false, the outputs zero, when the vessel has no mass or its inertia
        /// cannot be inverted. Call on what <see cref="Of"/> returned in the same step.
        /// </summary>
        internal bool RigidMotion(out Vector3 centre, out Vector3 velocity, out Vector3 angularVelocity)
        {
            if (rigidRead != reads)
            {
                rigidRead = reads;
                ComputeRigidMotion();
            }
            centre = rigidCentre;
            velocity = rigidVelocity;
            angularVelocity = rigidAngularVelocity;
            return rigidValid;
        }

        private void ComputeRigidMotion()
        {
            rigidValid = Rigid(null, out rigidCentre, out rigidVelocity, out rigidAngularVelocity);
        }

        /// <summary>
        /// Notes that the body of a wheel touches the ground at the current physics step, for
        /// <see cref="GroundedRigidMotion"/>. Call on what <see cref="Of"/> returned in the same step.
        /// </summary>
        internal void MarkGrounded(Rigidbody body)
        {
            TurnGroundedStep();
            groundedNow.Add(body);
        }

        /// <summary>
        /// Gives the motion, taken as one rigid body, of the bodies of the vessel's wheels that touched the ground at the
        /// step before (<see cref="MarkGrounded"/>), as <see cref="RigidMotion"/> gives the vessel's: the parts that
        /// rest on the ground, without what they carry above and that may swing, such as a load hanging from a crane.
        /// Returns false, the outputs zero, when no wheel was marked at the step before, or their inertia cannot be
        /// inverted. Call on what <see cref="Of"/> returned in the same step.
        /// </summary>
        internal bool GroundedRigidMotion(out Vector3 centre, out Vector3 velocity, out Vector3 angularVelocity)
        {
            TurnGroundedStep();
            if (groundRead != reads)
            {
                groundRead = reads;
                groundValid = groundedLast.Count > 0 &&
                    Rigid(groundedLast, out groundCentre, out groundVelocity, out groundAngularVelocity);
            }
            centre = groundCentre;
            velocity = groundVelocity;
            angularVelocity = groundAngularVelocity;
            return groundValid;
        }

        // The marks of a step become those of the step before at the first mark or request of the next step.
        private void TurnGroundedStep()
        {
            float now = Time.fixedTime;
            if (groundedTime == now)
            {
                return;
            }
            HashSet<Rigidbody> last = groundedLast;
            groundedLast = groundedNow;
            groundedNow = last;
            groundedNow.Clear();
            // A step skipped (the vessel on rails, or not loaded) leaves no wheel on the ground from the step before.
            if (now - groundedTime > Time.fixedDeltaTime * 1.5f)
            {
                groundedLast.Clear();
            }
            groundedTime = now;
            groundRead = -1;
        }

        /// <summary>
        /// Gives the motion of the bodies of the vessel taken as one rigid body, or of those in <paramref name="only"/>
        /// when it is not null: their centre of mass, its velocity, and the angular velocity that carries their angular
        /// momentum about it. Returns false, the outputs zero, when they have no mass or their inertia cannot be
        /// inverted.
        /// </summary>
        private bool Rigid(HashSet<Rigidbody> only, out Vector3 centre, out Vector3 velocity, out Vector3 angularVelocity)
        {
            centre = velocity = angularVelocity = Vector3.zero;
            bool fromPoses = PoseMotion.AnchorReadsPositions;
            if (fromPoses)
            {
                ReadPoseMotion();
            }
            else
            {
                ReadMotion();
            }
            float mass = 0f;
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < count; i++)
            {
                if (only == null || only.Contains(bodies[i].body))
                {
                    mass += bodies[i].mass;
                    sum += bodies[i].mass * bodies[i].centre;
                }
            }
            if (!(mass > 0f))
            {
                return false;
            }
            sum /= mass;
            Vector3 momentum = Vector3.zero;
            Vector3 angularMomentum = Vector3.zero;
            Sym3 inertiaTensor = default(Sym3);
            for (int i = 0; i < count; i++)
            {
                ref Body b = ref bodies[i];
                if (only != null && !only.Contains(b.body))
                {
                    continue;
                }
                Vector3 offset = b.centre - sum;
                Vector3 p = b.mass * (fromPoses ? b.poseVelocity : b.velocity);
                momentum += p;
                angularMomentum += (fromPoses ? b.poseSpin : b.spin) + Vector3.Cross(offset, p);
                inertiaTensor.AddPrincipal(b.frame, b.inertia);
                inertiaTensor.AddPoint(b.mass, offset);
            }
            Sym3 inverse;
            if (!inertiaTensor.TryInverse(out inverse))
            {
                return false;
            }
            centre = sum;
            velocity = momentum / mass;
            angularVelocity = inverse.Times(angularMomentum);
            return true;
        }

        private void Read(Vessel vessel)
        {
            float now = Time.fixedTime;
            // Within a step the bodies do not move, but for a shift of the floating origin, which moves them all.
            Vector3d shift = FloatingOrigin.fetch != null ? FloatingOrigin.fetch.offsetNonKrakensbane : Vector3d.zero;
            if (time == now && origin.x == shift.x && origin.y == shift.y && origin.z == shift.z)
            {
                return;
            }
            time = now;
            origin = shift;
            reads++;
            float keepFor = (RefreshSteps - 0.25f) * Time.fixedDeltaTime;

            int n = 0;
            List<Part> parts = vessel.parts;
            for (int i = 0; i < parts.Count; i++)
            {
                Part part = parts[i];
                if (part == null)
                {
                    continue;
                }
                // The mass FlightIntegrator.UpdateMassStats sets on the part's bodies at each step, from fields of the
                // part, cheaper to read than the bodies': a kept mass that is no longer this one is read again with the
                // inertia, which Unity scales with it. Fuel that burns is seen at once, and so is the first step after
                // a vessel is unpacked, whose bodies still have the mass of the part's prefab.
                float expected = part.partInfo != null
                    ? Mathf.Max(part.partInfo.MinimumRBMass, (float)part.physicsMass) : float.NaN;
                if (part.servoRb != null)
                {
                    expected /= 2f;
                }
                // PhysX keeps the inverse of the mass, in a float, and gives back the inverse of that: the casts round
                // each step to a float, which Mono would otherwise carry in double precision.
                float inverse = (float)(1f / expected);
                expected = (float)(1f / inverse);
                for (int k = 0; k < 2; k++)
                {
                    Rigidbody body = k == 0 ? part.rb : part.servoRb;
                    if (body == null)
                    {
                        continue;
                    }
                    if (n == bodies.Length)
                    {
                        Array.Resize(ref bodies, n * 2);
                    }
                    ref Body b = ref bodies[n++];
                    // The centre first: the first read of a pose in a step brings the bodies up to date, the inertia
                    // that Unity scales to a new mass included.
                    b.centre = body.worldCenterOfMass;
                    bool changed = !ReferenceEquals(b.body, body) || b.mass != expected;
                    if (changed || b.again || now - b.readTime >= keepFor || now < b.readTime)
                    {
                        b.body = body;
                        b.transform = body.transform;
                        b.mass = body.mass;
                        b.inertia = body.inertiaTensor;
                        b.inertiaRotation = body.inertiaTensorRotation;
                        b.readTime = now;
                        // Unity scales the inertia of a body to a new mass only once the physics has run: a body new
                        // here, or whose mass has just changed, is read again at the next step.
                        b.again = changed;
                    }
                    // The transform's rotation, which the physics wrote back at the end of the step: Rigidbody.rotation
                    // would sync every transform of the scene first (Physics.autoSyncTransforms), at a high cost.
                    b.frame = b.transform.rotation * b.inertiaRotation;
                }
            }
            // The places left over hold no body: one there later is read in full.
            for (int i = n; i < count; i++)
            {
                bodies[i] = default(Body);
            }
            count = n;
        }

        /// <summary>
        /// Gives the images of the x, y and z unit vectors turned by <paramref name="q"/>, the columns of its rotation
        /// matrix: bit for bit what <c>q * Vector3.right</c>, <c>q * Vector3.up</c> and <c>q * Vector3.forward</c> give.
        /// </summary>
        internal static void Axes(Quaternion q, out Vector3 ex, out Vector3 ey, out Vector3 ez)
        {
            // Unity's Quaternion * Vector3, without its three calls and its products by 0 and 1.
            float x = q.x * 2f, y = q.y * 2f, z = q.z * 2f;
            float xx = q.x * x, yy = q.y * y, zz = q.z * z;
            float xy = q.x * y, xz = q.x * z, yz = q.y * z;
            float wx = q.w * x, wy = q.w * y, wz = q.w * z;
            ex = new Vector3(1f - (yy + zz), xy + wz, xz - wy);
            ey = new Vector3(xy - wz, 1f - (xx + zz), yz + wx);
            ez = new Vector3(xz + wy, yz - wx, 1f - (xx + yy));
        }

        /// <summary>
        /// The angular momentum of a body about its centre of mass, in world axes, from its principal axes
        /// <paramref name="frame"/>, its principal moments <paramref name="inertia"/> and its angular velocity.
        /// </summary>
        private static Vector3 Spin(Quaternion frame, Vector3 inertia, Vector3 angularVelocity)
        {
            Vector3 local = Quaternion.Inverse(frame) * angularVelocity;
            return frame * Vector3.Scale(inertia, local);
        }
    }
}
