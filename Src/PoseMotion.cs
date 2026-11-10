using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using VehiclePhysics;

namespace com.github.lhervier.ksp.fix.suspensionbounce
{
    /// <summary>
    /// Makes a tyre read how fast its contact really slides, so that a parked rover no longer creeps down a slope or
    /// turns on its own, its tyres fighting a slide that does not exist.
    /// <para>
    /// Vehicle Physics Pro reads the slide of a tyre from the wheel's Rigidbody velocity
    /// (VPWheelCollider.GetTangentVelocity). PhysX moves the bodies with the velocity of its position iterations, joint
    /// and contact corrections included, then gives back the velocity of its velocity iterations, run without those
    /// corrections: on a vessel of many parts whose joints carry a lasting load, a part's Rigidbody.velocity holds a
    /// steady velocity that moves nothing, of opposite signs on parts pushing against each other. Each tyre then pushes
    /// against a slide that is not there, and the vessel creeps.
    /// </para>
    /// <para>
    /// The fix gives the tyre the velocity of its contact as the physics really moved the wheel over the last step, read
    /// from the wheel's pose at this step and at the step before. Nothing is written back to PhysX.
    /// </para>
    /// </summary>
    public static class PoseMotion
    {
        /// <summary>Whether the fix runs at all (settings.cfg, tyreReadsPoses).</summary>
        public static bool Enabled = true;

        /// <summary>
        /// Gap (m/s) between the velocity read from the poses and Rigidbody.velocity beyond which the body is taken as
        /// moved by the game rather than by the physics (a vessel placed on the ground after it is loaded, say): the
        /// Rigidbody velocity is used for that step. The joints' corrections are millimetres per second.
        /// </summary>
        public static float TeleportGap = 0.5f;

        /// <summary>How many times a pose velocity replaced a Rigidbody velocity, for checking.</summary>
        public static long Uses;

        /// <summary>How many times a pose velocity was set aside as a move by the game (TeleportGap), for checking.</summary>
        public static long Teleports;

        /// <summary>The largest gap seen between a pose velocity kept and Rigidbody.velocity (m/s), for checking.</summary>
        public static float MaxGap;

        private class Pose
        {
            public float time = float.NegativeInfinity;
            public int shifts;
            public Vector3 origin;
            public Quaternion rotation;

            public float lastTime = float.NegativeInfinity;
            public int lastShifts;
            public Vector3 lastOrigin;
            public Quaternion lastRotation;
        }

        private static readonly ConditionalWeakTable<Rigidbody, Pose> poses = new ConditionalWeakTable<Rigidbody, Pose>();

        // Floating origin shifts seen: a pose taken before a shift is not compared with one taken after.
        private static int shifts;

        private static readonly MethodInfo GetTangentVelocity =
            AccessTools.DeclaredMethod(typeof(VPWheelCollider), "GetTangentVelocity");
        private static readonly MethodInfo CalculateLocalFrame =
            AccessTools.DeclaredMethod(typeof(VehicleBase), "CalculateLocalFrame");
        private static readonly AccessTools.FieldRef<VPWheelCollider, Rigidbody> WheelBody =
            AccessTools.FieldRefAccess<VPWheelCollider, Rigidbody>("m_rigidbody");

        /// <summary>Patches VPP's contact velocity and counts the floating origin shifts.</summary>
        public static void Install(Harmony harmony)
        {
            if (GetTangentVelocity == null || CalculateLocalFrame == null)
            {
                throw new MissingMemberException("VPWheelCollider.GetTangentVelocity or VehicleBase.CalculateLocalFrame");
            }
            harmony.Patch(CalculateLocalFrame,
                prefix: new HarmonyMethod(typeof(PoseMotion), nameof(CalculateLocalFramePrefix)));
            harmony.Patch(GetTangentVelocity,
                prefix: new HarmonyMethod(typeof(PoseMotion), nameof(GetTangentVelocityPrefix)));
            GameEvents.onFloatingOriginShift.Add((a, b) => shifts++);
        }

        /// <summary>
        /// Gives the velocity of the centre of mass of <paramref name="body"/> and its angular velocity over the last
        /// physics step, from its poses at this step and the step before, with its centre of mass now. The first call
        /// of a step takes this step's pose. Returns false, the outputs zero, when there is no pose of the step before
        /// (first call, a step missed, a floating origin shift between them, Krakensbane moving the world), or when the
        /// body was moved by the game rather than by the physics (<see cref="TeleportGap"/>).
        /// </summary>
        internal static bool Velocity(Rigidbody body, out Vector3 centre, out Vector3 velocity, out Vector3 angularVelocity)
        {
            centre = velocity = angularVelocity = Vector3.zero;
            if (body == null)
            {
                return false;
            }
            centre = body.worldCenterOfMass;
            Vector3 origin;
            if (!OriginVelocity(body, out origin, out velocity, out angularVelocity))
            {
                velocity = angularVelocity = Vector3.zero;
                return false;
            }
            // The body's origin, not its centre of mass, is what the poses follow: the game may set the centre of
            // mass of a body anew without moving it.
            velocity += Vector3.Cross(angularVelocity, centre - origin);
            float gap = (velocity - body.velocity).magnitude;
            if (gap > TeleportGap)
            {
                Teleports++;
                velocity = angularVelocity = Vector3.zero;
                return false;
            }
            if (gap > MaxGap)
            {
                MaxGap = gap;
            }
            return true;
        }

        // The velocity of the body's origin and its angular velocity over the last step, with the origin now.
        private static bool OriginVelocity(Rigidbody body, out Vector3 origin, out Vector3 velocity,
            out Vector3 angularVelocity)
        {
            velocity = angularVelocity = Vector3.zero;
            Pose pose = poses.GetOrCreateValue(body);
            float now = Time.fixedTime;
            if (pose.time != now)
            {
                pose.lastTime = pose.time;
                pose.lastShifts = pose.shifts;
                pose.lastOrigin = pose.origin;
                pose.lastRotation = pose.rotation;
                pose.time = now;
                pose.shifts = shifts;
                // The transform, which the physics wrote back at the end of the step: Rigidbody.position and
                // rotation would sync every transform of the scene first (Physics.autoSyncTransforms).
                Transform t = body.transform;
                pose.origin = t.position;
                pose.rotation = t.rotation;
            }
            origin = pose.origin;
            float dt = now - pose.lastTime;
            if (pose.lastShifts != pose.shifts || !(dt > 0f) || dt > Time.fixedDeltaTime * 1.5f ||
                !Krakensbane.GetFrameVelocity().IsZero())
            {
                return false;
            }
            velocity = (pose.origin - pose.lastOrigin) / dt;
            Quaternion turn = pose.rotation * Quaternion.Inverse(pose.lastRotation);
            if (turn.w < 0f)
            {
                turn = new Quaternion(-turn.x, -turn.y, -turn.z, -turn.w);
            }
            // Small angle: the axis times the angle is twice the vector part.
            angularVelocity = new Vector3(turn.x, turn.y, turn.z) * (2f / dt);
            return true;
        }

        /// <summary>The velocity of a point of <paramref name="body"/> over the last step, from its poses; false as for Velocity.</summary>
        internal static bool PointVelocity(Rigidbody body, Vector3 point, out Vector3 velocity)
        {
            Vector3 centre, angular;
            if (!Velocity(body, out centre, out velocity, out angular))
            {
                return false;
            }
            velocity += Vector3.Cross(angular, point - centre);
            return true;
        }

        // Takes the wheel body's pose at every step, grounded or not, so that it has one from the step before when it
        // touches down.
        private static void CalculateLocalFramePrefix(VehicleBase.WheelState wheel)
        {
            if (wheel != null && wheel.wheelCol != null)
            {
                Vector3 c, v, w;
                Velocity(WheelBody(wheel.wheelCol), out c, out v, out w);
            }
        }

        // VPWheelCollider.GetTangentVelocity with the contact's pose velocity instead of Rigidbody.GetPointVelocity.
        private static bool GetTangentVelocityPrefix(VPWheelCollider __instance, Vector3 contactPoint, Vector3 surfaceNormal,
            Rigidbody surfaceRigidbody, ref Vector3 __result)
        {
            Vector3 pointVelocity;
            if (!PointVelocity(WheelBody(__instance), contactPoint, out pointVelocity))
            {
                return true;
            }
            if (surfaceRigidbody != null)
            {
                pointVelocity -= surfaceRigidbody.GetPointVelocity(contactPoint);
            }
            __result = pointVelocity - Vector3.Project(pointVelocity, surfaceNormal);
            Uses++;
            return false;
        }
    }
}
