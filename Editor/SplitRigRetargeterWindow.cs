using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace Codex.SplitRigRetargeter
{
    internal sealed class SplitRigRetargeterWindow : EditorWindow
    {
        private enum WorkArea
        {
            Retarget,
            Repair
        }

        private enum UiLanguage
        {
            Chinese,
            English
        }

        [SerializeField] private WorkArea workArea;
        [SerializeField] private UiLanguage uiLanguage;
        private const string DefaultSourceRootKey =
            "Codex.SplitRigRetargeter.DefaultSourceRoot";
        private const string DefaultSourceClipKey =
            "Codex.SplitRigRetargeter.DefaultSourceClip";
        private const string DefaultTargetRootKey =
            "Codex.SplitRigRetargeter.DefaultTargetRoot";
        private const string LastBakeSnapshotKey =
            "Codex.SplitRigRetargeter.LastBakeSnapshot";

        [System.Serializable]
        private sealed class LastBakeSnapshot
        {
            public string sourceRootId;
            public string sourceClipId;
            public string targetRootId;
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
            public bool sourceAnimationAlreadyRepaired;
            public bool stabilizeSuddenFacingChanges;
            public float suddenTurnThreshold;
            public bool skipInvalidFirstFrame;
            public bool autoNameFromRotation;
            public string outputFolder;
            public string outputPath;
        }
        [SerializeField] private GameObject sourceRoot;
        [SerializeField] private AnimationClip sourceClip;
        [SerializeField] private GameObject targetRoot;
        [SerializeField] private float sourceReferenceTime;
        [SerializeField] private float targetReferenceTime;
        [SerializeField] private bool useScenePoseAsTargetReference = true;
        [SerializeField] private float sampleRate = 120f;
        [SerializeField] private bool copyHipsPosition = true;
        [SerializeField] private bool useScenePoseAsSourceReference;
        [SerializeField] private bool autoScaleHipsMotion = true;
        [SerializeField] private bool alignHipsMotionToTargetFacing = true;
        [SerializeField] private bool alignTargetFacingToRoot = true;
        [SerializeField] private bool zeroInitialHipsHorizontalPosition = true;
        [SerializeField] private Vector3 targetRotationOffset;
        [SerializeField] private Vector3 hipsMotionRotationOffset;
        [SerializeField] private Vector3 hipsMotionScale = Vector3.one;
        [SerializeField] private Vector3 hipsPositionOffset;
        [SerializeField] private float leftArmBendPlaneOffset;
        [SerializeField] private float rightArmBendPlaneOffset;
        [SerializeField] private bool sourceAnimationAlreadyRepaired;
        [SerializeField] private bool stabilizeSuddenFacingChanges = true;
        [SerializeField] private float suddenTurnThreshold = 90f;
        [SerializeField] private bool skipInvalidFirstFrame = true;
        [SerializeField] private string outputPath =
            "Assets/Retargeted/Retargeted_Mixamo.anim";
        [SerializeField] private bool autoNameFromRotation = true;
        [SerializeField] private string outputFolder = "Assets/Retargeted";
        [SerializeField] private GameObject repairSourceRoot;
        [SerializeField] private AnimationClip repairSourceClip;
        [SerializeField] private Transform repairMotionRoot;
        [SerializeField] private float repairSampleRate = 120f;
        [SerializeField] private bool repairSkipFirstFrame = true;
        [SerializeField] private bool repairInitialFlip = true;
        [SerializeField] private bool repairAlignInitialFacingToRoot = true;
        [SerializeField] private bool repairZeroInitialHorizontalPosition = true;
        [SerializeField] private bool repairZeroInitialLocalRootXZ;
        [SerializeField] private float repairTurnThreshold = 90f;
        [SerializeField] private Vector3 repairRootRotation;
        [SerializeField] private Vector3 repairMotionRotation;
        [SerializeField] private Vector3 repairMotionScale = Vector3.one;
        [SerializeField] private Vector3 repairPositionOffset;
        [SerializeField] private string repairOutputPath =
            "Assets/Repaired/Repaired_Animation.anim";

        private Vector2 scroll;
        private List<string> lastMessages = new List<string>();

        private void OnEnable()
        {
            LoadDefaultInputs();
        }

        private void ResetManualAdjustments()
        {
            targetRotationOffset = Vector3.zero;
            hipsMotionRotationOffset = Vector3.zero;
            hipsMotionScale = Vector3.one;
            hipsPositionOffset = Vector3.zero;
            leftArmBendPlaneOffset = 0f;
            rightArmBendPlaneOffset = 0f;
        }

        private void ApplyProblemGlbPreset()
        {
            sourceReferenceTime = 0f;
            targetReferenceTime = 0f;
            useScenePoseAsTargetReference = true;
            sampleRate = 120f;
            copyHipsPosition = true;
            useScenePoseAsSourceReference = false;
            autoScaleHipsMotion = true;
            alignHipsMotionToTargetFacing = true;
            alignTargetFacingToRoot = true;
            zeroInitialHipsHorizontalPosition = true;
            ResetManualAdjustments();
            sourceAnimationAlreadyRepaired = false;
            stabilizeSuddenFacingChanges = true;
            suddenTurnThreshold = 90f;
            skipInvalidFirstFrame = true;
            autoNameFromRotation = true;
            outputFolder = "Assets/Retargeted";
            outputPath = BuildAutomaticOutputPath();
            lastMessages.Clear();
            ShowNotification(new GUIContent("GLB 120 FPS preset applied"));
        }


        [MenuItem("Tools/Split Rig Retargeter")]
        private static void Open()
        {
            GetWindow<SplitRigRetargeterWindow>(
                "Split Rig Retargeter");
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            uiLanguage = (UiLanguage)GUILayout.Toolbar(
                (int)uiLanguage,
                new[] { "中文", "English" });
            workArea = (WorkArea)GUILayout.Toolbar(
                (int)workArea,
                new[]
                {
                    L("骨骼重定向", "Retarget"),
                    L("动画修复", "Repair")
                });
            EditorGUILayout.Space();
            if (workArea == WorkArea.Repair)
            {
                DrawRepairGUI();
                EditorGUILayout.EndScrollView();
                return;
            }
            EditorGUILayout.LabelField(
                L("分区骨骼 → Mixamo", "Split GLB Rig → Mixamo"),
                EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                L(
                    "把源动画角色和 Mixamo 目标角色放入场景。本区域会把动作" +
                    "映射到另一套骨骼，不修改原始层级。",
                    "Place a source character and a Mixamo target in the scene. " +
                    "This area maps motion to another skeleton without changing " +
                    "the source hierarchy."),
                MessageType.Info);
            if (GUILayout.Button(
                    L("重置手动调整", "Reset Manual Adjustments")))
                ResetManualAdjustments();
            if (GUILayout.Button(
                    L("应用 GLB 120 FPS 修复预设",
                        "Apply GLB 120 FPS Repair Preset")))
                ApplyProblemGlbPreset();

            sourceRoot = (GameObject)EditorGUILayout.ObjectField(
                L("源骨架根节点", "Source Root"),
                sourceRoot,
                typeof(GameObject),
                true);
            sourceClip = (AnimationClip)EditorGUILayout.ObjectField(
                L("源动画", "Source Clip"),
                sourceClip,
                typeof(AnimationClip),
                false);
            targetRoot = (GameObject)EditorGUILayout.ObjectField(
                L("Mixamo 目标根节点", "Mixamo Target Root"),
                targetRoot,
                typeof(GameObject),
                true);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(
                    L("保存当前输入为默认", "Save Current Inputs As Default")))
                SaveDefaultInputs();
            if (GUILayout.Button(
                    L("重新载入默认输入", "Reload Default Inputs")))
                LoadDefaultInputs();
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button(
                    L("恢复上次生成的全部设置", "Load Last Bake (All Settings)")))
                LoadLastBakeSnapshot();
            EditorGUILayout.HelpBox(
                "Saves Source Root, Source Clip and Mixamo Target Root. " +
                "Rotation, position and arm test values are not changed.",
                MessageType.None);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                L("参考姿势", "Reference Pose"),
                EditorStyles.boldLabel);
            sourceReferenceTime = EditorGUILayout.FloatField(
                L("源参考时间", "Source Reference Time"),
                sourceReferenceTime);
            using (new EditorGUI.DisabledScope(
                       sourceAnimationAlreadyRepaired))
            {
                useScenePoseAsSourceReference = EditorGUILayout.Toggle(
                    L(
                        "使用场景初始姿态作为源参考",
                        "Use Scene Pose As Source Reference"),
                    useScenePoseAsSourceReference);
            }
            useScenePoseAsTargetReference = EditorGUILayout.Toggle(
                L(
                    "使用场景初始姿态作为目标参考",
                    "Use Scene Pose As Target Reference"),
                useScenePoseAsTargetReference);
            using (new EditorGUI.DisabledScope(
                       useScenePoseAsTargetReference))
            {
                targetReferenceTime = EditorGUILayout.FloatField(
                    L("目标参考时间", "Target Reference Time"),
                    targetReferenceTime);
            }
            EditorGUILayout.HelpBox(
                L(
                    "未经修复的动画可使用中立姿态时间。修复版动画的第 0 帧通常已经是动作帧，必须使用场景中尚未播放动画的 T-Pose/初始姿态作为源参考。",
                    "For raw clips, use a neutral-pose time. A repaired clip usually starts on an action pose, so it must use the unanimated scene T-pose/default pose as its source reference."),
                MessageType.None);

            EditorGUILayout.Space();
            sampleRate = EditorGUILayout.FloatField(
                L("采样率", "Sample Rate"),
                sampleRate);
            copyHipsPosition = EditorGUILayout.Toggle(
                L("复制 Hips 位移", "Copy Hips Position"),
                copyHipsPosition);
            using (new EditorGUI.DisabledScope(!copyHipsPosition))
            {
                autoScaleHipsMotion = EditorGUILayout.Toggle(
                    L("自动缩放位移", "Auto Scale Motion"),
                    autoScaleHipsMotion);
                alignHipsMotionToTargetFacing = EditorGUILayout.Toggle(
                    L(
                        "按目标朝向对齐 Hips 位移",
                        "Align Hips Motion To Target Facing"),
                    alignHipsMotionToTargetFacing);
                hipsMotionRotationOffset = EditorGUILayout.Vector3Field(
                    L("Hips 位移旋转 XYZ", "Hips Motion Rotation XYZ"),
                    hipsMotionRotationOffset);
                hipsMotionScale = EditorGUILayout.Vector3Field(
                    L("Hips 位移倍率 XYZ", "Hips Motion Scale XYZ"),
                    hipsMotionScale);
                hipsPositionOffset = EditorGUILayout.Vector3Field(
                    L("Hips 位置偏移 XYZ", "Hips Position Offset XYZ"),
                    hipsPositionOffset);
            }
            using (new EditorGUI.DisabledScope(
                       sourceAnimationAlreadyRepaired))
            {
                alignTargetFacingToRoot = EditorGUILayout.Toggle(
                    L(
                        "目标正面匹配根节点 Z 箭头",
                        "Align Target Facing To Root Z Arrow"),
                    alignTargetFacingToRoot);
                zeroInitialHipsHorizontalPosition =
                    EditorGUILayout.Toggle(
                        L(
                            "目标首帧 Hips 水平位置归零",
                            "Zero Initial Target Hips Horizontal Position"),
                        zeroInitialHipsHorizontalPosition);
            }
            EditorGUILayout.HelpBox(
                L(
                    "仅用于未提前修复的源动画。第一项对齐 Mixamo 正面和移动轨迹，第二项消除目标 Hips 的固定 X/Z 起点偏移。",
                    "For unrepaired source clips only. The first aligns Mixamo facing and motion; the second removes the fixed initial target Hips X/Z offset."),
                MessageType.None);
            targetRotationOffset = EditorGUILayout.Vector3Field(
                L("目标姿态旋转 XYZ", "Target Rotation Offset XYZ"),
                targetRotationOffset);
            leftArmBendPlaneOffset = EditorGUILayout.Slider(
                L("左臂弯折平面", "Left Arm Bend Plane Offset"),
                leftArmBendPlaneOffset,
                -180f,
                180f);
            rightArmBendPlaneOffset = EditorGUILayout.Slider(
                L("右臂弯折平面", "Right Arm Bend Plane Offset"),
                rightArmBendPlaneOffset,
                -180f,
                180f);

            var repairedBefore = sourceAnimationAlreadyRepaired;
            sourceAnimationAlreadyRepaired = EditorGUILayout.Toggle(
                L("源动画已提前修复", "Source Animation Already Repaired"),
                sourceAnimationAlreadyRepaired);
            if (sourceAnimationAlreadyRepaired && !repairedBefore)
            {
                useScenePoseAsSourceReference = true;
                alignTargetFacingToRoot = false;
                zeroInitialHipsHorizontalPosition = false;
                stabilizeSuddenFacingChanges = false;
                skipInvalidFirstFrame = false;
            }
            EditorGUILayout.HelpBox(
                L(
                    "勾选后将自动关闭并锁定目标朝向对齐、目标起点归零、开头翻转修复和异常第一帧跳过，避免重复处理。",
                    "Disables and locks target-facing alignment, target-position zeroing, initial flip repair and first-frame skipping to avoid processing a repaired clip twice."),
                MessageType.None);

            using (new EditorGUI.DisabledScope(
                       sourceAnimationAlreadyRepaired))
            {
                stabilizeSuddenFacingChanges = EditorGUILayout.Toggle(
                    L("修复开头 180°翻转", "Fix Initial 180 Degree Flip"),
                    stabilizeSuddenFacingChanges);
                using (new EditorGUI.DisabledScope(
                           !stabilizeSuddenFacingChanges))
                {
                    suddenTurnThreshold = EditorGUILayout.Slider(
                        L("突然转向阈值", "Sudden Turn Threshold"),
                        suddenTurnThreshold,
                        10f,
                        180f);
                }
            }
            EditorGUILayout.HelpBox(
                "Enable only when the source hips suddenly flips direction " +
                "at the start of the action. Only the first 0.25 seconds are " +
                "checked, so later intentional turns are preserved.",
                MessageType.None);
            using (new EditorGUI.DisabledScope(
                       sourceAnimationAlreadyRepaired))
            {
                skipInvalidFirstFrame = EditorGUILayout.Toggle(
                    L("跳过异常第一帧", "Skip Invalid First Frame"),
                    skipInvalidFirstFrame);
            }
            EditorGUILayout.HelpBox(
                "Enable when frame 0 is a bind/rest pose and frame 1 suddenly " +
                "jumps into the real animation. The first valid sampled frame " +
                "becomes the new reference and output start.",
                MessageType.None);
            autoNameFromRotation = EditorGUILayout.Toggle(
                L("按旋转值自动命名", "Auto Name From Rotation"),
                autoNameFromRotation);
            if (autoNameFromRotation)
            {
                outputFolder = EditorGUILayout.TextField(
                    L("输出文件夹", "Output Folder"),
                    outputFolder);
                outputPath = BuildAutomaticOutputPath();
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.TextField(
                        L("输出动画", "Output Clip"),
                        outputPath);
            }
            else
            {
                outputPath = EditorGUILayout.TextField(
                    L("输出动画", "Output Clip"),
                    outputPath);
            }

            EditorGUILayout.Space();
            if (GUILayout.Button(L("检查骨骼映射", "Validate Mapping")))
                ValidateMapping();

            GUI.backgroundColor = new Color(0.45f, 0.85f, 0.55f);
            if (GUILayout.Button(
                    L("生成重定向动画", "Bake Retargeted Clip"),
                    GUILayout.Height(32f)))
                Bake();
            GUI.backgroundColor = Color.white;

            foreach (var message in lastMessages)
                EditorGUILayout.HelpBox(message, MessageType.Warning);

            EditorGUILayout.EndScrollView();
        }

        private string L(string chinese, string english)
        {
            return uiLanguage == UiLanguage.Chinese ? chinese : english;
        }

        private void DrawRepairGUI()
        {
            EditorGUILayout.LabelField(
                L("原骨架动画修复", "Repair Animation On Original Rig"),
                EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                L(
                    "保持原骨架和骨骼路径不变，只重新采样并修复动画。适用于" +
                    "异常 T-Pose 首帧、开头方向翻转和四元数不连续。",
                    "Keeps the original skeleton and paths. Re-samples the clip " +
                    "to repair an invalid T-pose first frame, initial facing " +
                    "flips and quaternion discontinuities."),
                MessageType.Info);

            repairSourceRoot = (GameObject)EditorGUILayout.ObjectField(
                L("动画对象根节点", "Animation Root"),
                repairSourceRoot,
                typeof(GameObject),
                true);
            repairSourceClip = (AnimationClip)EditorGUILayout.ObjectField(
                L("需要修复的动画", "Animation Clip To Repair"),
                repairSourceClip,
                typeof(AnimationClip),
                false);
            repairMotionRoot = (Transform)EditorGUILayout.ObjectField(
                L("根运动骨骼（可留空）", "Motion Root (Optional)"),
                repairMotionRoot,
                typeof(Transform),
                true);
            EditorGUILayout.HelpBox(
                L(
                    "留空时会自动寻找 Hips。",
                    "Leave empty to detect Hips automatically."),
                MessageType.None);

            repairSampleRate = EditorGUILayout.FloatField(
                L("采样率", "Sample Rate"),
                repairSampleRate);
            repairSkipFirstFrame = EditorGUILayout.Toggle(
                L("跳过异常第一帧", "Skip Invalid First Frame"),
                repairSkipFirstFrame);
            repairInitialFlip = EditorGUILayout.Toggle(
                L("修复开头方向翻转", "Fix Initial Facing Flip"),
                repairInitialFlip);
            repairAlignInitialFacingToRoot = EditorGUILayout.Toggle(
                L(
                    "初始正面匹配根节点 Z 箭头",
                    "Align Initial Facing To Root Z Arrow"),
                repairAlignInitialFacingToRoot);
            EditorGUILayout.HelpBox(
                L(
                    "将第一有效帧的角色水平正面旋转到场景对象的蓝色 Z 箭头方向，并同步旋转根运动轨迹。它解决“动作连续但角色始终背对箭头”。",
                    "Rotates the first valid horizontal facing and root-motion trajectory to the scene object's blue Z arrow. This fixes a continuous animation that remains opposite to the arrow."),
                MessageType.None);
            var localRootZeroBefore = repairZeroInitialLocalRootXZ;
            repairZeroInitialLocalRootXZ = EditorGUILayout.Toggle(
                L(
                    "根骨骼本地 X/Z 归零（Y 不变）",
                    "Zero Local Root X/Z (Keep Y)"),
                repairZeroInitialLocalRootXZ);
            if (repairZeroInitialLocalRootXZ && !localRootZeroBefore)
                repairZeroInitialHorizontalPosition = false;
            using (new EditorGUI.DisabledScope(
                       repairZeroInitialLocalRootXZ))
            {
                repairZeroInitialHorizontalPosition =
                    EditorGUILayout.Toggle(
                        L(
                            "首帧水平根位置归零",
                            "Zero Initial Horizontal Root Position"),
                        repairZeroInitialHorizontalPosition);
            }
            EditorGUILayout.HelpBox(
                L(
                    "本地归零会从整段动画减去第一有效帧的根骨骼本地 X/Z，使起点为 (0, Y, 0)，保留 Y 和后续相对位移。它与世界坐标的首帧归零互斥。",
                    "Local zeroing subtracts the first valid local root X/Z from the entire clip, making the start (0, Y, 0) while preserving Y and subsequent relative motion. It is mutually exclusive with world-space zeroing."),
                MessageType.None);
            using (new EditorGUI.DisabledScope(!repairInitialFlip))
            {
                repairTurnThreshold = EditorGUILayout.Slider(
                    L("方向跳变阈值", "Turn Threshold"),
                    repairTurnThreshold,
                    10f,
                    180f);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                L("手动根运动调整", "Manual Root Motion Adjustments"),
                EditorStyles.boldLabel);
            repairRootRotation = EditorGUILayout.Vector3Field(
                L("模型旋转 XYZ", "Root Rotation XYZ"),
                repairRootRotation);
            repairMotionRotation = EditorGUILayout.Vector3Field(
                L("位移方向旋转 XYZ", "Motion Rotation XYZ"),
                repairMotionRotation);
            repairMotionScale = EditorGUILayout.Vector3Field(
                L("位移倍率 XYZ", "Motion Scale XYZ"),
                repairMotionScale);
            repairPositionOffset = EditorGUILayout.Vector3Field(
                L("位置偏移 XYZ", "Position Offset XYZ"),
                repairPositionOffset);
            repairOutputPath = EditorGUILayout.TextField(
                L("输出动画", "Output Clip"),
                repairOutputPath);

            EditorGUILayout.Space();
            if (GUILayout.Button(L("检查修复设置", "Validate Repair Settings")))
                ValidateRepair();
            GUI.backgroundColor = new Color(0.45f, 0.75f, 0.95f);
            if (GUILayout.Button(
                    L("生成修复动画", "Bake Repaired Clip"),
                    GUILayout.Height(32f)))
                BakeRepair();
            GUI.backgroundColor = Color.white;

            foreach (var message in lastMessages)
                EditorGUILayout.HelpBox(message, MessageType.Warning);
        }

        private RepairSettings CurrentRepairSettings()
        {
            return new RepairSettings
            {
                sourceRoot = repairSourceRoot,
                sourceClip = repairSourceClip,
                motionRoot = repairMotionRoot,
                sampleRate = repairSampleRate,
                skipInvalidFirstFrame = repairSkipFirstFrame,
                fixInitialFacingFlip = repairInitialFlip,
                alignInitialFacingToRoot =
                    repairAlignInitialFacingToRoot,
                zeroInitialHorizontalPosition =
                    repairZeroInitialHorizontalPosition,
                zeroInitialLocalRootXZ =
                    repairZeroInitialLocalRootXZ,
                suddenTurnThreshold = repairTurnThreshold,
                rootRotationOffset = repairRootRotation,
                rootMotionRotation = repairMotionRotation,
                rootMotionScale = repairMotionScale,
                rootPositionOffset = repairPositionOffset,
                outputPath = repairOutputPath
            };
        }

        private void ValidateRepair()
        {
            lastMessages.Clear();
            var errors = AnimationRepairBaker.Validate(
                CurrentRepairSettings());
            lastMessages.AddRange(errors);
            if (errors.Count == 0)
            {
                lastMessages.Add(
                    L(
                        "设置有效，可以生成修复动画。",
                        "Settings are valid. The repaired clip can be baked."));
            }
        }

        private void BakeRepair()
        {
            lastMessages.Clear();
            try
            {
                var clip = AnimationRepairBaker.Bake(
                    CurrentRepairSettings(),
                    out var warnings);
                lastMessages.AddRange(warnings);
                Selection.activeObject =
                    AssetDatabase.LoadAssetAtPath<AnimationClip>(
                        repairOutputPath);
                EditorGUIUtility.PingObject(Selection.activeObject);
                ShowNotification(
                    new GUIContent(
                        L("已生成 ", "Created ") + clip.name));
            }
            catch (System.Exception exception)
            {
                lastMessages.Add(exception.Message);
                Debug.LogException(exception);
            }
        }

        private BakeSettings Settings()
        {
            return new BakeSettings
            {
                sourceRoot = sourceRoot,
                sourceClip = sourceClip,
                targetRoot = targetRoot,
                sourceReferenceTime = sourceReferenceTime,
                targetReferenceTime = targetReferenceTime,
                useScenePoseAsTargetReference =
                    useScenePoseAsTargetReference,
                sampleRate = sampleRate,
                copyHipsPosition = copyHipsPosition,
                useScenePoseAsSourceReference =
                    useScenePoseAsSourceReference,
                autoScaleHipsMotion = autoScaleHipsMotion,
                alignHipsMotionToTargetFacing =
                    alignHipsMotionToTargetFacing,
                alignTargetFacingToRoot = alignTargetFacingToRoot,
                zeroInitialHipsHorizontalPosition =
                    zeroInitialHipsHorizontalPosition,
                targetRotationOffset = targetRotationOffset,
                hipsMotionRotationOffset = hipsMotionRotationOffset,
                hipsMotionScale = hipsMotionScale,
                hipsPositionOffset = hipsPositionOffset,
                leftArmBendPlaneOffset = leftArmBendPlaneOffset,
                rightArmBendPlaneOffset = rightArmBendPlaneOffset,
                stabilizeSuddenFacingChanges =
                    stabilizeSuddenFacingChanges,
                suddenTurnThreshold = suddenTurnThreshold,
                skipInvalidFirstFrame = skipInvalidFirstFrame,
                outputPath = outputPath
            };
        }

        private string BuildAutomaticOutputPath()
        {
            var folder = string.IsNullOrWhiteSpace(outputFolder)
                ? "Assets/Retargeted"
                : outputFolder.Trim().TrimEnd('/', '\\');
            return folder + "/x" + FormatNumber(targetRotationOffset.x) +
                   "_y" + FormatNumber(targetRotationOffset.y) +
                   "_z" + FormatNumber(targetRotationOffset.z) + ".anim";
        }

        private static string FormatNumber(float value)
        {
            if (Mathf.Abs(value) < 0.0001f)
                value = 0f;
            return value.ToString(
                "0.##",
                CultureInfo.InvariantCulture);
        }

        private void SaveDefaultInputs()
        {
            SaveObjectId(DefaultSourceRootKey, sourceRoot);
            SaveObjectId(DefaultSourceClipKey, sourceClip);
            SaveObjectId(DefaultTargetRootKey, targetRoot);
            ShowNotification(new GUIContent("Default inputs saved"));
        }

        private void LoadDefaultInputs()
        {
            sourceRoot = LoadObjectId<GameObject>(
                DefaultSourceRootKey,
                sourceRoot);
            sourceClip = LoadObjectId<AnimationClip>(
                DefaultSourceClipKey,
                sourceClip);
            targetRoot = LoadObjectId<GameObject>(
                DefaultTargetRootKey,
                targetRoot);
        }

        private static void SaveObjectId(string key, Object value)
        {
            if (value == null)
            {
                EditorPrefs.DeleteKey(key);
                return;
            }
            var id = GlobalObjectId.GetGlobalObjectIdSlow(value);
            EditorPrefs.SetString(key, id.ToString());
        }

        private static T LoadObjectId<T>(string key, T fallback)
            where T : Object
        {
            var text = EditorPrefs.GetString(key, string.Empty);
            if (string.IsNullOrEmpty(text) ||
                !GlobalObjectId.TryParse(text, out var id))
                return fallback;
            return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as T
                   ?? fallback;
        }

        private static string GetObjectId(Object value)
        {
            return value == null
                ? string.Empty
                : GlobalObjectId.GetGlobalObjectIdSlow(value).ToString();
        }

        private static T ResolveObjectId<T>(string text, T fallback)
            where T : Object
        {
            if (string.IsNullOrEmpty(text) ||
                !GlobalObjectId.TryParse(text, out var id))
                return fallback;
            return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as T
                   ?? fallback;
        }

        private void SaveLastBakeSnapshot()
        {
            var snapshot = new LastBakeSnapshot
            {
                sourceRootId = GetObjectId(sourceRoot),
                sourceClipId = GetObjectId(sourceClip),
                targetRootId = GetObjectId(targetRoot),
                sourceReferenceTime = sourceReferenceTime,
                targetReferenceTime = targetReferenceTime,
                useScenePoseAsTargetReference =
                    useScenePoseAsTargetReference,
                sampleRate = sampleRate,
                copyHipsPosition = copyHipsPosition,
                useScenePoseAsSourceReference =
                    useScenePoseAsSourceReference,
                autoScaleHipsMotion = autoScaleHipsMotion,
                alignHipsMotionToTargetFacing =
                    alignHipsMotionToTargetFacing,
                alignTargetFacingToRoot = alignTargetFacingToRoot,
                zeroInitialHipsHorizontalPosition =
                    zeroInitialHipsHorizontalPosition,
                targetRotationOffset = targetRotationOffset,
                hipsMotionRotationOffset = hipsMotionRotationOffset,
                hipsMotionScale = hipsMotionScale,
                hipsPositionOffset = hipsPositionOffset,
                leftArmBendPlaneOffset = leftArmBendPlaneOffset,
                rightArmBendPlaneOffset = rightArmBendPlaneOffset,
                sourceAnimationAlreadyRepaired =
                    sourceAnimationAlreadyRepaired,
                stabilizeSuddenFacingChanges =
                    stabilizeSuddenFacingChanges,
                suddenTurnThreshold = suddenTurnThreshold,
                skipInvalidFirstFrame = skipInvalidFirstFrame,
                autoNameFromRotation = autoNameFromRotation,
                outputFolder = outputFolder,
                outputPath = outputPath
            };
            EditorPrefs.SetString(
                LastBakeSnapshotKey,
                JsonUtility.ToJson(snapshot));
        }

        private void LoadLastBakeSnapshot()
        {
            var json = EditorPrefs.GetString(
                LastBakeSnapshotKey,
                string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                ShowNotification(new GUIContent("No previous Bake found"));
                return;
            }

            var snapshot = JsonUtility.FromJson<LastBakeSnapshot>(json);
            sourceRoot = ResolveObjectId(snapshot.sourceRootId, sourceRoot);
            sourceClip = ResolveObjectId(snapshot.sourceClipId, sourceClip);
            targetRoot = ResolveObjectId(snapshot.targetRootId, targetRoot);
            sourceReferenceTime = snapshot.sourceReferenceTime;
            targetReferenceTime = snapshot.targetReferenceTime;
            useScenePoseAsTargetReference =
                snapshot.useScenePoseAsTargetReference;
            sampleRate = snapshot.sampleRate;
            copyHipsPosition = snapshot.copyHipsPosition;
            useScenePoseAsSourceReference =
                snapshot.useScenePoseAsSourceReference;
            autoScaleHipsMotion = snapshot.autoScaleHipsMotion;
            alignHipsMotionToTargetFacing =
                snapshot.alignHipsMotionToTargetFacing;
            alignTargetFacingToRoot = snapshot.alignTargetFacingToRoot;
            zeroInitialHipsHorizontalPosition =
                snapshot.zeroInitialHipsHorizontalPosition;
            targetRotationOffset = snapshot.targetRotationOffset;
            hipsMotionRotationOffset = snapshot.hipsMotionRotationOffset;
            hipsMotionScale = snapshot.hipsMotionScale;
            hipsPositionOffset = snapshot.hipsPositionOffset;
            leftArmBendPlaneOffset = snapshot.leftArmBendPlaneOffset;
            rightArmBendPlaneOffset = snapshot.rightArmBendPlaneOffset;
            sourceAnimationAlreadyRepaired =
                snapshot.sourceAnimationAlreadyRepaired;
            stabilizeSuddenFacingChanges =
                snapshot.stabilizeSuddenFacingChanges;
            suddenTurnThreshold = snapshot.suddenTurnThreshold;
            skipInvalidFirstFrame = snapshot.skipInvalidFirstFrame;
            if (sourceAnimationAlreadyRepaired)
            {
                useScenePoseAsSourceReference = true;
                alignTargetFacingToRoot = false;
                zeroInitialHipsHorizontalPosition = false;
                stabilizeSuddenFacingChanges = false;
                skipInvalidFirstFrame = false;
            }
            autoNameFromRotation = snapshot.autoNameFromRotation;
            outputFolder = snapshot.outputFolder;
            outputPath = snapshot.outputPath;
            ShowNotification(new GUIContent("Last Bake restored"));
        }

        private void ValidateMapping()
        {
            lastMessages.Clear();
            var errors = SplitRigBaker.Validate(Settings());
            lastMessages.AddRange(errors);
            if (errors.Count > 0)
                return;

            var resolved = RetargetingProfile.Resolve(
                sourceRoot.transform,
                targetRoot.transform,
                sourceClip,
                out var warnings);
            var transformCurves = 0;
            foreach (var binding in AnimationUtility.GetCurveBindings(sourceClip))
            {
                if (binding.type == typeof(Transform))
                    transformCurves++;
            }
            lastMessages.Add(
                "Complete clip: " + transformCurves +
                " transform curves; matched " + resolved.Count +
                " target bones. No manual curve merge is required.");
            lastMessages.AddRange(warnings);
        }

        private void Bake()
        {
            lastMessages.Clear();
            try
            {
                var clip = SplitRigBaker.Bake(Settings(), out var warnings);
                SaveLastBakeSnapshot();
                lastMessages.AddRange(warnings);
                Selection.activeObject =
                    AssetDatabase.LoadAssetAtPath<AnimationClip>(outputPath);
                EditorGUIUtility.PingObject(Selection.activeObject);
                Debug.Log(
                    "Retargeted clip created: " +
                    AssetDatabase.GetAssetPath(Selection.activeObject));
                ShowNotification(
                    new GUIContent("Created " + clip.name));
            }
            catch (System.Exception exception)
            {
                lastMessages.Add(exception.Message);
                Debug.LogException(exception);
            }
        }
    }
}
