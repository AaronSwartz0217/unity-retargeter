using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Codex.SplitRigRetargeter
{
    internal sealed class BakeSettings
    {
        public GameObject sourceRoot;
        public AnimationClip sourceClip;
        public GameObject targetRoot;
        public float sourceReferenceTime;
        public float targetReferenceTime;
        public bool useScenePoseAsTargetReference;
        public float sampleRate;
        public bool copyHipsPosition;
        public bool useScenePoseAsSourceReference;
        public bool autoScaleHipsMotion;
        public bool alignHipsMotionToTargetFacing;
        public bool alignTargetFacingToRoot;
        public bool zeroInitialHipsHorizontalPosition;
        public Vector3 targetRotationOffset;
        public Vector3 hipsMotionRotationOffset;
        public Vector3 hipsMotionScale;
        public Vector3 hipsPositionOffset;
        public float leftArmBendPlaneOffset;
        public float rightArmBendPlaneOffset;
        public bool stabilizeSuddenFacingChanges;
        public float suddenTurnThreshold;
        public bool skipInvalidFirstFrame;
        public string outputPath;
    }

    internal static class SplitRigBaker
    {
        private sealed class CurveSet
        {
            public readonly AnimationCurve positionX = new AnimationCurve();
            public readonly AnimationCurve positionY = new AnimationCurve();
            public readonly AnimationCurve positionZ = new AnimationCurve();
            public readonly AnimationCurve rotationX = new AnimationCurve();
            public readonly AnimationCurve rotationY = new AnimationCurve();
            public readonly AnimationCurve rotationZ = new AnimationCurve();
            public readonly AnimationCurve rotationW = new AnimationCurve();
        }

        public static List<string> Validate(BakeSettings settings)
        {
            var errors = new List<string>();
            if (settings.sourceRoot == null)
                errors.Add("Source Root is required.");
            if (settings.sourceClip == null)
                errors.Add("Source Clip is required.");
            if (settings.targetRoot == null)
                errors.Add("Target Root is required.");
            if (settings.sourceRoot == settings.targetRoot &&
                settings.sourceRoot != null)
                errors.Add("Source Root and Target Root must be different.");
            if (settings.sampleRate <= 0f)
                errors.Add("Sample Rate must be greater than zero.");
            if (settings.stabilizeSuddenFacingChanges &&
                (settings.suddenTurnThreshold <= 0f ||
                 settings.suddenTurnThreshold > 180f))
                errors.Add("Sudden Turn Threshold must be between 0 and 180.");
            if (string.IsNullOrWhiteSpace(settings.outputPath) ||
                !settings.outputPath.StartsWith("Assets/", StringComparison.Ordinal))
                errors.Add("Output path must be inside Assets/.");
            return errors;
        }

        public static AnimationClip Bake(
            BakeSettings settings,
            out List<string> warnings)
        {
            var errors = Validate(settings);
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join("\n", errors));

            Undo.RegisterFullObjectHierarchyUndo(
                settings.sourceRoot,
                "Sample source retarget animation");
            Undo.RegisterFullObjectHierarchyUndo(
                settings.targetRoot,
                "Bake retarget animation");

            var sourceTransforms = CaptureLocalPose(settings.sourceRoot.transform);
            var targetTransforms = CaptureLocalPose(settings.targetRoot.transform);
            var targetRootForward = settings.targetRoot.transform.forward;
            var targetRootPosition = settings.targetRoot.transform.position;
            warnings = new List<string>();

            try
            {
                AnimationMode.StartAnimationMode();
                var sourceStartOffset = settings.skipInvalidFirstFrame
                    ? 1f / settings.sampleRate
                    : 0f;
                if (!settings.useScenePoseAsSourceReference)
                {
                    Sample(
                        settings.sourceRoot,
                        settings.sourceClip,
                        Mathf.Max(
                            settings.sourceReferenceTime,
                            sourceStartOffset));
                }
                SampleTargetReference(settings);

                var resolved = RetargetingProfile.Resolve(
                    settings.sourceRoot.transform,
                    settings.targetRoot.transform,
                    settings.sourceClip,
                    out var resolveWarnings);
                warnings.AddRange(resolveWarnings);

                if (resolved.Count == 0)
                    throw new InvalidOperationException(
                        "No matching source and target bones were found.");

                CaptureReferences(resolved);
                var hipsScale = settings.autoScaleHipsMotion
                    ? EstimateHeightScale(resolved)
                    : 1f;

                var clip = new AnimationClip
                {
                    name = Path.GetFileNameWithoutExtension(settings.outputPath),
                    frameRate = settings.sampleRate
                };

                var curves = CreateCurves(resolved);
                var hips = resolved.Find(
                    bone => bone.definition.semantic == "Hips");
                var targetFacingCorrection =
                    settings.alignTargetFacingToRoot && hips != null
                        ? CalculateHorizontalAlignment(
                            hips.targetReferenceWorldRotation *
                            Vector3.forward,
                            targetRootForward)
                        : Quaternion.identity;
                var headingStabilizer = new HeadingStabilizer(
                    hips != null ? hips.source : null,
                    settings.stabilizeSuddenFacingChanges,
                    settings.suddenTurnThreshold);
                var duration = Mathf.Max(
                    0f,
                    settings.sourceClip.length - sourceStartOffset);
                var sampleCount = Mathf.Max(
                    1,
                    Mathf.CeilToInt(duration * settings.sampleRate));

                for (var frame = 0; frame <= sampleCount; frame++)
                {
                    var time = Mathf.Min(frame / settings.sampleRate, duration);
                    var sourceTime = Mathf.Min(
                        time + sourceStartOffset,
                        settings.sourceClip.length);
                    Sample(
                        settings.sourceRoot,
                        settings.sourceClip,
                        sourceTime);
                    var headingCorrection = headingStabilizer.Update(time);
                    ApplyPose(
                        resolved,
                        settings.copyHipsPosition,
                        hipsScale,
                        headingCorrection,
                        targetFacingCorrection,
                        targetRootPosition,
                        settings.alignHipsMotionToTargetFacing,
                        settings.zeroInitialHipsHorizontalPosition,
                        settings.targetRotationOffset,
                        settings.hipsMotionRotationOffset,
                        settings.hipsMotionScale,
                        settings.hipsPositionOffset,
                        settings.leftArmBendPlaneOffset,
                        settings.rightArmBendPlaneOffset);
                    AddKeys(
                        settings.targetRoot.transform,
                        resolved,
                        curves,
                        time,
                        settings.copyHipsPosition);
                }

                WriteCurves(
                    clip,
                    settings.targetRoot.transform,
                    resolved,
                    curves,
                    settings.copyHipsPosition);

                clip.EnsureQuaternionContinuity();
                SaveClip(clip, settings.outputPath);
                return AssetDatabase.LoadAssetAtPath<AnimationClip>(
                    settings.outputPath);
            }
            finally
            {
                if (AnimationMode.InAnimationMode())
                    AnimationMode.StopAnimationMode();
                RestoreLocalPose(sourceTransforms);
                RestoreLocalPose(targetTransforms);
            }
        }

        private static void SampleTargetReference(BakeSettings settings)
        {
            if (settings.useScenePoseAsTargetReference)
                return;
            var animator = settings.targetRoot.GetComponentInChildren<Animator>();
            if (animator == null || animator.runtimeAnimatorController == null)
                return;

            var clips = animator.runtimeAnimatorController.animationClips;
            if (clips.Length > 0)
                AnimationMode.SampleAnimationClip(
                    settings.targetRoot,
                    clips[0],
                    Mathf.Clamp(
                        settings.targetReferenceTime,
                        0f,
                        clips[0].length));
        }

        private static void Sample(
            GameObject root,
            AnimationClip clip,
            float time)
        {
            AnimationMode.SampleAnimationClip(
                root,
                clip,
                Mathf.Clamp(time, 0f, clip.length));
        }

        private static void CaptureReferences(List<ResolvedBone> bones)
        {
            foreach (var bone in bones)
            {
                bone.sourceReferenceWorldRotation = bone.source.rotation;
                bone.targetReferenceWorldRotation = bone.target.rotation;
                bone.sourceReferenceWorldPosition = bone.source.position;
                bone.targetReferenceWorldPosition = bone.target.position;
                bone.targetReferenceBoneAxis =
                    FindReferenceBoneAxis(bone.target);
                bone.hasPreviousRotation = false;
            }
        }

        private static Vector3 FindReferenceBoneAxis(Transform bone)
        {
            if (bone.childCount == 0)
                return bone.right;
            var axis = bone.GetChild(0).position - bone.position;
            return axis.sqrMagnitude > 0.000001f
                ? axis.normalized
                : bone.right;
        }

        private static float EstimateHeightScale(List<ResolvedBone> bones)
        {
            var hips = bones.Find(b => b.definition.semantic == "Hips");
            var head = bones.Find(b => b.definition.semantic == "Head");
            if (hips == null || head == null)
                return 1f;

            var sourceHeight = Vector3.Distance(
                hips.sourceReferenceWorldPosition,
                head.sourceReferenceWorldPosition);
            var targetHeight = Vector3.Distance(
                hips.targetReferenceWorldPosition,
                head.targetReferenceWorldPosition);
            return sourceHeight > 0.0001f ? targetHeight / sourceHeight : 1f;
        }

        private static void ApplyPose(
            List<ResolvedBone> bones,
            bool copyHipsPosition,
            float hipsScale,
            Quaternion headingCorrection,
            Quaternion targetFacingCorrection,
            Vector3 targetRootPosition,
            bool alignHipsMotionToTargetFacing,
            bool zeroInitialHipsHorizontalPosition,
            Vector3 targetRotationOffset,
            Vector3 hipsMotionRotationOffset,
            Vector3 hipsMotionScale,
            Vector3 hipsPositionOffset,
            float leftArmBendPlaneOffset,
            float rightArmBendPlaneOffset)
        {
            var manualFacingCorrection =
                Quaternion.Euler(targetRotationOffset);
            var manualMotionCorrection =
                Quaternion.Euler(hipsMotionRotationOffset);
            // Transform.rotation writes the correct local rotation for the target
            // hierarchy, which is why this works even though the source is split
            // across several armature objects.
            foreach (var bone in bones)
            {
                // Align each source bone's reference axes to the corresponding
                // target bone. This preserves differing bone-roll conventions
                // (especially Mixamo upper legs and mirrored limbs).
                var referenceAxisAlignment =
                    bone.targetReferenceWorldRotation *
                    Quaternion.Inverse(
                        bone.sourceReferenceWorldRotation);
                var axisAlignedRotation =
                    referenceAxisAlignment *
                    bone.source.rotation;
                var bendPlaneOffset = 0f;
                if (bone.definition.semantic == "Left upper arm")
                    bendPlaneOffset = leftArmBendPlaneOffset;
                else if (bone.definition.semantic == "Right upper arm")
                    bendPlaneOffset = rightArmBendPlaneOffset;

                if (Mathf.Abs(bendPlaneOffset) > 0.001f)
                {
                    var poseDelta =
                        axisAlignedRotation *
                        Quaternion.Inverse(
                            bone.targetReferenceWorldRotation);
                    var currentBoneAxis =
                        poseDelta * bone.targetReferenceBoneAxis;
                    axisAlignedRotation =
                        Quaternion.AngleAxis(
                            bendPlaneOffset,
                            currentBoneAxis) *
                        axisAlignedRotation;
                }

                // The initial facing correction is a character-wide world yaw,
                // so it must be applied after per-bone axis alignment. Applying
                // it inside the alignment twists left/right limb axes.
                bone.target.rotation =
                    manualFacingCorrection *
                    targetFacingCorrection *
                    headingCorrection *
                    axisAlignedRotation;

                if (copyHipsPosition && bone.definition.copyPosition)
                {
                    var sourceDelta =
                        bone.source.position -
                        bone.sourceReferenceWorldPosition;
                    sourceDelta = headingCorrection * sourceDelta;
                    sourceDelta = targetFacingCorrection * sourceDelta;
                    if (alignHipsMotionToTargetFacing)
                    {
                        var motionAlignment =
                            CalculateHorizontalAlignment(
                                bone.sourceReferenceWorldRotation,
                                bone.targetReferenceWorldRotation);
                        sourceDelta = motionAlignment * sourceDelta;
                    }
                    sourceDelta = Vector3.Scale(
                        manualMotionCorrection * sourceDelta,
                        hipsMotionScale);
                    bone.target.position =
                        (zeroInitialHipsHorizontalPosition
                            ? new Vector3(
                                targetRootPosition.x,
                                bone.targetReferenceWorldPosition.y,
                                targetRootPosition.z)
                            : bone.targetReferenceWorldPosition) +
                        sourceDelta * hipsScale +
                        hipsPositionOffset;
                }
            }
        }

        private static Quaternion CalculateHorizontalAlignment(
            Quaternion sourceReference,
            Quaternion targetReference)
        {
            var sourceForward = Vector3.ProjectOnPlane(
                sourceReference * Vector3.forward,
                Vector3.up);
            var targetForward = Vector3.ProjectOnPlane(
                targetReference * Vector3.forward,
                Vector3.up);
            if (sourceForward.sqrMagnitude < 0.000001f ||
                targetForward.sqrMagnitude < 0.000001f)
                return Quaternion.identity;

            sourceForward.Normalize();
            targetForward.Normalize();
            return Quaternion.FromToRotation(
                sourceForward,
                targetForward);
        }

        private static Quaternion CalculateHorizontalAlignment(
            Vector3 sourceForward,
            Vector3 targetForward)
        {
            sourceForward = Vector3.ProjectOnPlane(
                sourceForward,
                Vector3.up);
            targetForward = Vector3.ProjectOnPlane(
                targetForward,
                Vector3.up);
            if (sourceForward.sqrMagnitude < 0.000001f ||
                targetForward.sqrMagnitude < 0.000001f)
                return Quaternion.identity;
            var angle = Vector3.SignedAngle(
                sourceForward,
                targetForward,
                Vector3.up);
            return Quaternion.AngleAxis(angle, Vector3.up);
        }

        private sealed class HeadingStabilizer
        {
            private readonly Transform hips;
            private readonly bool enabled;
            private readonly float threshold;
            private bool hasPreviousYaw;
            private float previousRawYaw;
            private float accumulatedCorrection;
            private const float InitialRepairWindow = 0.25f;

            public HeadingStabilizer(
                Transform hips,
                bool enabled,
                float threshold)
            {
                this.hips = hips;
                this.enabled = enabled;
                this.threshold = threshold;
            }

            public Quaternion Update(float time)
            {
                if (!enabled || hips == null)
                    return Quaternion.identity;

                var forward = Vector3.ProjectOnPlane(
                    hips.forward,
                    Vector3.up);
                if (forward.sqrMagnitude < 0.000001f)
                    return Quaternion.AngleAxis(
                        accumulatedCorrection,
                        Vector3.up);

                var rawYaw = Mathf.Atan2(forward.x, forward.z) *
                             Mathf.Rad2Deg;
                if (hasPreviousYaw && time <= InitialRepairWindow)
                {
                    var delta = Mathf.DeltaAngle(previousRawYaw, rawYaw);
                    if (Mathf.Abs(delta) >= threshold)
                        accumulatedCorrection -= delta;
                }

                previousRawYaw = rawYaw;
                hasPreviousYaw = true;
                return Quaternion.AngleAxis(
                    accumulatedCorrection,
                    Vector3.up);
            }
        }

        private static Dictionary<ResolvedBone, CurveSet> CreateCurves(
            List<ResolvedBone> bones)
        {
            var result = new Dictionary<ResolvedBone, CurveSet>();
            foreach (var bone in bones)
                result.Add(bone, new CurveSet());
            return result;
        }

        private static void AddKeys(
            Transform targetRoot,
            List<ResolvedBone> bones,
            Dictionary<ResolvedBone, CurveSet> curves,
            float time,
            bool copyHipsPosition)
        {
            foreach (var bone in bones)
            {
                var rotation = bone.target.localRotation;
                if (bone.hasPreviousRotation &&
                    Quaternion.Dot(bone.previousLocalRotation, rotation) < 0f)
                {
                    rotation = new Quaternion(
                        -rotation.x,
                        -rotation.y,
                        -rotation.z,
                        -rotation.w);
                }

                bone.previousLocalRotation = rotation;
                bone.hasPreviousRotation = true;
                var set = curves[bone];
                set.rotationX.AddKey(time, rotation.x);
                set.rotationY.AddKey(time, rotation.y);
                set.rotationZ.AddKey(time, rotation.z);
                set.rotationW.AddKey(time, rotation.w);

                if (copyHipsPosition && bone.definition.copyPosition)
                {
                    var position = bone.target.localPosition;
                    set.positionX.AddKey(time, position.x);
                    set.positionY.AddKey(time, position.y);
                    set.positionZ.AddKey(time, position.z);
                }
            }
        }

        private static void WriteCurves(
            AnimationClip clip,
            Transform targetRoot,
            List<ResolvedBone> bones,
            Dictionary<ResolvedBone, CurveSet> curves,
            bool copyHipsPosition)
        {
            foreach (var bone in bones)
            {
                var path = AnimationUtility.CalculateTransformPath(
                    bone.target,
                    targetRoot);
                var set = curves[bone];
                SetCurve(clip, path, "m_LocalRotation.x", set.rotationX);
                SetCurve(clip, path, "m_LocalRotation.y", set.rotationY);
                SetCurve(clip, path, "m_LocalRotation.z", set.rotationZ);
                SetCurve(clip, path, "m_LocalRotation.w", set.rotationW);

                if (copyHipsPosition && bone.definition.copyPosition)
                {
                    SetCurve(clip, path, "m_LocalPosition.x", set.positionX);
                    SetCurve(clip, path, "m_LocalPosition.y", set.positionY);
                    SetCurve(clip, path, "m_LocalPosition.z", set.positionZ);
                }
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
            if (!string.IsNullOrEmpty(directory) &&
                !AssetDatabase.IsValidFolder(directory))
            {
                CreateAssetFolders(directory);
            }

            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(clip, path);
            }
            else
            {
                EditorUtility.CopySerialized(clip, existing);
                UnityEngine.Object.DestroyImmediate(clip);
                EditorUtility.SetDirty(existing);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void CreateAssetFolders(string path)
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

        private sealed class LocalPose
        {
            public Transform transform;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
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
    }
}
