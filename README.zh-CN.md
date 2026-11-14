# VMD Converter Window

[English](README.md)

Unity 2022.3+ 的 VMD 动作、镜头和 VPD 转换窗口，不依赖 VRCSDK。

## 安装与升级

将本仓库作为 Unity 包安装，或将完整目录放到 `Packages/com.verafirefly.vmdcw`。内置解析可独立使用；Core 集成要求 **VMDCC 1.0.0 或更新兼容版本**，请同时升级 VMDCC 包及其 Tools 运行文件。在 PMX 转换设置中选择 Core 及配套 EXE 路径。

通过 Unity 菜单 `VeraFirefly > VMD Converter > VMD Converter` 打开窗口。

## 动作输出

动作转换、内置解析调试和 VMDCC 调试提供 `humanoid-ik`、`target-humanoid`、`target-fk` 以及两种 target 的 `-all`／`-arm`／`-leg`，共九种输出模式。各模式均输出整身动作。Normal 校准目标根运动、躯干和下肢，上肢采用源 Humanoid 姿态；Leg 与 Normal 相同；All 包含目标上肢反解；Arm 对上肢反解并保留源 Humanoid 躯干、下肢。内置调试另提供通用 PMX 骨骼、下肢 FK＋其余 Humanoid 输出。

存在 VRCSDK 时可通过激活的 Avatar Descriptor 识别目标；否则在窗口中指定 GameObject。更换目标时重新加载形态键列表，未映射形态键按源名称写入 Body。

## 求解方式

- 内置解析：在进程内准备 PMX/VMD 和 IK，通过 Unity HumanPoseHandler／目标 Transform 烘焙动画。
- Core 默认：EXE 准备 PMX/IK 原始姿态，Unity 校验缓存后烘焙最终 Humanoid 或目标 FK 曲线。
- 全部托管调试选项：EXE 使用 HPS 离线生成 `.anim`，再由 Unity 导入。此路径的结果可能与 Unity 原生烘焙不同。

PMX 设置可生成单行控制台命令。离线 target 模式必须指定 FBX 或 HPS rig；Humanoid IK 无目标时使用 PMX rig，有目标时整体重定向，不隐式启用目标全身重解。目标下肢自动拟合膝、踝方向及足部姿态。

## 上肢插值

`BakeOptions.UpperTangentError` 控制上肢肌肉曲线的三次插值，默认 `0.01`，范围 `0..0.05` 肌肉单位；`0` 保留线性插值。此预算在曲线删减后计算，不等于最终骨骼姿态误差。根与腿保持原插值。全部托管导出会将此设置传递给 VMDCC。

插值本身不能解决 Unity Humanoid 在完整轴向自转中的旋转分支限制。

## 脚本接口

使用 `VmdBatchConversionSession`、`VmdConversionSettings` 和 `VmdClipOutput` 批量转换；`ConvertPreparedPoseToClip` 可读取外部 PMX 姿态。目标范围由 `BakeOptions.TargetSolveScope` 与 `FkOptions.TargetSolveScope` 设置。Unity 场景、Avatar、曲线及资源操作必须在 Editor 主线程执行。Core 外部接口及构建见 [VMDCC 文档](https://github.com/Vera-Firefly/VMD-Converter-Core)。
