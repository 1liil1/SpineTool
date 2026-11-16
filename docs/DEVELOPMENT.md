# 开发与构建

## 环境

- Windows 10 / 11 x64，.NET 8 SDK（WPF / Windows Desktop）。`global.json` 允许较新 SDK。
- Unity 2022.3.62f2，安装 Windows Build Support。
- 正式导出需要本机激活的 Spine 4.1。自动验证程序不需要编辑器。

仓库的 `Packages/Spine`、`Packages/Spine-URP` 是预览器和验证器依赖，不能只复制 `Tools`。第三方源码保留许可与 GUID。

## 主工具

```powershell
dotnet build Tools/SpineTool/SpineTool.csproj --configuration Release
```

运行 `Tools/SpineTool/bin/Release/net8.0-windows/SpineTool.exe`。开发版预览可设置 `SPINE_PREVIEWER_EXE` 指向已构建的预览器，或生成完整便携包。

## 自动检查

```powershell
pwsh -File scripts/Test.ps1
```

依次运行 Exporter、Validator、WPF/配置分享/资源目录自动验证。它们是 Console 检查程序，使用 `dotnet run` 而非 `dotnet test`。GitHub Actions 在 Windows 上使用同一入口。

## Unity 预览器

通过 Unity Hub 打开 `Tools/SpinePreviewer`，等待恢复 URP 依赖，打开 `Assets/Scenes/Previewer.unity`。

命令行构建：

```powershell
& 'C:/Program Files/Unity/Hub/Editor/2022.3.62f2/Editor/Unity.exe' `
  -batchmode -quit -projectPath "$PWD/Tools/SpinePreviewer" `
  -executeMethod PreviewBuild.BuildWindows `
  -previewOutput "$PWD/artifacts/previewer/SpinePreviewer.exe" `
  -logFile "$PWD/artifacts/previewer-build.log"
```

Unity 构建需要有效许可。CI 只检查 .NET 与目录发现逻辑，不假设有 Unity 编辑器授权。

## Windows 完整便携包

```powershell
pwsh -File scripts/Build-Windows.ps1 -UnityEditor 'D:/Unity/Editor/Unity.exe' -Version '0.1.0'
```

已构建预览器时可用 `-SkipPreviewerBuild -PreviewerDirectory <完整构建目录>`。输出 `artifacts/SpineTool-Win64-0.1.0.zip` 与 SHA256 文件，程序包含 .NET 运行库，两套配置和预览器。

```text
SpineTool-Win64-0.1.0/
  App/
    SpineTool.exe
    config/
    portable.flag
    Previewer/SpinePreviewer.exe
  Licenses/
  使用说明.md
  请先阅读.txt
```

直接运行 EXE，不包含 CMD 启动器或桌面快捷方式创建脚本。

## 模块职责

- `SpineTool`：界面、卡片、配置编辑、分享、调用流程。
- `SpineExporter`：CLI、staging、产物归一化，不负责动画规范。
- `SpineValidator`：Runtime 解析与规则检查，不修改美术工程。
- `SpinePreviewer`：运行时加载、显示、骨骼标记、事件与交互。

配置 schema 为 2，分享封装为 1。旧组合只用于迁移，现有卡片分别引用导出配置与验证规则。

不要提交缓存、个人 UserData、Spine 激活信息、游戏资源或导出产物。第三方改动注明来源和原因。
