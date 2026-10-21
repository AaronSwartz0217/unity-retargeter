# Split Rig to Mixamo Retargeter

Editor-only animation baker for Unity 2022.3. It samples a split humanoid
hierarchy in world space and writes a new AnimationClip for a continuous Mixamo
skeleton. It does not modify the source hierarchy or either imported model.

## GLB import

Unity 2022.3 does not include `.glb` in its built-in model import list. Install a
glTF importer before copying a GLB into `Assets`.

Recommended: Unity glTFast (`com.unity.cloud.gltfast`). Install a release that
supports Unity 2022.3 through **Window > Package Manager > + > Add package by
name**, using:

`com.unity.cloud.gltfast`

If that package is unavailable in the selected China registry, either add the
official Unity registry for that package, install glTFast from its official Git
repository, or convert the GLB to FBX externally. The retargeter has no compile
time dependency on the importer: it only needs the resulting scene hierarchy
and AnimationClip.

## Install this package

In Unity Package Manager, choose **Add package from git URL** and enter:

`git@github.com:AaronSwartz0217/unity-retargeter.git`

The machine running Unity must have SSH access to GitHub. For a public clone,
the HTTPS URL can also be used:

`https://github.com/AaronSwartz0217/unity-retargeter.git`

For a local installation, copy this folder under the project's `Packages`
directory:

`Packages/com.codex.split-rig-retargeter`

Alternatively use Package Manager's **Add package from disk** and select
`package.json`.

## Workflow

1. Import the complex animated GLB with glTFast as the source asset.
2. Import the Mixamo FBX separately as the target asset. In the FBX Rig settings, keeping it Generic is
   sufficient for this baker; Humanoid is optional.
3. Put one instance of each asset in the same scene.
4. Open **Tools > Split Rig Retargeter**.
5. Assign the imported GLB instance to Source Root.
6. Assign its animation to Source Clip.
7. Assign the Mixamo instance to Mixamo Target Root.
8. Start with both reference times at `0`.
9. Set sample rate to `60`, or `120` to preserve all samples in the supplied GLB.
10. Validate, then bake.

The source and target are expected to be two separately imported assets. Do not
merge their hierarchies or animation clips before using the baker.

## Supplied-file mapping notes

- `_1:LeftShoulder`, `_1:RightShoulder`, and `_1:Neck` are region/armature
  objects. Their transforms intentionally map to the Mixamo shoulder and neck
  bones.
- Source `Spine1` maps to target `Spine2`. Target `Spine1` remains in its
  reference pose in version 0.1.
- Finger segments 1-3 map directly. Mixamo segment 4 is an end bone and is not
  animated.
- Hips translation can be scaled automatically from hips-to-head height.

## Version 0.2

- Bone matching now prefers transforms actually driven by the selected source
  clip. This avoids choosing an unrelated controller or duplicate bone name.
- Validation reports the number of transform curves in the complete source clip.

## Current limitations

- The source and target reference poses must be reasonably similar.
- Spine distribution, foot locking, IK contact correction, curve reduction,
  and visual error reporting are planned improvements.
- If the GLB importer splits one glTF animation into multiple unrelated
  AnimationClips rather than one hierarchy-wide clip, combine or re-export the
  animation first. Unity glTFast normally preserves a glTF animation at the
  hierarchy level.
