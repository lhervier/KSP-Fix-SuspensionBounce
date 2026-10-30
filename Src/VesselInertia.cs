using System.Collections.Generic;
using UnityEngine;

namespace com.github.lhervier.ksp.fix.suspensionbounce
{
    /// <summary>
    /// The mass, centre of mass and inertia of a vessel taken as one rigid body, from the rigidbodies of its parts.
    /// </summary>
    internal sealed class VesselInertia
    {
        /// <summary>The inertia of each vessel, computed for the physics step <see cref="step"/>.</summary>
        private static readonly Dictionary<Vessel, VesselInertia> inertias = new Dictionary<Vessel, VesselInertia>();
        private static float step = -1f;

        private double mass;
        private Vector3d centerOfMass;

        // The inverse of the inertia tensor about the centre of mass, in world axes, symmetric: xx, yy, zz, xy, xz, yz.
        // Null when the tensor cannot be inverted: the vessel then only resists being pushed, not being turned.
        private double[] inverse;

        /// <summary>
        /// Returns the inertia of <paramref name="vessel"/> for the current physics step, or null when none of its parts
        /// has a rigidbody.
        /// </summary>
        public static VesselInertia Of(Vessel vessel)
        {
            // Every suspension of a vessel asks at each step: compute it once per vessel and step.
            if (Time.fixedTime != step)
            {
                inertias.Clear();
                step = Time.fixedTime;
            }
            VesselInertia inertia;
            if (!inertias.TryGetValue(vessel, out inertia))
            {
                inertia = Compute(vessel);
                inertias[vessel] = inertia;
            }
            return inertia;
        }

        /// <summary>
        /// Returns the mass a force pushing at <paramref name="point"/> along <paramref name="direction"/> (a unit
        /// vector) sets in motion at that point: the whole mass for a push through the centre of mass, less the farther
        /// off its line the push is, as the vessel then turns as much as it moves.
        /// </summary>
        public double EffectiveMass(Vector3d point, Vector3d direction)
        {
            // A push F at r from the centre of mass moves that point by F / M along the push, and turns the vessel by
            // I^-1 (r x F), which moves the point by (I^-1 (r x F)) x r: along the push, (r x n) . I^-1 (r x n) per unit
            // of force. The point therefore answers as a mass m with 1 / m = 1 / M + (r x n) . I^-1 (r x n).
            double inverseMass = 1.0 / mass;
            if (inverse != null)
            {
                Vector3d a = Vector3d.Cross(point - centerOfMass, direction);
                inverseMass += a.x * (inverse[0] * a.x + inverse[3] * a.y + inverse[4] * a.z)
                    + a.y * (inverse[3] * a.x + inverse[1] * a.y + inverse[5] * a.z)
                    + a.z * (inverse[4] * a.x + inverse[5] * a.y + inverse[2] * a.z);
            }
            return 1.0 / inverseMass;
        }

        private static VesselInertia Compute(Vessel vessel)
        {
            // The parts without physics have no rigidbody: the game adds their mass to their parent's.
            List<Part> parts = vessel.parts;
            double mass = 0.0;
            Vector3d moment = Vector3d.zero;
            for (int i = 0; i < parts.Count; i++)
            {
                Rigidbody rb = parts[i].rb;
                if (rb != null)
                {
                    mass += rb.mass;
                    moment += (Vector3d)rb.worldCenterOfMass * rb.mass;
                }
            }
            if (mass <= 0.0)
            {
                return null;
            }
            Vector3d center = moment / mass;

            // Each part adds its own inertia, turned to world axes, and that of its mass about the vessel's centre of
            // mass (parallel axis theorem).
            double xx = 0, yy = 0, zz = 0, xy = 0, xz = 0, yz = 0;
            for (int i = 0; i < parts.Count; i++)
            {
                Rigidbody rb = parts[i].rb;
                if (rb == null)
                {
                    continue;
                }
                Quaternion axes = rb.rotation * rb.inertiaTensorRotation;
                Vector3 diagonal = rb.inertiaTensor;
                Vector3 ex = axes * Vector3.right, ey = axes * Vector3.up, ez = axes * Vector3.forward;
                xx += diagonal.x * ex.x * ex.x + diagonal.y * ey.x * ey.x + diagonal.z * ez.x * ez.x;
                yy += diagonal.x * ex.y * ex.y + diagonal.y * ey.y * ey.y + diagonal.z * ez.y * ez.y;
                zz += diagonal.x * ex.z * ex.z + diagonal.y * ey.z * ey.z + diagonal.z * ez.z * ez.z;
                xy += diagonal.x * ex.x * ex.y + diagonal.y * ey.x * ey.y + diagonal.z * ez.x * ez.y;
                xz += diagonal.x * ex.x * ex.z + diagonal.y * ey.x * ey.z + diagonal.z * ez.x * ez.z;
                yz += diagonal.x * ex.y * ex.z + diagonal.y * ey.y * ey.z + diagonal.z * ez.y * ez.z;

                Vector3d r = (Vector3d)rb.worldCenterOfMass - center;
                double m = rb.mass;
                xx += m * (r.y * r.y + r.z * r.z);
                yy += m * (r.x * r.x + r.z * r.z);
                zz += m * (r.x * r.x + r.y * r.y);
                xy -= m * r.x * r.y;
                xz -= m * r.x * r.z;
                yz -= m * r.y * r.z;
            }

            return new VesselInertia
            {
                mass = mass,
                centerOfMass = center,
                inverse = Invert(xx, yy, zz, xy, xz, yz),
            };
        }

        /// <summary>
        /// Returns the inverse of a symmetric 3 x 3 matrix as xx, yy, zz, xy, xz, yz, or null when it has none.
        /// </summary>
        private static double[] Invert(double xx, double yy, double zz, double xy, double xz, double yz)
        {
            // Cofactors over the determinant.
            double cxx = yy * zz - yz * yz;
            double cyy = xx * zz - xz * xz;
            double czz = xx * yy - xy * xy;
            double cxy = xz * yz - xy * zz;
            double cxz = xy * yz - xz * yy;
            double cyz = xy * xz - xx * yz;
            double determinant = xx * cxx + xy * cxy + xz * cxz;
            if (!(determinant > 1e-12))
            {
                return null;
            }
            return new[]
            {
                cxx / determinant, cyy / determinant, czz / determinant,
                cxy / determinant, cxz / determinant, cyz / determinant,
            };
        }
    }
}
