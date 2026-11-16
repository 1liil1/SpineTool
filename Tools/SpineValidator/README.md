# SpineValidator

Validator 读取一份通用规则配置，对单个资源目录或批量资源目录执行检查。规则 schema 为 2，不再按资源类型选择 Profile。

```powershell
dotnet run --project Tools/SpineValidator/SpineValidator.csproj -- `
  --input D:/art/output/Monster_1001 `
  --rules Tools/SpineValidator/config/Rules.json `
  --report D:/art/reports/Monster_1001.json
```

批量模式的每项只包含 `input` 和 `report`。旧的按资源类型 Profile 规则不会自动迁移，需要重新创建 schema 2 规则文件。
