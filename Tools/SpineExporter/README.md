# SpineExporter

Exporter 读取一份通用导出配置，调用 Spine CLI，将 JSON、SKEL、Atlas 和 PNG 归一化到 `<输出根目录>/<资源名>`。配置 schema 为 2，导出配置和官方 Export Settings 与具体资源类型无关。

```powershell
dotnet run --project Tools/SpineExporter/SpineExporter.csproj -- `
  --config Tools/SpineExporter/config/SpineExporter.json `
  --project D:/art/Monster.spine `
  --output D:/art/output
```

批处理使用 `Batch.sample.json`，每项只需要工程路径和输出根目录。旧的按资源类型 Profile 配置不会自动迁移，需要重新创建 schema 2 配置。
