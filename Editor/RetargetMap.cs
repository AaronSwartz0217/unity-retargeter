using System;
using System.Collections.Generic;
using UnityEngine;

namespace Codex.SplitRigRetargeter
{
    [Serializable]
    internal sealed class RetargetBone
    {
        public string semantic;
        public string sourceName;
        public string targetName;
        public bool copyPosition;
    }

    internal static class RetargetMap
    {
        public static List<RetargetBone> CreateDefault()
        {
            var result = new List<RetargetBone>
            {
                Bone("Hips", "Hips", "Hips", true),
                Bone("Spine", "Spine", "Spine"),
                // The split GLB has two spine joints, while Mixamo has three.
                // Source Spine1 represents the upper torso and maps to Spine2.
                Bone("Upper torso", "Spine1", "Spine2"),
                Bone("Neck", "Neck", "Neck"),
                Bone("Head", "Head", "Head"),
                Bone("Left shoulder", "LeftShoulder", "LeftShoulder"),
                Bone("Left upper arm", "LeftArm", "LeftArm"),
                Bone("Left forearm", "LeftForeArm", "LeftForeArm"),
                Bone("Left hand", "LeftHand", "LeftHand"),
                Bone("Right shoulder", "RightShoulder", "RightShoulder"),
                Bone("Right upper arm", "RightArm", "RightArm"),
                Bone("Right forearm", "RightForeArm", "RightForeArm"),
                Bone("Right hand", "RightHand", "RightHand"),
                Bone("Left upper leg", "LeftUpLeg", "LeftUpLeg"),
                Bone("Left lower leg", "LeftLeg", "LeftLeg"),
                Bone("Left foot", "LeftFoot", "LeftFoot"),
                Bone("Left toe", "LeftToeBase", "LeftToeBase"),
                Bone("Right upper leg", "RightUpLeg", "RightUpLeg"),
                Bone("Right lower leg", "RightLeg", "RightLeg"),
                Bone("Right foot", "RightFoot", "RightFoot"),
                Bone("Right toe", "RightToeBase", "RightToeBase")
            };

            AddFinger(result, "Left", "Thumb");
            AddFinger(result, "Left", "Index");
            AddFinger(result, "Left", "Middle");
            AddFinger(result, "Left", "Ring");
            AddFinger(result, "Left", "Pinky");
            AddFinger(result, "Right", "Thumb");
            AddFinger(result, "Right", "Index");
            AddFinger(result, "Right", "Middle");
            AddFinger(result, "Right", "Ring");
            AddFinger(result, "Right", "Pinky");
            return result;
        }

        private static RetargetBone Bone(
            string semantic,
            string source,
            string target,
            bool position = false)
        {
            return new RetargetBone
            {
                semantic = semantic,
                sourceName = source,
                targetName = target,
                copyPosition = position
            };
        }

        private static void AddFinger(
            List<RetargetBone> result,
            string side,
            string finger)
        {
            for (var segment = 1; segment <= 3; segment++)
            {
                var name = side + "Hand" + finger + segment;
                result.Add(Bone(name, name, name));
            }
        }
    }
}
