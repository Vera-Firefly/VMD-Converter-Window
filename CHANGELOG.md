# Changelog / 更新记录

## Unreleased

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
