using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Codex.SplitRigRetargeter
{
    internal sealed class ResolvedBone
    {
        public RetargetBone definition;
        public Transform source;
        public Transform target;
        public Quaternion sourceReferenceWorldRotation;
        public Quaternion targetReferenceWorldRotation;
        public Vector3 sourceReferenceWorldPosition;
        public Vector3 targetReferenceWorldPosition;
        public Quaternion previousLocalRotation;
        public bool hasPreviousRotation;
        public Vector3 targetReferenceBoneAxis;
    }

    internal static class RetargetingProfile
    {
        private static readonly string[] Prefixes =
        {
            "mixamorig:", "_1:", "mixamorig_", "mixamorig"
        };

        public static Dictionary<string, Transform> Index(Transform root)
        {
            var result = new Dictionary<string, Transform>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                var normalized = Normalize(transform.name);
                if (!result.ContainsKey(normalized))
                    result.Add(normalized, transform);
            }

            return result;
        }

        private static HashSet<Transform> FindAnimatedTransforms(
            Transform root,
            AnimationClip clip)
        {
            var result = new HashSet<Transform>();
            if (root == null || clip == null)
                return result;

            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.type != typeof(Transform))
                    continue;
                var transform = string.IsNullOrEmpty(binding.path)
                    ? root
                    : root.Find(binding.path);
                if (transform != null)
                    result.Add(transform);
            }
            return result;
        }

        private static Dictionary<string, Transform> IndexForClip(
            Transform root,
            AnimationClip clip)
        {
            var result = new Dictionary<string, Transform>(
                StringComparer.OrdinalIgnoreCase);
            var animated = FindAnimatedTransforms(root, clip);

            // Prefer the hierarchy actually driven by this clip. A GLTF can
            // contain both "_1:" and "mixamorig:" bones with identical names.
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (animated.Contains(transform))
                    AddIfMissing(result, transform);
            }
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                AddIfMissing(result, transform);
            return result;
        }

        private static void AddIfMissing(
            Dictionary<string, Transform> index,
            Transform transform)
        {
            var normalized = Normalize(transform.name);
            if (!index.ContainsKey(normalized))
                index.Add(normalized, transform);
        }

        public static string Normalize(string name)
        {
            var value = name.Trim();
            foreach (var prefix in Prefixes)
            {
                if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    value = value.Substring(prefix.Length);
                    break;
                }
            }

            return value.Replace(" ", string.Empty);
        }

        public static List<ResolvedBone> Resolve(
            Transform sourceRoot,
            Transform targetRoot,
            AnimationClip sourceClip,
            out List<string> warnings)
        {
            warnings = new List<string>();
            var sourceIndex = IndexForClip(sourceRoot, sourceClip);
            var targetIndex = Index(targetRoot);
            var result = new List<ResolvedBone>();

            foreach (var definition in RetargetMap.CreateDefault())
            {
                sourceIndex.TryGetValue(
                    Normalize(definition.sourceName),
                    out var source);
                targetIndex.TryGetValue(
                    Normalize(definition.targetName),
                    out var target);

                if (source == null || target == null)
                {
                    warnings.Add(
                        definition.semantic + ": " +
                        (source == null ? "source missing " + definition.sourceName : "") +
                        (source == null && target == null ? ", " : "") +
                        (target == null ? "target missing " + definition.targetName : ""));
                    continue;
                }

                result.Add(new ResolvedBone
                {
                    definition = definition,
                    source = source,
                    target = target
                });
            }

            return result;
        }

        public static List<ResolvedBone> Resolve(
            Transform sourceRoot,
            Transform targetRoot,
            out List<string> warnings)
        {
            return Resolve(sourceRoot, targetRoot, null, out warnings);
        }
    }
}
