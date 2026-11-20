# Changelog / 更新记录

## Unreleased

- 修复编辑器清理未使用资源或重新打开窗口后，动作形态键映射可能持续报空引用的问题。
- Fixed repeated null-reference errors in motion morph mappings after editor asset cleanup or reopening the window.

- 动作页新增按动作分组的“IK切换”，根据源 PMX 列出 IK，支持跟随 VMD、强制开启、强制关闭，并应用于内置及 Core 转换。
- Added per-motion IK switches from the source PMX, with Follow VMD, Force On and Force Off options for built-in and Core conversion.

- 镜头转换新增默认启用的关键帧精简，按误差限减少冗余曲线，保留原始关键帧、切镜及投影切换；可关闭以逐帧输出。
- Added camera keyframe reduction, enabled by default, with bounded curve error and preserved source keys, cuts and projection switches. It can be disabled for per-frame output.

## 1.1.0 — 2026-11-17

- VPD 转换页对齐动作页的九种输出模式、Avatar 与源骨架选择、默认输出目录及分隔布局。
- Aligned VPD conversion with the motion page's nine output modes, Avatar and source skeleton selectors, default output folder and section separators.

- 默认输出目录改为 `Assets/VmdConverter/OutPut`，自定义输出支持文件夹拖入；自动创建默认输出与 PMX 文件夹，转换设置三块之间增加分隔线。
- Changed the default output folder to `Assets/VmdConverter/OutPut`, added folder drop fields for custom output, automatic default folder creation and separators between conversion settings.

- 动作转换页固定直接导出动画，移除转换方案及元数据存储位置选项。
- The motion page now always exports animations directly. Removed the conversion scheme and metadata storage selectors.

- 修复手动指定预制体 Avatar 后转换仍被禁用的问题；重定向源骨架新增内置、手动选择及从文件夹三种选择方式。
- Fixed conversion remaining disabled for manually selected prefab Avatars. Source skeletons can now be selected from the built-in rig, a PMX asset or a folder list.

- 默认 PMX 改用 YYB 式 Miku 骨架；生成工具从原模型提取骨骼及 IK 设置。
- Updated the default PMX to the YYB Miku skeleton; the generator extracts bones and IK from the source model.

- 移除镜头转换的预览器应用入口及 MMDPMP 集成；语言设置改为独立保存。
- Removed camera assignment to the previewer and MMDPMP integration. Language settings are stored independently.

- 修复窗口高度较小或停靠时，动作、镜头及 VPD 页的转换按钮消失的问题；转换条件不满足时仍显示禁用按钮。
- Fixed conversion buttons disappearing in short or docked motion, camera and VPD windows. Buttons remain visible when conversion is unavailable.

## 1.0.0 — 2026-11-14

- 修复上肢旋转连续性，减少手臂翻转、卡顿和关节扭曲。
- 新增可选上肢三次插值，`UpperTangentError` 默认 `0.01`，设为 `0` 保留线性插值。
- Core 集成要求 VMDCC 1.0.0 或更新兼容版本，并向离线导出传递插值设置；内置解析仍可独立使用。

- Fixed arm rotation continuity to reduce flips, stuttering and joint distortion.
- Added upper-muscle cubic interpolation. `UpperTangentError` defaults to `0.01`; use `0` for linear interpolation.
- Core integration requires VMDCC 1.0.0 or a later compatible version and forwards interpolation settings to offline export. The built-in parser works independently.
