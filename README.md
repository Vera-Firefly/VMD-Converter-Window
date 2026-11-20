# VMD Converter Window

[简体中文](README.zh-CN.md)

Unity 2022.3+ Editor window for VMD motion, camera and VPD conversion. VRCSDK is optional.

## Install and upgrade

Install this repository as a Unity package, or copy its complete contents to `Packages/com.verafirefly.vmdcw`. The built-in parser works independently of VMDCC. Core integration requires **VMDCC 1.0.0 or a later compatible version**; upgrade the Core package and its complete Tools~ runtime together. Select Core and the matching executable path in PMX conversion settings.

Open `VeraFirefly > VMD Converter > VMD Converter` from the Unity menu.

## Motion output

The motion page exports `.anim` directly without metadata `.asset` files, defaulting to `Assets/VmdConverter/OutPut`. Select Custom under Output Location, then drag a folder from Assets into Output Storage Location. The default output folder and `Assets/VmdConverter/PMX` folder are created automatically when missing.

The motion page, built-in debug page and Core debug page list nine output modes: `humanoid-ik`, `target-humanoid`, `target-fk`, and the `-all`, `-arm`, `-leg` variants of each target mode. All modes export whole-body motion. Normal and leg modes fit target root, torso and legs while keeping the source Humanoid upper limb; all includes target upper-limb reconstruction; arm reconstructs target arms while retaining source body and legs. Built-in debugging also offers generic PMX bones and lower-body FK with a Humanoid remainder.

When VRCSDK is present, an active Avatar Descriptor can identify the target. Otherwise select a scene GameObject or prefab in the window. Target output requires a valid Humanoid Animator. Changing the target reloads morph mappings; unmapped morphs retain their source name under Body.

Retarget Source Skeleton offers Built-in, Manual and From Folder. Built-in uses the bundled PMX skeleton. Manual accepts a `.pmx` in Select Target (PMX Skeleton). From Folder accepts a project folder in Target Folder and lists its PMX files, including subfolders. The default folder is `Assets/VmdConverter/PMX`.

IK Switching appears above Model BlendShape Mapping and groups the selected PMX's IK chains by motion. Each entry defaults to Follow VMD, with Force On and Force Off overrides; selections are saved separately for each motion and source skeleton. Follow VMD defaults to enabled when no matching switch key exists. Settings are baked into the `.anim`; convert again after changing them. Source VMD files are preserved.

The VPD page offers the same nine output modes and source skeleton choices, exporting one pose `.anim` per VPD. Target Humanoid and FK modes require a valid Humanoid in Avatar GameObject. For standard Humanoid output, the Avatar is optional and supplies morph mappings. Output folders, folder drop fields and automatic default folder creation follow the motion page.

## Conversion routes

Camera conversion enables Reduce Keyframes by default. It samples at 30 FPS, then reduces curves while preserving source keys, cuts and projection switches. Relative to the per-frame curves, tolerances are `0.00005` Unity units per position component, `0.00001` per quaternion component and `0.005°` for field of view. Disable it for per-frame output. Reduction primarily saves animation size; sampling is still required.

- Built-in: prepare PMX/VMD and IK in-process, then bake through Unity HumanPoseHandler or target Transforms.
- Core default: the EXE prepares raw PMX/IK poses; Unity validates the cache and bakes final Humanoid or target FK curves.
- Fully managed debug option: EXE and HPS generate `.anim` offline; Unity imports it. This route can differ from native Unity baking.

PMX settings can generate a single-line CLI command. Offline target modes require an explicit FBX or HPS rig; Humanoid IK can use a PMX rig without a target. An explicit target retargets the source pose without implicitly enabling target whole-body reconstruction. Automatic target-leg fitting uses knee/ankle directions and foot orientation.

## Upper-limb interpolation

`BakeOptions.UpperTangentError` controls upper-muscle cubic interpolation. It defaults to `0.01` muscle units, accepts `0..0.05`, and uses `0` for linear interpolation. This budget applies after curve reduction and is not a final bone-pose error bound. Root and leg interpolation is preserved. Fully managed export forwards this setting to VMDCC.

Interpolation alone does not resolve Unity Humanoid's rotation-branch limitations for complete axial turns.

## Scripting

Use `VmdBatchConversionSession`, `VmdConversionSettings` and `VmdClipOutput` for batch conversion; `ConvertPreparedPoseToClip` consumes external PMX poses. Set target regions through `BakeOptions.TargetSolveScope` and `FkOptions.TargetSolveScope`. Run Unity scene, Avatar, curve and asset operations on the Editor main thread. See the [VMDCC manual](https://github.com/Vera-Firefly/VMD-Converter-Core) for external APIs and builds.

Package layout: `Editor/Model~` holds the bundled PMX reference rig and its notices; `Tools~` holds the offline generator. Both are read from disk without Unity metadata. The Core executable is resolved from VMDCC's `Tools~/VmdConverterCore.exe`.
