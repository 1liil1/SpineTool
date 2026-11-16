using System.IO;

namespace IdleGearDefence.SpineTool;

public static class BuiltInConfigurations
{
    public const string NonePairId = "builtin-none";

    public static void Ensure(ProjectCardStoreData data, string applicationDirectory)
    {
        Add(data, applicationDirectory, NonePairId, "无", "SpineExporter.json", "Rules.None.json", "默认导出，无验证条件。");
        Add(data, applicationDirectory, "builtin-gear-soldier", "齿轮士兵", "SpineExporter.GearSoldier.json", "Rules.GearSoldier.json",
            "预乘 Alpha；attack 动画必须有 attack 事件；缺少 BulletPostion 仅输出警告。");
    }

    private static void Add(ProjectCardStoreData data, string root, string id, string name, string exportFile, string rulesFile, string note)
    {
        var exportPath = Path.Combine("config", exportFile);
        var rulesPath = Path.Combine("config", rulesFile);
        if (!File.Exists(Path.Combine(root, exportPath)) || !File.Exists(Path.Combine(root, rulesPath))) return;
        var pair = data.ConfigPairs.FirstOrDefault(x => x.Id == id) ?? data.ConfigPairs.FirstOrDefault(x => x.Name == name);
        if (pair == null)
        {
            pair = new ConfigPairCard { Id = id, Name = name, Note = note };
            data.ConfigPairs.Add(pair);
        }
        var export = data.ExportConfigs.FirstOrDefault(x => x.Id == pair.ExportConfigId);
        if (export == null)
        {
            export = new ExportConfigCard { Id = id + "-export", Name = name, Note = note };
            data.ExportConfigs.Add(export);
            pair.ExportConfigId = export.Id;
        }
        var rule = data.ValidationRules.FirstOrDefault(x => x.Id == pair.ValidationRuleId);
        if (rule == null)
        {
            rule = new ValidationRuleCard { Id = id + "-rules", Name = name, Note = note };
            data.ValidationRules.Add(rule);
            pair.ValidationRuleId = rule.Id;
        }
        if (string.IsNullOrWhiteSpace(export.Path)) export.Path = exportPath;
        if (string.IsNullOrWhiteSpace(rule.Path)) rule.Path = rulesPath;
        if (export.Note == note)
            export.Note = name == "无" ? "默认 JSON / Binary 导出；Attachments 打包。" : "Attachments 打包；预乘 Alpha（PMA）。";
        if (rule.Note == note)
            rule.Note = name == "无" ? "关闭验证，无动画、事件或骨骼限制。" : "必须有 attack 动画及 attack 事件；缺少 BulletPostion 仅输出警告。";
    }
}
