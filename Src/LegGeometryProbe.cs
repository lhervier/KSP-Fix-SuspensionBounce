using System.Collections.Generic;
using System.Diagnostics;
using ModuleWheels;
using UnityEngine;

namespace com.github.lhervier.ksp.fix.suspensionbounce
{
    /// <summary>
    /// Prototype probe for the fixed tuning: measures, without showing anything, where the wheel collider of every
    /// deployable leg or gear sits once deployed, and checks that measure against the legs of the vessels in flight.
    /// Writes to KSP.log only; does nothing unless probeLegGeometry = true.
    /// </summary>
    internal static class LegGeometryProbe
    {
        public static bool Enabled = false;

        /// <summary>Pose of a transform in the frame of its part.</summary>
        internal struct Pose
        {
            public Vector3 Position;
            public Quaternion Rotation;

            public override string ToString()
            {
                Vector3 down = Rotation * Vector3.down;
                return $"pos ({Position.x:F4}, {Position.y:F4}, {Position.z:F4}) down ({down.x:F3}, {down.y:F3}, {down.z:F3})";
            }
        }

        /// <summary>Deployed and retracted poses of the deploy target, by part name.</summary>
        internal static readonly Dictionary<string, Pose[]> Measured = new Dictionary<string, Pose[]>();

        /// <summary>
        /// Clones the prefab of every part with a wheel deployment, inactive so that nothing of it runs or shows,
        /// samples its deployment animation at both ends, and logs and keeps the pose of the deploy target and of the
        /// wheel collider host in the part's frame. Then destroys the clone.
        /// </summary>
        public static void MeasureAll()
        {
            Stopwatch watch = Stopwatch.StartNew();
            int count = 0;
            foreach (AvailablePart info in PartLoader.LoadedPartsList)
            {
                Part prefab = info.partPrefab;
                if (prefab == null)
                {
                    continue;
                }
                ModuleWheelBase wheelBase = prefab.FindModuleImplementing<ModuleWheelBase>();
                if (wheelBase == null)
                {
                    continue;
                }
                ModuleWheelDeployment deployment = prefab.FindModuleImplementing<ModuleWheelDeployment>();
                try
                {
                    MeasureOne(info.name, prefab, wheelBase, deployment);
                    count++;
                }
                catch (System.Exception e)
                {
                    Log.Warning($"Probe: {info.name}: {e}");
                }
            }
            Log.Info($"Probe: {count} wheel parts measured in {watch.Elapsed.TotalMilliseconds:F1} ms");
        }

        private static void MeasureOne(string name, Part prefab, ModuleWheelBase wheelBase, ModuleWheelDeployment deployment)
        {
            bool prefabActive = prefab.gameObject.activeSelf;
            if (prefabActive)
            {
                // A clone of an active prefab would run its parts' Awake: not a measure without side effects.
                Log.Warning($"Probe: {name}: prefab active, not cloned");
                return;
            }
            GameObject clone = Object.Instantiate(prefab.gameObject);
            try
            {
                clone.SetActive(false);
                Transform root = clone.transform;
                root.position = Vector3.zero;
                root.rotation = Quaternion.identity;
                root.localScale = Vector3.one;
                Transform host = Find(root, wheelBase.wheelColliderTransformName);
                WheelCollider collider = host != null ? host.GetComponent<WheelCollider>() : null;
                string colliderText = collider == null ? "no WheelCollider"
                    : $"radius {collider.radius:F3} travel {collider.suspensionDistance:F3} center {collider.center}";
                if (deployment == null)
                {
                    Log.Info($"Probe: {name}: fixed, prefab active {prefabActive}, host {PoseOf(root, host)}, {colliderText}");
                    return;
                }
                Transform target = Find(root, deployment.deployTargetTransformName);
                AnimationClip clip = null;
                Animation animation = null;
                foreach (Animation candidate in clone.GetComponentsInChildren<Animation>(true))
                {
                    AnimationClip found = candidate.GetClip(deployment.animationStateName);
                    if (found != null)
                    {
                        animation = candidate;
                        clip = found;
                        break;
                    }
                }
                if (clip == null || target == null)
                {
                    Log.Warning($"Probe: {name}: clip {deployment.animationStateName} {(clip != null)}, target " +
                        $"{deployment.deployTargetTransformName} {(target != null)}");
                    return;
                }
                // deployedPosition is 0 or 1, the end of the clip where the leg is out.
                float deployedTime = deployment.deployedPosition * clip.length;
                float retractedTime = (1f - deployment.deployedPosition) * clip.length;
                clip.SampleAnimation(animation.gameObject, retractedTime);
                Pose retractedTarget = PoseOf(root, target);
                Pose retractedHost = PoseOf(root, host);
                clip.SampleAnimation(animation.gameObject, deployedTime);
                Pose deployedTarget = PoseOf(root, target);
                Pose deployedHost = PoseOf(root, host);
                Measured[name] = new[] { deployedTarget, retractedTarget };
                Log.Info($"Probe: {name}: clip {clip.name} {clip.length:F2} s deployedPosition {deployment.deployedPosition}, " +
                    $"prefab active {prefabActive}, {colliderText}\n" +
                    $"  target deployed  {deployedTarget}\n  target retracted {retractedTarget}\n" +
                    $"  host deployed    {deployedHost}\n  host retracted   {retractedHost}");
            }
            finally
            {
                Object.DestroyImmediate(clone);
            }
        }

        /// <summary>
        /// Logs, for every deployable wheel of the active vessel, its deployment state and the pose of its wheel
        /// collider host in the part's frame, beside the deployed pose measured on the prefab.
        /// </summary>
        public static void CheckActiveVessel()
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null)
            {
                return;
            }
            foreach (Part part in vessel.parts)
            {
                ModuleWheelDeployment deployment = part.FindModuleImplementing<ModuleWheelDeployment>();
                ModuleWheelBase wheelBase = part.FindModuleImplementing<ModuleWheelBase>();
                if (deployment == null || wheelBase == null || wheelBase.wheelColliderHost == null)
                {
                    continue;
                }
                Pose live = PoseOf(part.transform, wheelBase.wheelColliderHost);
                Pose[] measured;
                string expected = Measured.TryGetValue(part.partInfo.name, out measured)
                    ? $"measured deployed {measured[0]}, retracted {measured[1]}" : "not measured";
                Log.Info($"Probe check: {part.partInfo.name} #{part.persistentId} state {deployment.stateString} " +
                    $"position {deployment.position:F2} rescale {part.rescaleFactor:F3}: host {live}; {expected}");
            }
        }

        private static Pose PoseOf(Transform root, Transform t)
        {
            if (t == null)
            {
                return new Pose { Position = new Vector3(float.NaN, float.NaN, float.NaN), Rotation = Quaternion.identity };
            }
            return new Pose
            {
                Position = root.InverseTransformPoint(t.position),
                Rotation = Quaternion.Inverse(root.rotation) * t.rotation,
            };
        }

        private static Transform Find(Transform root, string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name)
                {
                    return t;
                }
            }
            return null;
        }
    }

    /// <summary>Runs the probe's measure once, at the main menu, when every part is loaded.</summary>
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class LegGeometryProbeMenu : MonoBehaviour
    {
        private void Start()
        {
            if (LegGeometryProbe.Enabled || FixedTuning.Enabled)
            {
                LegGeometryProbe.MeasureAll();
            }
        }
    }

    /// <summary>Runs the probe's check on the active vessel every 5 s of flight.</summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class LegGeometryProbeFlight : MonoBehaviour
    {
        private float next;

        private void Update()
        {
            if (!LegGeometryProbe.Enabled || Time.time < next)
            {
                return;
            }
            next = Time.time + 5f;
            LegGeometryProbe.CheckActiveVessel();
        }
    }
}
