using System.Text.Json;
using System.Text.Json.Serialization;

namespace IdleGearDefence.SpineExporter;

public sealed class ExporterConfig
{
    public int SchemaVersion { get; set; }

    public ToolchainConfig Toolchain { get; set; } = new();

    public ExportProfile Export { get; set; } = new();

    [JsonIgnore]
    public string ConfigPath { get; internal set; } = string.Empty;

    [JsonIgnore]
    public string ConfigDirectory => Path.GetDirectoryName(ConfigPath) ?? Directory.GetCurrentDirectory();

    public ExportProfile GetExportSettings()
    {
        Export ??= new ExportProfile();
        Export.Validate();
        return Export;
    }
}

public sealed class ToolchainConfig
{
    public string SpineExecutable { get; set; } = string.Empty;

    public string RequiredSpineVersion { get; set; } = string.Empty;

    public string StagingRoot { get; set; } = "staging";

    public bool KeepStagingOnSuccess { get; set; }

    public bool KeepStagingOnFailure { get; set; } = true;

    public string ResolveStagingRoot(string configDirectory)
    {
        if (string.IsNullOrWhiteSpace(StagingRoot))
            throw new ExportConfigurationException("toolchain.stagingRoot 不能为空。");

        return Path.GetFullPath(Path.IsPathRooted(StagingRoot)
            ? StagingRoot
            : Path.Combine(configDirectory, StagingRoot));
    }
}

public sealed class ExportProfile
{
    public string JsonExportSettingsFile { get; set; } = string.Empty;

    public string BinaryExportSettingsFile { get; set; } = string.Empty;

    // Compatibility fallback for a profile that emits only one data format.
    public string ExportSettingsFile { get; set; } = string.Empty;

    public TexturePackerConfig TexturePacker { get; set; } = new();

    public OutputPolicy Output { get; set; } = new();

    public bool EmitJson { get; set; } = true;

    public bool EmitBinary { get; set; } = true;

    public bool RequireAtlas { get; set; } = true;

    public bool RequireTextures { get; set; } = true;

    public bool CleanAnimation { get; set; }

    public bool RequirePremultipliedAlpha { get; set; }

    public string ResolveExportSettings(string configDirectory, bool binary)
    {
        var configuredPath = binary
            ? (string.IsNullOrWhiteSpace(BinaryExportSettingsFile) ? ExportSettingsFile : BinaryExportSettingsFile)
            : (string.IsNullOrWhiteSpace(JsonExportSettingsFile) ? ExportSettingsFile : JsonExportSettingsFile);
        var fieldName = binary ? "binaryExportSettingsFile" : "jsonExportSettingsFile";
        return ResolveRequiredFile(configuredPath, configDirectory, fieldName);
    }

    public string? ResolvePackSettings(string configDirectory)
    {
        if (TexturePacker is null || string.IsNullOrWhiteSpace(TexturePacker.SettingsFile))
            return null;

        return ResolveRequiredFile(TexturePacker.SettingsFile, configDirectory, "texturePacker.settingsFile");
    }

    public void Validate()
    {
        if (EmitJson && string.IsNullOrWhiteSpace(JsonExportSettingsFile) && string.IsNullOrWhiteSpace(ExportSettingsFile))
            throw new ExportConfigurationException("导出设置未配置 JSON Export Settings JSON。");

        if (EmitBinary && string.IsNullOrWhiteSpace(BinaryExportSettingsFile) && string.IsNullOrWhiteSpace(ExportSettingsFile))
            throw new ExportConfigurationException("导出设置未配置 Binary Export Settings JSON。");

        if (Output is null)
            throw new ExportConfigurationException("导出设置未配置 output。");

        Output.Validate();

        if (TexturePacker is null)
            throw new ExportConfigurationException("导出设置未配置 texturePacker。");

        TexturePacker.Validate();
    }

    private static string ResolveRequiredFile(string configuredPath, string configDirectory, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            throw new ExportConfigurationException($"{fieldName} 不能为空。");

        var path = Path.GetFullPath(Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(configDirectory, configuredPath));

        if (!File.Exists(path))
            throw new ExportConfigurationException($"{fieldName} 文件不存在：{path}");

        return path;
    }
}

public sealed class TexturePackerConfig
{
    /// <summary>
    /// EmbeddedAttachments uses Spine's official data-export packing mode. It packs only
    /// images referenced by attachments in the project, so unused files in images/ are ignored.
    /// Embedded is kept as a backwards-compatible alias for EmbeddedAttachments.
    /// </summary>
    public string Mode { get; set; } = "EmbeddedAttachments";

    public string SettingsFile { get; set; } = string.Empty;

    public string InputDirectory { get; set; } = string.Empty;

    public string? Name { get; set; }

    public bool UsesEmbeddedAttachments =>
        string.Equals(Mode, "EmbeddedAttachments", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Mode, "Embedded", StringComparison.OrdinalIgnoreCase);

    public void Validate()
    {
        if (!UsesEmbeddedAttachments
            && !string.Equals(Mode, "Separate", StringComparison.OrdinalIgnoreCase))
        {
            throw new ExportConfigurationException(
                $"texturePacker.mode 必须是 EmbeddedAttachments 或 Separate，实际为：{Mode}。");
        }

        if (string.Equals(Mode, "Separate", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(SettingsFile))
        {
            throw new ExportConfigurationException("使用 Separate 打包时必须配置 texturePacker.settingsFile。");
        }
    }
}

public sealed class OutputPolicy
{
    public string OutputSubdirectoryTemplate { get; set; } = "{resourceName}";

    public string AtlasExtension { get; set; } = ".atlas.txt";

    public string BinaryExtension { get; set; } = ".skel.bytes";

    public bool AllowOverwrite { get; set; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(OutputSubdirectoryTemplate))
            throw new ExportConfigurationException("output.outputSubdirectoryTemplate 不能为空。");

        if (OutputSubdirectoryTemplate.Contains("{resourceType}", StringComparison.OrdinalIgnoreCase))
            throw new ExportConfigurationException("output.outputSubdirectoryTemplate 不支持 {resourceType}，请改用 {resourceName}。");

        if (!AtlasExtension.StartsWith(".", StringComparison.Ordinal)
            || (!AtlasExtension.Equals(".atlas", StringComparison.OrdinalIgnoreCase)
                && !AtlasExtension.Equals(".atlas.txt", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ExportConfigurationException(
                "output.atlasExtension 必须是 .atlas 或 .atlas.txt。");
        }

        if (!BinaryExtension.Equals(".skel.bytes", StringComparison.OrdinalIgnoreCase))
            throw new ExportConfigurationException(
                "output.binaryExtension 当前必须是 .skel.bytes。");
    }
}

public static class ExporterConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static ExporterConfig Load(string configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath))
            throw new ExportConfigurationException("配置文件路径不能为空。");

        var fullPath = Path.GetFullPath(configPath);
        if (!File.Exists(fullPath))
            throw new ExportConfigurationException($"配置文件不存在：{fullPath}");

        try
        {
            var json = File.ReadAllText(fullPath);
            var config = JsonSerializer.Deserialize<ExporterConfig>(json, JsonOptions)
                         ?? throw new ExportConfigurationException("配置文件内容为空。");
            config.ConfigPath = fullPath;

            if (config.SchemaVersion != 2)
                throw new ExportConfigurationException($"不支持的配置 schemaVersion：{config.SchemaVersion}，当前仅支持 2；旧的资源类型 Profile 配置需要重新创建。");

            if (config.Toolchain is null)
                throw new ExportConfigurationException("配置缺少 toolchain。");

            if (config.Export is null)
                throw new ExportConfigurationException("配置缺少 export 设置。");

            config.Export.Validate();

            return config;
        }
        catch (JsonException exception)
        {
            throw new ExportConfigurationException($"配置文件 JSON 无法解析：{fullPath}", exception);
        }
    }
}

public sealed class ExportConfigurationException : Exception
{
    public ExportConfigurationException(string message)
        : base(message)
    {
    }

    public ExportConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
