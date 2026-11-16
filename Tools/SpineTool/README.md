# SpineTool

WPF 整合界面，调用通用 Exporter 和 Validator。导出配置与验证规则各自管理，Spine 工程任务卡片通过两个独立下拉框分别选择。旧版组合选择会自动迁移。

导出配置与验证规则均可单独导入 / 导出分享文件。导出设置分享文件包含所引用的 JSON、Binary 和图集设置；导入后保存为独立的本地配置，不依赖发送方路径。

纹理打包页按 Spine 的区域、区域填充、页面、运行时、输出、选项分区，顶部单独提供缩放、预乘 Alpha、溢出、能被 4 整除和正方形。预览器按资源名匹配同目录的骨架和 Atlas，通过动态资源按钮切换，同名 JSON / SKEL 作为备用格式。

功能包括任务卡片、Tag/所属项目筛选、可拖动侧栏宽度、输出根目录历史、打开当前资源输出目录和批量验证。SpineTool 主界面暂不显示预览历史，独立 Previewer 继续保留自己的历史。

美术使用说明见 [使用说明.md](使用说明.md)。发布包包含 .NET 8 Windows Desktop Runtime、Unity 2022.3.62f2 预览器及“无”“齿轮士兵”两套内置配置。

发布目录放置 `portable.flag` 时，任务卡片、Spine 程序路径与输出目录历史保存到旁边的 `UserData`。没有该标记时，继续使用原有 LocalAppData 存储。配置引用保存为相对路径，适合整包移动；首次启动自动补齐内置配置。

构建：

```powershell
dotnet build Tools/SpineTool/SpineTool.csproj --configuration Release
```
