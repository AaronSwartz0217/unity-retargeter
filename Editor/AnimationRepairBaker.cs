using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Codex.SplitRigRetargeter
{
    internal sealed class RepairSettings
    {
        public GameObject sourceRoot;
        public AnimationClip sourceClip;
        public Transform motionRoot;
        public float sampleRate;
        public bool skipInvalidFirstFrame;
        public bool fixInitialFacingFlip;
        public bool alignInitialFacingToRoot;
        public bool zeroInitialHorizontalPosition;
        public bool zeroInitialLocalRootXZ;
        public float suddenTurnThreshold;
        public Vector3 rootRotationOffset;
        public Vector3 rootMotionRotation;
        public Vector3 rootMotionScale;
        public Vector3 rootPositionOffset;
        public string outputPath;
    }

    internal static class AnimationRepairBaker
    {
        private sealed class TransformCurves
        {
            public readonly AnimationCurve px = new AnimationCurve();
            public readonly AnimationCurve py = new AnimationCurve();
            public readonly AnimationCurve pz = new AnimationCurve();
            public readonly AnimationCurve rx = new AnimationCurve();
            public readonly AnimationCurve ry = new AnimationCurve();
            public readonly AnimationCurve rz = new AnimationCurve();
            public readonly AnimationCurve rw = new AnimationCurve();
            public readonly AnimationCurve sx = new AnimationCurve();
            public readonly AnimationCurve sy = new AnimationCurve();
            public readonly AnimationCurve sz = new AnimationCurve();
            public Quaternion previousRotation;
            public bool hasPreviousRotation;
        }

        private sealed class LocalPose
        {
            public Transform transform;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
        }

        public static List<string> Validate(RepairSettings settings)
        {
            var errors = new List<string>();
            if (settings.sourceRoot == null)
                errors.Add("Repair Source Root is required.");
            if (settings.sourceClip == null)
                errors.Add("Repair Source Clip is required.");
            if (settings.sampleRate <= 0f)
                errors.Add("Repair Sample Rate must be greater than zero.");
            if (settings.fixInitialFacingFlip &&
                (settings.suddenTurnThreshold <= 0f ||
                 settings.suddenTurnThreshold > 180f))
                errors.Add("Repair turn threshold must be between 0 and 180.");
            if (string.IsNullOrWhiteSpace(settings.outputPath) ||
                !settings.outputPath.StartsWith("Assets/", StringComparison.Ordinal))
                errors.Add("Repair output must be inside Assets/.");
            return errors;
        }

        public static AnimationClip Bake(
            RepairSettings settings,
            out List<string> warnings)
        {
            var errors = Validate(settings);
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join("\n", errors));

            warnings = new List<string>();
            var originalPose = CaptureLocalPose(settings.sourceRoot.transform);
            var desiredForward = Vector3.ProjectOnPlane(
                settings.sourceRoot.transform.forward,
                Vector3.up);
            try
            {
                AnimationMode.StartAnimationMode();
                var animated = ResolveAnimatedTransforms(
                    settings.sourceRoot.transform,
                    settings.sourceClip,
                    warnings);
                if (animated.Count == 0)
                    throw new InvalidOperationException(
                        "No animated Transform paths were resolved.");

                var motionRoot = settings.motionRoot != null
                    ? settings.motionRoot
                    : FindMotionRoot(animated);
                if (motionRoot == null)
                    motionRoot = settings.sourceRoot.transform;

                var startOffset = settings.skipInvalidFirstFrame
                    ? 1f / settings.sampleRate
                    : 0f;
                Sample(settings, startOffset);
                var referencePosition = motionRoot.position;
                var referenceLocalPosition = motionRoot.localPosition;
                var initialFacingCorrection =
                    settings.alignInitialFacingToRoot
                        ? HorizontalAlignment(
                            motionRoot.forward,
                            desiredForward)
                        : Quaternion.identity;
                var outputReferencePosition =
                    settings.zeroInitialHorizontalPosition
                        ? new Vector3(
                            settings.sourceRoot.transform.position.x,
                            referencePosition.y,
                            settings.sourceRoot.transform.position.z)
                        : referencePosition;
                var heading = new HeadingRepair(
                    motionRoot,
                    settings.fixInitialFacingFlip,
                    settings.suddenTurnThreshold);
                var curves = new Dictionary<Transform, TransformCurves>();
                foreach (var transform in animated)
                    curves.Add(transform, new TransformCurves());

                var duration = Mathf.Max(
                    0f,
                    settings.sourceClip.length - startOffset);
                var sampleCount = Mathf.Max(
                    1,
                    Mathf.CeilToInt(duration * settings.sampleRate));

                for (var frame = 0; frame <= sampleCount; frame++)
                {
                    var outputTime = Mathf.Min(
                        frame / settings.sampleRate,
                        duration);
                    var sourceTime = Mathf.Min(
                        outputTime + startOffset,
                        settings.sourceClip.length);
                    Sample(settings, sourceTime);

                    var headingCorrection = heading.Update(outputTime);
                    var poseCorrection =
                        Quaternion.Euler(settings.rootRotationOffset);
                    motionRoot.rotation =
                        poseCorrection *
                        initialFacingCorrection *
                        headingCorrection *
                        motionRoot.rotation;

                    var delta = motionRoot.position - referencePosition;
                    delta = headingCorrection * delta;
                    delta = initialFacingCorrection * delta;
                    delta = Quaternion.Euler(
                        settings.rootMotionRotation) * delta;
                    delta = Vector3.Scale(delta, settings.rootMotionScale);
                    motionRoot.position =
                        outputReferencePosition +
                        delta +
                        settings.rootPositionOffset;
                    if (settings.zeroInitialLocalRootXZ)
                    {
                        var localPosition = motionRoot.localPosition;
                        localPosition.x -= referenceLocalPosition.x;
                        localPosition.z -= referenceLocalPosition.z;
                        motionRoot.localPosition = localPosition;
                    }

                    AddKeys(animated, curves, outputTime);
                }

                var clip = new AnimationClip
                {
                    name = Path.GetFileNameWithoutExtension(settings.outputPath),
                    frameRate = settings.sampleRate
                };
                WriteCurves(
                    clip,
                    settings.sourceRoot.transform,
                    animated,
                    curves);
                clip.EnsureQuaternionContinuity();
                SaveClip(clip, settings.outputPath);
                return AssetDatabase.LoadAssetAtPath<AnimationClip>(
                    settings.outputPath);
            }
            finally
            {
                if (AnimationMode.InAnimationMode())
                    AnimationMode.StopAnimationMode();
                RestoreLocalPose(originalPose);
            }
        }

        private static List<Transform> ResolveAnimatedTransforms(
            Transform root,
            AnimationClip clip,
            List<string> warnings)
        {
            var result = new List<Transform>();
            var seen = new HashSet<Transform>();
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.type != typeof(Transform))
                    continue;
                var transform = string.IsNullOrEmpty(binding.path)
                    ? root
                    : root.Find(binding.path);
                if (transform == null)
                {
                    warnings.Add("Unresolved path: " + binding.path);
                    continue;
                }
                if (seen.Add(transform))
                    result.Add(transform);
            }
            return result;
        }

        private static Transform FindMotionRoot(List<Transform> transforms)
        {
            foreach (var transform in transforms)
            {
                var name = RetargetingProfile.Normalize(transform.name);
                if (string.Equals(
                        name,
                        "Hips",
                        StringComparison.OrdinalIgnoreCase))
                    return transform;
            }
            return transforms.Count > 0 ? transforms[0] : null;
        }

        private static void Sample(RepairSettings settings, float time)
        {
            AnimationMode.SampleAnimationClip(
                settings.sourceRoot,
                settings.sourceClip,
                Mathf.Clamp(time, 0f, settings.sourceClip.length));
        }

        private static Quaternion HorizontalAlignment(
            Vector3 currentForward,
            Vector3 desiredForward)
        {
            currentForward = Vector3.ProjectOnPlane(
                currentForward,
                Vector3.up);
            desiredForward = Vector3.ProjectOnPlane(
                desiredForward,
                Vector3.up);
            if (currentForward.sqrMagnitude < 0.000001f ||
                desiredForward.sqrMagnitude < 0.000001f)
                return Quaternion.identity;
            var angle = Vector3.SignedAngle(
                currentForward,
                desiredForward,
                Vector3.up);
            return Quaternion.AngleAxis(angle, Vector3.up);
        }

        private static void AddKeys(
            List<Transform> transforms,
            Dictionary<Transform, TransformCurves> curves,
            float time)
        {
            foreach (var transform in transforms)
            {
                var set = curves[transform];
                var p = transform.localPosition;
                var r = transform.localRotation;
                var s = transform.localScale;
                if (set.hasPreviousRotation &&
                    Quaternion.Dot(set.previousRotation, r) < 0f)
                {
                    r = new Quaternion(-r.x, -r.y, -r.z, -r.w);
                }
                set.previousRotation = r;
                set.hasPreviousRotation = true;

                set.px.AddKey(time, p.x);
                set.py.AddKey(time, p.y);
                set.pz.AddKey(time, p.z);
                set.rx.AddKey(time, r.x);
                set.ry.AddKey(time, r.y);
                set.rz.AddKey(time, r.z);
                set.rw.AddKey(time, r.w);
                set.sx.AddKey(time, s.x);
                set.sy.AddKey(time, s.y);
                set.sz.AddKey(time, s.z);
            }
        }

        private static void WriteCurves(
            AnimationClip clip,
            Transform root,
            List<Transform> transforms,
            Dictionary<Transform, TransformCurves> curves)
        {
            foreach (var transform in transforms)
            {
                var path = AnimationUtility.CalculateTransformPath(
                    transform,
                    root);
                var set = curves[transform];
                SetCurve(clip, path, "m_LocalPosition.x", set.px);
                SetCurve(clip, path, "m_LocalPosition.y", set.py);
                SetCurve(clip, path, "m_LocalPosition.z", set.pz);
                SetCurve(clip, path, "m_LocalRotation.x", set.rx);
                SetCurve(clip, path, "m_LocalRotation.y", set.ry);
                SetCurve(clip, path, "m_LocalRotation.z", set.rz);
                SetCurve(clip, path, "m_LocalRotation.w", set.rw);
                SetCurve(clip, path, "m_LocalScale.x", set.sx);
                SetCurve(clip, path, "m_LocalScale.y", set.sy);
                SetCurve(clip, path, "m_LocalScale.z", set.sz);
            }
        }

        private static void SetCurve(
            AnimationClip clip,
            string path,
            string property,
            AnimationCurve curve)
        {
            AnimationUtility.SetEditorCurve(
                clip,
                EditorCurveBinding.FloatCurve(
                    path,
                    typeof(Transform),
                    property),
                curve);
        }

        private static void SaveClip(AnimationClip clip, string path)
        {
            var directory = Path.GetDirectoryName(path)?.Replace("\\", "/");
            if (!string.IsNullOrEmpty(directory))
                CreateFolders(directory);
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing == null)
                AssetDatabase.CreateAsset(clip, path);
            else
            {
                EditorUtility.CopySerialized(clip, existing);
                UnityEngine.Object.DestroyImmediate(clip);
                EditorUtility.SetDirty(existing);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void CreateFolders(string path)
        {
            var parts = path.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static List<LocalPose> CaptureLocalPose(Transform root)
        {
            var result = new List<LocalPose>();
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                result.Add(new LocalPose
                {
                    transform = transform,
                    position = transform.localPosition,
                    rotation = transform.localRotation,
                    scale = transform.localScale
                });
            }
            return result;
        }

        private static void RestoreLocalPose(List<LocalPose> poses)
        {
            foreach (var pose in poses)
            {
                if (pose.transform == null)
                    continue;
                pose.transform.localPosition = pose.position;
                pose.transform.localRotation = pose.rotation;
                pose.transform.localScale = pose.scale;
            }
        }

        private sealed class HeadingRepair
        {
            private const float RepairWindow = 0.25f;
            private readonly Transform root;
            private readonly bool enabled;
            private readonly float threshold;
            private bool hasPrevious;
            private float previousYaw;
            private float correction;

            public HeadingRepair(
                Transform root,
                bool enabled,
                float threshold)
            {
                this.root = root;
                this.enabled = enabled;
                this.threshold = threshold;
            }

            public Quaternion Update(float time)
            {
                if (!enabled || root == null)
                    return Quaternion.identity;
                var forward = Vector3.ProjectOnPlane(root.forward, Vector3.up);
                if (forward.sqrMagnitude < 0.000001f)
                    return Quaternion.AngleAxis(correction, Vector3.up);
                var yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
                if (hasPrevious && time <= RepairWindow)
                {
                    var delta = Mathf.DeltaAngle(previousYaw, yaw);
                    if (Mathf.Abs(delta) >= threshold)
                        correction -= delta;
                }
                previousYaw = yaw;
                hasPrevious = true;
                return Quaternion.AngleAxis(correction, Vector3.up);
            }
        }
    }
}
