# SpinePreviewer

独立 Unity 2022.3.62f2 预览工程，使用相对路径复用当前项目的 Spine-Unity 4.1.43、Spine-URP 4.1.27 和 URP 14.0.12。

运行参数：

```text
SpinePreviewer.exe --input <resource-directory>
```

同一目录内按文件名识别多个资源，动态生成资源切换按钮；同名 JSON / SKEL 合并为一个资源，加载时只尝试当前资源的备用格式。每个骨架优先使用同名 Atlas，目录只有一个 Atlas 时允许共用；多份 Atlas 且无法匹配时会提示错误。文件新增、删除与修改会刷新列表，并尽量保留当前资源选择。

预览器支持 JSON / SKEL 动态加载、SkeletonAnimation / SkeletonGraphic、动画和 Skin 切换、速度调整、实时背景颜色、棋盘背景、无限制滚轮缩放、资源拖动和目录热重载。控制面板、历史目录列表和右下角事件日志会拦截鼠标输入，不会移动镜头。事件日志默认展开，关闭/打开状态写入 PlayerPrefs；最近 20 个资源目录与 SpineTool 共用 `%LOCALAPPDATA%/IdleGearDefence/SpineTool/preview-history.json`。

骨骼点检查区域支持输入任意骨骼名，也会根据 `bullet`、`muzzle`、`fire`、`shoot`、`weapon`、`attack`、`projectile`、`emit`、`launch`、`cast` 等关键词列出常见发射点候选。选择后会显示一个跟随动画和拖动的红色标记，并同时显示 Spine 局部坐标、Spine 世界坐标和预览世界坐标。

Windows 构建：

- 独立包目录：`Tools/SpinePreviewer/Builds/SpinePreviewer-win64/`
- 压缩包：`Tools/SpinePreviewer/Builds/SpinePreviewer-win64.zip`
- SpineTool 直接调用副本：`Tools/SpineTool/bin/Release/net8.0-windows/SpinePreviewer.exe`

当前工程只提供最小场景和运行时加载器；最终 EXE 使用 Unity 2022.3.62f2 构建。
