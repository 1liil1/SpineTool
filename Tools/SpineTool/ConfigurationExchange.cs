using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using IdleGearDefence.SpineExporter;
using IdleGearDefence.SpineValidator;

namespace IdleGearDefence.SpineTool;

/// <summary>Single-file sharing, including the settings referenced by an export profile.</summary>
public static class ConfigurationExchange
{
    private const string Format = "SpineTool.Configuration";
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static void ExportSettings(ExportConfigCard card, string destination)
        => Write(destination, ReadExport(card.Path, card.Name, card.Note));

    public static void ExportRules(ValidationRuleCard card, string destination)
    {
        new ValidatorRulesLoader().Load(card.Path);
        Write(destination, Envelope("validation", card.Name, card.Note, ReadObject(card.Path)));
    }

    public static ExportConfigCard ImportSettings(string source, string storageDirectory)
    {
        var package = ReadObject(source);
        if (package["format"] is null)
            package = ReadExport(source, Path.GetFileNameWithoutExtension(source), string.Empty);
        ValidateEnvelope(package, "export");
        var profile = package["profile"]?.DeepClone().AsObject() ?? throw new InvalidDataException("分享文件缺少导出配置。");
        var export = profile["export"]?.AsObject() ?? throw new InvalidDataException("分享文件缺少 export。");
        var id = Guid.NewGuid().ToString("N");
        var directory = Path.Combine(storageDirectory, "Exports", id);
        Directory.CreateDirectory(directory);
        try
        {
            RestoreDependency("jsonSettings", "jsonExportSettingsFile", "json.json");
            RestoreDependency("binarySettings", "binaryExportSettingsFile", "binary.json");
            export["exportSettingsFile"] = string.Empty;
            var pack = export["texturePacker"]?.AsObject() ?? throw new InvalidDataException("缺少 texturePacker。");
            pack["settingsFile"] = string.Empty;
            if (package["packSettings"] is JsonObject packSettings)
            {
                Write(Path.Combine(directory, "pack.json"), packSettings);
                pack["settingsFile"] = "pack.json";
            }
            // The recipient always uses their own local Spine installation and staging directory.
            var toolchain = profile["toolchain"]?.AsObject() ?? throw new InvalidDataException("缺少 toolchain。");
            toolchain["spineExecutable"] = string.Empty;
            toolchain["stagingRoot"] = "staging";
            var path = Path.Combine(directory, "exporter.json");
            Write(path, profile);
            var restored = ExporterConfigLoader.Load(path);
            if (restored.Export.EmitJson) restored.Export.ResolveExportSettings(directory, false);
            if (restored.Export.EmitBinary) restored.Export.ResolveExportSettings(directory, true);
            restored.Export.ResolvePackSettings(directory);
            return new ExportConfigCard { Id = id, Name = ReadName(package, source), Note = package["note"]?.GetValue<string>() ?? "", Path = path };
        }
        catch
        {
            Directory.Delete(directory, true);
            throw;
        }

        void RestoreDependency(string key, string field, string fileName)
        {
            export[field] = string.Empty;
            if (package[key] is not JsonObject node) return;
            Write(Path.Combine(directory, fileName), node);
            export[field] = fileName;
        }
    }

    public static ValidationRuleCard ImportRules(string source, string storageDirectory)
    {
        var package = ReadObject(source);
        if (package["format"] is null)
        {
            new ValidatorRulesLoader().Load(source);
            package = Envelope("validation", Path.GetFileNameWithoutExtension(source), string.Empty, package);
        }
        ValidateEnvelope(package, "validation");
        var rules = package["profile"] as JsonObject ?? throw new InvalidDataException("分享文件缺少验证规则。");
        if (rules["settings"] is not JsonObject) throw new InvalidDataException("验证规则缺少 settings。");
        var id = Guid.NewGuid().ToString("N");
        var directory = Path.Combine(storageDirectory, "Rules", id);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "rules.json");
        try
        {
            Write(path, rules);
            new ValidatorRulesLoader().Load(path);
            return new ValidationRuleCard { Id = id, Name = ReadName(package, source), Note = package["note"]?.GetValue<string>() ?? "", Path = path };
        }
        catch { Directory.Delete(directory, true); throw; }
    }

    public static ExportConfigCard CopySettings(ExportConfigCard card, string storageDirectory)
    {
        var temporary = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var source = ExporterConfigLoader.Load(card.Path);
            ExportSettings(card, temporary);
            var copy = ImportSettings(temporary, storageDirectory);
            // A local copy retains per-profile overrides; sharing still uses the recipient's installation.
            var profile = ReadObject(copy.Path);
            if (!string.IsNullOrWhiteSpace(source.Toolchain.SpineExecutable))
                profile["toolchain"]!["spineExecutable"] = Path.GetFullPath(source.Toolchain.SpineExecutable, source.ConfigDirectory);
            profile["export"]!["texturePacker"]!["inputDirectory"] = source.Export.TexturePacker.InputDirectory;
            Write(copy.Path, profile);
            return copy;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static ValidationRuleCard CopyRules(ValidationRuleCard card, string storageDirectory)
    {
        var temporary = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        try { ExportRules(card, temporary); return ImportRules(temporary, storageDirectory); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static JsonObject ReadExport(string path, string name, string note)
    {
        var config = ExporterConfigLoader.Load(path);
        var profile = ReadObject(path);
        var package = Envelope("export", name, note, profile);
        if (config.Export.EmitJson) package["jsonSettings"] = ReadObject(config.Export.ResolveExportSettings(config.ConfigDirectory, false));
        if (config.Export.EmitBinary) package["binarySettings"] = ReadObject(config.Export.ResolveExportSettings(config.ConfigDirectory, true));
        var pack = config.Export.ResolvePackSettings(config.ConfigDirectory);
        if (pack != null) package["packSettings"] = ReadObject(pack);
        profile["toolchain"]!["spineExecutable"] = string.Empty;
        profile["toolchain"]!["stagingRoot"] = "staging";
        // Dependency paths are assigned on import, never taken from the sending computer.
        var export = profile["export"]!;
        export["jsonExportSettingsFile"] = "json.json";
        export["binaryExportSettingsFile"] = "binary.json";
        export["exportSettingsFile"] = string.Empty;
        export["texturePacker"]!["settingsFile"] = pack == null ? "" : "pack.json";
        var input = export["texturePacker"]!["inputDirectory"]?.GetValue<string>();
        if (input != null && Path.IsPathRooted(input)) export["texturePacker"]!["inputDirectory"] = "{imagesDirectory}";
        return package;
    }

    private static JsonObject Envelope(string kind, string name, string note, JsonObject profile)
        => new() { ["format"] = Format, ["version"] = 1, ["kind"] = kind, ["name"] = name, ["note"] = note, ["profile"] = profile };

    private static void ValidateEnvelope(JsonObject node, string kind)
    {
        if (node["format"]?.GetValue<string>() != Format || node["version"]?.GetValue<int>() != 1)
            throw new InvalidDataException("不支持的配置分享格式或版本。");
        if (node["kind"]?.GetValue<string>() != kind)
            throw new InvalidDataException(kind == "export" ? "这是验证规则文件，请在验证规则页面导入。" : "这是导出配置文件，请在导出配置页面导入。");
    }

    private static string ReadName(JsonObject node, string source)
        => string.IsNullOrWhiteSpace(node["name"]?.GetValue<string>()) ? Path.GetFileNameWithoutExtension(source) : node["name"]!.GetValue<string>();

    private static JsonObject ReadObject(string path)
        => JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip })?.AsObject()
           ?? throw new InvalidDataException("配置 JSON 不能为空。");

    private static void Write(string path, JsonObject node) => File.WriteAllText(path, node.ToJsonString(Options));
}
