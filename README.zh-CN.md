# VMD Converter Window

[English](README.md)

Unity 2022.3+ 的 VMD 动作、镜头和 VPD 转换窗口，不依赖 VRCSDK。

## 安装与升级

将本仓库作为 Unity 包安装，或将完整目录放到 `Packages/com.verafirefly.vmdcw`。内置解析可独立使用；Core 集成要求 **VMDCC 1.0.0 或更新兼容版本**，请同时升级 VMDCC 包及其 Tools~ 运行文件。在 PMX 转换设置中选择 Core 及配套 EXE 路径。

通过 Unity 菜单 `VeraFirefly > VMD Converter > VMD Converter` 打开窗口。

## 动作输出

动作转换页直接导出 `.anim`，不生成元数据 `.asset`，默认保存到 `Assets/VmdConverter/OutPut`。在“输出位置”选择“自定义”后，可将 Assets 内的文件夹拖入“输出存储位置”。默认输出目录和 PMX 目录 `Assets/VmdConverter/PMX` 不存在时自动创建。

动作转换、内置解析调试和 VMDCC 调试提供 `humanoid-ik`、`target-humanoid`、`target-fk` 以及两种 target 的 `-all`／`-arm`／`-leg`，共九种输出模式。各模式均输出整身动作。Normal 校准目标根运动、躯干和下肢，上肢采用源 Humanoid 姿态；Leg 与 Normal 相同；All 包含目标上肢反解；Arm 对上肢反解并保留源 Humanoid 躯干、下肢。内置调试另提供通用 PMX 骨骼、下肢 FK＋其余 Humanoid 输出。

存在 VRCSDK 时可通过激活的 Avatar Descriptor 识别目标；否则在窗口中指定场景 GameObject 或预制体。目标输出需要有效的 Humanoid Animator。更换目标时重新加载形态键列表，未映射形态键按源名称写入 Body。

“重定向源骨架”可选择“内置”“手动选择”或“从文件夹”。内置使用包内 PMX 源骨架；手动选择时将 `.pmx` 拖入“指定目标(PMX骨架)”；从文件夹时将文件夹拖入“目标文件夹”，再从列表选择 PMX。默认文件夹为 `Assets/VmdConverter/PMX`，列表包含子文件夹内的 PMX。

“IK切换”位于“模型形态键映射”上方，按动作分组列出当前源 PMX 的 IK。每项默认“跟随 VMD”，也可选择“强制开启”或“强制关闭”；各动作、各源骨架分别保存选择。VMD 没有对应开关关键帧时，“跟随 VMD”默认开启。设置在转换时烘焙到 `.anim`，修改后需重新转换；源 VMD 不会被改写。

VPD 转换页提供相同的九种输出模式和源骨架选择，每个 VPD 导出一份 `.anim` 姿势。目标 Humanoid、FK 模式需要在“指定 Avatar”中提供有效的 Humanoid；普通 Humanoid 模式下此字段可选，用于匹配形态键。默认输出目录、文件夹拖入和自动创建规则与动作页相同。

## 求解方式

镜头转换默认启用“精简关键帧”：按 30 FPS 采样后精简曲线，保留原始关键帧、切镜及投影切换。相对逐帧曲线，每个位置分量误差限为 `0.00005` Unity 单位、四元数分量为 `0.00001`、视角为 `0.005°`。关闭此选项可恢复逐帧输出；精简主要减少动画体积，仍需完成原有采样。

- 内置解析：在进程内准备 PMX/VMD 和 IK，通过 Unity HumanPoseHandler／目标 Transform 烘焙动画。
- Core 默认：EXE 准备 PMX/IK 原始姿态，Unity 校验缓存后烘焙最终 Humanoid 或目标 FK 曲线。
- 全部托管调试选项：EXE 使用 HPS 离线生成 `.anim`，再由 Unity 导入。此路径的结果可能与 Unity 原生烘焙不同。

PMX 设置可生成单行控制台命令。离线 target 模式必须指定 FBX 或 HPS rig；Humanoid IK 无目标时使用 PMX rig，有目标时整体重定向，不隐式启用目标全身重解。目标下肢自动拟合膝、踝方向及足部姿态。

## 上肢插值

`BakeOptions.UpperTangentError` 控制上肢肌肉曲线的三次插值，默认 `0.01`，范围 `0..0.05` 肌肉单位；`0` 保留线性插值。此预算在曲线删减后计算，不等于最终骨骼姿态误差。根与腿保持原插值。全部托管导出会将此设置传递给 VMDCC。

插值本身不能解决 Unity Humanoid 在完整轴向自转中的旋转分支限制。

## 脚本接口

使用 `VmdBatchConversionSession`、`VmdConversionSettings` 和 `VmdClipOutput` 批量转换；`ConvertPreparedPoseToClip` 可读取外部 PMX 姿态。目标范围由 `BakeOptions.TargetSolveScope` 与 `FkOptions.TargetSolveScope` 设置。Unity 场景、Avatar、曲线及资源操作必须在 Editor 主线程执行。Core 外部接口及构建见 [VMDCC 文档](https://github.com/Vera-Firefly/VMD-Converter-Core)。

目录结构：`Editor/Model~` 存放内置 PMX 参考骨架及许可说明，`Tools~` 存放离线生成脚本；均从磁盘读取，不需要 Unity 元数据。Core 程序使用 VMDCC 的 `Tools~/VmdConverterCore.exe`。
