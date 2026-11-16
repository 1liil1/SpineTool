using System.IO;
using IdleGearDefence.SpineValidator;

namespace IdleGearDefence.SpineTool;

public sealed class SpineTaskCard
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public string SpineProjectPath { get; set; } = string.Empty;
    public string ProjectGroup { get; set; } = "齿轮项目";
    public List<string> Tags { get; set; } = new();
    public string OutputRootPath { get; set; } = string.Empty;
    public string ExportConfigId { get; set; } = string.Empty;
    public string ValidationRuleId { get; set; } = string.Empty;
    public string ConfigPairId { get; set; } = string.Empty;
    public ConfigReference ExportConfig { get; set; } = new();
    public ConfigReference ValidationRules { get; set; } = new();
    public bool IsPinned { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime LastUsedAt { get; set; } = DateTime.MinValue;
    public DateTime? LastExportAt { get; set; }
    public DateTime? LastProjectWriteTimeUtc { get; set; }
    public DateTime? LastPreviewAt { get; set; }
    public DateTime? LastValidationAt { get; set; }
    public bool? LastValidationPassed { get; set; }
    public int LastExportArtifactCount { get; set; }

    public SpineTaskCard Clone()
    {
        return new SpineTaskCard
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = Name + " 副本",
            Note = Note,
            SpineProjectPath = SpineProjectPath,
            ProjectGroup = ProjectGroup,
            Tags = Tags?.ToList() ?? new List<string>(),
            OutputRootPath = OutputRootPath,
            ExportConfigId = ExportConfigId,
            ValidationRuleId = ValidationRuleId,
            ConfigPairId = ConfigPairId,
            ExportConfig = ExportConfig.Clone(),
            ValidationRules = ValidationRules.Clone(),
            IsPinned = false,
            CreatedAt = DateTime.Now,
            LastUsedAt = DateTime.MinValue
        };
    }
}

public sealed class ExportConfigCard
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime LastUsedAt { get; set; } = DateTime.MinValue;
}

public sealed class ValidationRuleCard
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime LastUsedAt { get; set; } = DateTime.MinValue;
}

public sealed class ConfigPairCard
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public string ExportConfigId { get; set; } = string.Empty;
    public string ValidationRuleId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime LastUsedAt { get; set; } = DateTime.MinValue;
}

public sealed class ProjectCardStoreData
{
    public List<SpineTaskCard> Projects { get; set; } = new();
    public List<ExportConfigCard> ExportConfigs { get; set; } = new();
    public List<ValidationRuleCard> ValidationRules { get; set; } = new();
    public List<ConfigPairCard> ConfigPairs { get; set; } = new();
}

public sealed class PreviewHistoryEntry
{
    public string Path { get; set; } = string.Empty;
    public DateTime LastUsedAt { get; set; } = DateTime.MinValue;
}

public sealed class PreviewHistoryStoreData
{
    public List<PreviewHistoryEntry> Items { get; set; } = new();
}

public sealed class ConfigReference
{
    public string DisplayName { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;

    public ConfigReference Clone() => new() { DisplayName = DisplayName, Note = Note, Path = Path };
}

public sealed class BatchValidationRow
{
    public DiscoveredSpineResource Resource { get; init; } = new();
    public ValidationReport? Report { get; init; }
    public string? ErrorMessage { get; init; }
    public string Status => ErrorMessage != null ? "错误" : Report?.Passed == true ? "通过" : "失败";
    public int BlockerCount => Report?.Issues.Count(x => x.Severity == ValidationSeverity.Blocker) ?? 1;
    public int WarningCount => Report?.Issues.Count(x => x.Severity == ValidationSeverity.Warning) ?? 0;
}

public sealed class CardStore
{
    private readonly string _path;
    private readonly string _applicationDirectory;

    public CardStore(string? path = null, string? applicationDirectory = null)
    {
        _applicationDirectory = System.IO.Path.GetFullPath(applicationDirectory ?? AppContext.BaseDirectory);
        _path = path ?? System.IO.Path.Combine(GetDataDirectory(_applicationDirectory), "cards.json");
    }

    public string Path => _path;
    public string DataDirectory => System.IO.Path.GetDirectoryName(_path)!;

    public static string GetDataDirectory(string? applicationDirectory = null)
    {
        var root = applicationDirectory ?? AppContext.BaseDirectory;
        return File.Exists(System.IO.Path.Combine(root, "portable.flag"))
            ? System.IO.Path.Combine(root, "UserData")
            : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IdleGearDefence", "SpineTool");
    }

    public ProjectCardStoreData LoadData()
    {
        if (!File.Exists(_path)) return PrepareData(new ProjectCardStoreData());
        try
        {
            var json = File.ReadAllText(_path);
            ProjectCardStoreData? data = null;
            try { data = System.Text.Json.JsonSerializer.Deserialize<ProjectCardStoreData>(json, JsonOptions); }
            catch (System.Text.Json.JsonException) { }
            if (data?.Projects != null) return PrepareData(data);
            var legacy = System.Text.Json.JsonSerializer.Deserialize<List<SpineTaskCard>>(json, JsonOptions) ?? new List<SpineTaskCard>();
            var migrated = new ProjectCardStoreData { Projects = legacy };
            foreach (var project in migrated.Projects)
            {
                if (project.ExportConfig != null && !string.IsNullOrWhiteSpace(project.ExportConfig.Path))
                {
                    var config = new ExportConfigCard { Name = project.ExportConfig.DisplayName, Note = project.ExportConfig.Note, Path = project.ExportConfig.Path };
                    migrated.ExportConfigs.Add(config);
                    project.ExportConfigId = config.Id;
                }
                if (project.ValidationRules != null && !string.IsNullOrWhiteSpace(project.ValidationRules.Path))
                {
                    var rule = new ValidationRuleCard { Name = project.ValidationRules.DisplayName, Note = project.ValidationRules.Note, Path = project.ValidationRules.Path };
                    migrated.ValidationRules.Add(rule);
                    project.ValidationRuleId = rule.Id;
                }
            }
            return PrepareData(migrated);
        }
        catch (Exception)
        {
            try
            {
                var broken = _path + ".broken." + DateTime.Now.ToString("yyyyMMddHHmmss");
                File.Move(_path, broken, overwrite: true);
            }
            catch { }
            return PrepareData(new ProjectCardStoreData());
        }
    }

    private ProjectCardStoreData PrepareData(ProjectCardStoreData data)
    {
        BuiltInConfigurations.Ensure(data, _applicationDirectory);
        foreach (var card in data.Projects)
        {
            // Old paired selections are only used to fill missing independent references.
            var pair = data.ConfigPairs.FirstOrDefault(x => x.Id == card.ConfigPairId);
            if (string.IsNullOrEmpty(card.ExportConfigId)) card.ExportConfigId = pair?.ExportConfigId ?? string.Empty;
            if (string.IsNullOrEmpty(card.ValidationRuleId)) card.ValidationRuleId = pair?.ValidationRuleId ?? string.Empty;
            if (!data.ExportConfigs.Any(x => x.Id == card.ExportConfigId) && !string.IsNullOrWhiteSpace(card.ExportConfig?.Path))
            {
                var config = data.ExportConfigs.FirstOrDefault(x => x.Path == card.ExportConfig.Path);
                if (config == null)
                {
                    config = new ExportConfigCard { Name = card.ExportConfig.DisplayName, Note = card.ExportConfig.Note, Path = card.ExportConfig.Path };
                    data.ExportConfigs.Add(config);
                }
                card.ExportConfigId = config.Id;
            }
            if (!data.ValidationRules.Any(x => x.Id == card.ValidationRuleId) && !string.IsNullOrWhiteSpace(card.ValidationRules?.Path))
            {
                var rule = data.ValidationRules.FirstOrDefault(x => x.Path == card.ValidationRules.Path);
                if (rule == null)
                {
                    rule = new ValidationRuleCard { Name = card.ValidationRules.DisplayName, Note = card.ValidationRules.Note, Path = card.ValidationRules.Path };
                    data.ValidationRules.Add(rule);
                }
                card.ValidationRuleId = rule.Id;
            }
        }
        TransformConfigPaths(data, path => string.IsNullOrWhiteSpace(path) ? path : System.IO.Path.GetFullPath(path, _applicationDirectory));
        return data;
    }

    private static void TransformConfigPaths(ProjectCardStoreData data, Func<string, string> transform)
    {
        foreach (var config in data.ExportConfigs) config.Path = transform(config.Path);
        foreach (var rule in data.ValidationRules) rule.Path = transform(rule.Path);
        foreach (var card in data.Projects)
        {
            card.ExportConfig ??= new ConfigReference();
            card.ValidationRules ??= new ConfigReference();
            card.ExportConfig.Path = transform(card.ExportConfig.Path);
            card.ValidationRules.Path = transform(card.ValidationRules.Path);
        }
    }

    public List<SpineTaskCard> Load() => LoadData().Projects;

    public void Save(IEnumerable<SpineTaskCard> cards)
        => SaveData(new ProjectCardStoreData { Projects = cards.ToList() });

    public void SaveData(ProjectCardStoreData data)
    {
        var directory = System.IO.Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temp = _path + ".tmp";
        var snapshot = System.Text.Json.JsonSerializer.Deserialize<ProjectCardStoreData>(System.Text.Json.JsonSerializer.Serialize(data, JsonOptions), JsonOptions)!;
        TransformConfigPaths(snapshot, path =>
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            var fullPath = System.IO.Path.GetFullPath(path, _applicationDirectory);
            var relative = System.IO.Path.GetRelativePath(_applicationDirectory, fullPath);
            return relative != ".." && !relative.StartsWith(".." + System.IO.Path.DirectorySeparatorChar) && !System.IO.Path.IsPathRooted(relative) ? relative : fullPath;
        });
        File.WriteAllText(temp, System.Text.Json.JsonSerializer.Serialize(snapshot, JsonOptions));
        File.Move(temp, _path, overwrite: true);
    }

    public static string GetPreviewHistoryPath() => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IdleGearDefence", "SpineTool", "preview-history.json");

    public static List<PreviewHistoryEntry> LoadPreviewHistory(string? path = null)
    {
        var fullPath = path ?? GetPreviewHistoryPath();
        if (!File.Exists(fullPath)) return new List<PreviewHistoryEntry>();
        try
        {
            var json = File.ReadAllText(fullPath);
            try
            {
                var wrapped = System.Text.Json.JsonSerializer.Deserialize<PreviewHistoryStoreData>(json, JsonOptions);
                if (wrapped?.Items != null) return wrapped.Items;
            }
            catch (System.Text.Json.JsonException) { }
            return System.Text.Json.JsonSerializer.Deserialize<List<PreviewHistoryEntry>>(json, JsonOptions)
                   ?? new List<PreviewHistoryEntry>();
        }
        catch { return new List<PreviewHistoryEntry>(); }
    }

    public static void SavePreviewHistory(IEnumerable<PreviewHistoryEntry> entries, string? path = null)
    {
        var fullPath = path ?? GetPreviewHistoryPath();
        var directory = System.IO.Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        var temp = fullPath + ".tmp";
        File.WriteAllText(temp, System.Text.Json.JsonSerializer.Serialize(new PreviewHistoryStoreData { Items = entries.Take(20).ToList() }, JsonOptions));
        File.Move(temp, fullPath, overwrite: true);
    }

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
}
