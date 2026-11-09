using System;
using UnityEngine;

namespace com.github.lhervier.ksp.fix.suspensionbounce
{
    /// <summary>A symmetric 3 x 3 matrix, in doubles: the inertia tensor of a vessel.</summary>
    internal struct Sym3
    {
        public double xx, yy, zz, xy, xz, yz;

        /// <summary>Adds a body's inertia, given by its principal axes in world axes and its principal moments.</summary>
        public void AddPrincipal(Quaternion frame, Vector3 moments)
        {
            Vector3 ex, ey, ez;
            VesselBodies.Axes(frame, out ex, out ey, out ez);
            AddOuter(ex, moments.x);
            AddOuter(ey, moments.y);
            AddOuter(ez, moments.z);
        }

        /// <summary>Adds the inertia of a point mass at <paramref name="offset"/> (parallel axis theorem).</summary>
        public void AddPoint(double mass, Vector3 offset)
        {
            double d2 = offset.sqrMagnitude;
            xx += mass * (d2 - offset.x * offset.x);
            yy += mass * (d2 - offset.y * offset.y);
            zz += mass * (d2 - offset.z * offset.z);
            xy -= mass * offset.x * offset.y;
            xz -= mass * offset.x * offset.z;
            yz -= mass * offset.y * offset.z;
        }

        private void AddOuter(Vector3 axis, double scale)
        {
            xx += scale * axis.x * axis.x;
            yy += scale * axis.y * axis.y;
            zz += scale * axis.z * axis.z;
            xy += scale * axis.x * axis.y;
            xz += scale * axis.x * axis.z;
            yz += scale * axis.y * axis.z;
        }

        public Vector3 Times(Vector3 v)
        {
            return new Vector3(
                (float)(xx * v.x + xy * v.y + xz * v.z),
                (float)(xy * v.x + yy * v.y + yz * v.z),
                (float)(xz * v.x + yz * v.y + zz * v.z));
        }

        /// <summary>Returns false, with <paramref name="inverse"/> zero, when the matrix is singular.</summary>
        public bool TryInverse(out Sym3 inverse)
        {
            double cxx = yy * zz - yz * yz;
            double cxy = xz * yz - xy * zz;
            double cxz = xy * yz - xz * yy;
            double det = xx * cxx + xy * cxy + xz * cxz;
            inverse = default(Sym3);
            if (Math.Abs(det) < 1e-12 || double.IsNaN(det))
            {
                return false;
            }
            inverse.xx = cxx / det;
            inverse.xy = cxy / det;
            inverse.xz = cxz / det;
            inverse.yy = (xx * zz - xz * xz) / det;
            inverse.yz = (xy * xz - xx * yz) / det;
            inverse.zz = (xx * yy - xy * xy) / det;
            return true;
        }
    }
}
