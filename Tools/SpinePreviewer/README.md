# SpinePreviewer

独立 Unity 2022.3.62f2 预览工程，使用相对路径复用当前项目的 Spine-Unity 4.1.43、Spine-URP 4.1.27 和 URP 14.0.12。

运行参数：

```text
SpinePreviewer.exe --input <resource-directory>
```

选择资源文件夹时递归扫描全部子文件夹，按相对路径生成资源切换按钮，可输入名称或子目录筛选。同名 JSON / SKEL 合并，跨目录同名资源保持独立。每个资源仅在自己的目录匹配同名 Atlas，目录只有一个 Atlas 时允许共用；子目录的 preview-manifest.json 也按该目录解析。目录新增、删除或修改会自动刷新；跳过不可读目录和链接目录，防止扫描循环。

动画按钮显示时长，底部时间轴显示当前时间 / 总时长、百分比和事件标记。点击或拖动进度条实时定位并暂停，支持前后拖动和动画结束点；点击播放恢复循环。◆ 按真实事件时间排列，点击定位，悬停查看参数。旧右下角事件日志已移除。

支持 SkeletonAnimation / SkeletonGraphic 两种模式、Skin、播放速度、实时背景、棋盘、无限制缩放、拖动及骨骼标记。控制面板和时间轴拦截鼠标操作，拖动时间轴不会移动画面。

骨骼点检查区域支持输入任意骨骼名，也会根据 `bullet`、`muzzle`、`fire`、`shoot`、`weapon`、`attack`、`projectile`、`emit`、`launch`、`cast` 等关键词列出常见发射点候选。选择后会显示一个跟随动画和拖动的红色标记，并同时显示 Spine 局部坐标、Spine 世界坐标和预览世界坐标。

Windows 构建：

- 独立包目录：`Tools/SpinePreviewer/Builds/SpinePreviewer-win64/`
- 压缩包：`Tools/SpinePreviewer/Builds/SpinePreviewer-win64.zip`
- SpineTool 直接调用副本：`Tools/SpineTool/bin/Release/net8.0-windows/SpinePreviewer.exe`

当前工程只提供最小场景和运行时加载器；最终 EXE 使用 Unity 2022.3.62f2 构建。
