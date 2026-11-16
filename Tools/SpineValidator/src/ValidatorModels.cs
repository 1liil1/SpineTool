using System.Text.Json;
using System.Text.Json.Serialization;

namespace IdleGearDefence.SpineValidator;

public sealed class ValidatorRules
{
    public int SchemaVersion { get; set; }

    public bool Enabled { get; set; } = true;

    public bool ShowFailureDialog { get; set; }

    public ValidatorProfile Settings { get; set; } = new();

    [JsonIgnore]
    public string RulesPath { get; internal set; } = string.Empty;

    public ValidatorProfile Resolve() => (Settings ?? new ValidatorProfile()).Clone();
}

public sealed class ValidatorProfile
{
    public List<string> RequiredAnimations { get; set; }

    public List<string> RequiredBones { get; set; }

    public List<string> WarningBones { get; set; }

    public List<string> RequiredSlots { get; set; }

    public List<AnimationEventRule> Events { get; set; }

    public string DataVersion { get; set; }

    public string NamingPattern { get; set; }

    public bool? RequireJson { get; set; }

    public bool? RequireBinary { get; set; }

    public bool? RequireAtlas { get; set; }

    public bool? RequireTextures { get; set; }

    public ValidatorProfile Clone()
    {
        return new ValidatorProfile
        {
            RequiredAnimations = RequiredAnimations?.ToList(),
            RequiredBones = RequiredBones?.ToList(),
            WarningBones = WarningBones?.ToList(),
            RequiredSlots = RequiredSlots?.ToList(),
            Events = Events?.Select(x => x with { }).ToList(),
            DataVersion = DataVersion,
            NamingPattern = NamingPattern,
            RequireJson = RequireJson,
            RequireBinary = RequireBinary,
            RequireAtlas = RequireAtlas,
            RequireTextures = RequireTextures
        };
    }

    public void Merge(ValidatorProfile overlay)
    {
        if (overlay.RequiredAnimations is not null) RequiredAnimations = overlay.RequiredAnimations.ToList();
        if (overlay.RequiredBones is not null) RequiredBones = overlay.RequiredBones.ToList();
        if (overlay.WarningBones is not null) WarningBones = overlay.WarningBones.ToList();
        if (overlay.RequiredSlots is not null) RequiredSlots = overlay.RequiredSlots.ToList();
        if (overlay.Events is not null) Events = overlay.Events.Select(x => x with { }).ToList();
        if (overlay.DataVersion is not null) DataVersion = overlay.DataVersion;
        if (overlay.NamingPattern is not null) NamingPattern = overlay.NamingPattern;
        if (overlay.RequireJson.HasValue) RequireJson = overlay.RequireJson;
        if (overlay.RequireBinary.HasValue) RequireBinary = overlay.RequireBinary;
        if (overlay.RequireAtlas.HasValue) RequireAtlas = overlay.RequireAtlas;
        if (overlay.RequireTextures.HasValue) RequireTextures = overlay.RequireTextures;
    }
}

public sealed record AnimationEventRule(
    string Animation,
    string EventName,
    int MinCount = 1,
    int? MaxCount = null,
    float? ExpectedTime = null,
    float TimeTolerance = 0.001f);

public sealed class ValidationReport
{
    public string ResourceName { get; init; } = string.Empty;

    public bool Passed => Issues.All(x => x.Severity != ValidationSeverity.Blocker);

    public List<ValidationIssue> Issues { get; } = new();

    public List<ValidationFileInfo> Files { get; } = new();

    public void Add(string code, ValidationSeverity severity, string message, string file = null)
    {
        Issues.Add(new ValidationIssue(code, severity, message, file));
    }
}

public sealed record ValidationIssue(
    string Code,
    ValidationSeverity Severity,
    string Message,
    string File);

public sealed record ValidationFileInfo(
    string Kind,
    string Path,
    long Size,
    string Sha256);

public enum ValidationSeverity
{
    Info,
    Warning,
    Blocker
}

public sealed class ValidatorRulesLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public ValidatorRules Load(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new ValidatorConfigurationException($"规则文件不存在：{fullPath}");

        try
        {
            var rules = JsonSerializer.Deserialize<ValidatorRules>(File.ReadAllText(fullPath), JsonOptions)
                        ?? throw new ValidatorConfigurationException("规则文件为空。");
            if (rules.SchemaVersion != 2)
                throw new ValidatorConfigurationException($"不支持的规则 schemaVersion：{rules.SchemaVersion}，当前仅支持 2；旧的资源类型 Profile 规则需要重新创建。");
            if (rules.Settings is null)
                throw new ValidatorConfigurationException("规则文件缺少 settings 设置。");
            rules.RulesPath = fullPath;
            return rules;
        }
        catch (JsonException exception)
        {
            throw new ValidatorConfigurationException($"规则文件无法解析：{fullPath}", exception);
        }
    }
}

public sealed class ValidatorConfigurationException : Exception
{
    public ValidatorConfigurationException(string message) : base(message) { }

    public ValidatorConfigurationException(string message, Exception innerException) : base(message, innerException) { }
}
