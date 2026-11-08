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
            for (int i = 0; i < count; i++)
            {
                ref Body b = ref bodies[i];
                b.velocity = b.body.velocity;
                b.spin = Spin(b.frame, b.inertia, b.body.angularVelocity);
            }
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
