using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using IdleGearDefence.SpineExporter;

namespace IdleGearDefence.SpineTool;

public sealed class SpineVisualSettings
{
    public bool JsonNonessential { get; set; } = true;
    public bool JsonPrettyPrint { get; set; } = true;
    public bool BinaryNonessential { get; set; } = true;
    public bool PremultiplyAlpha { get; set; }
    public bool MultipleOfFour { get; set; }
    public bool Square { get; set; }
    public bool PowerOfTwo { get; set; } = true;
    public int MinWidth { get; set; } = 16;
    public int MinHeight { get; set; } = 16;
    public int MaxWidth { get; set; } = 4096;
    public int MaxHeight { get; set; } = 4096;
    public float Scale { get; set; } = 1f;
    public int PaddingX { get; set; } = 2;
    public int PaddingY { get; set; } = 2;
    public bool EdgePadding { get; set; } = true;
    public bool DuplicatePadding { get; set; }
    public bool Rotation { get; set; } = true;
    public bool StripWhitespaceX { get; set; }
    public bool StripWhitespaceY { get; set; }
    public bool Bleed { get; set; }
    public bool Alias { get; set; } = true;
    public bool IgnoreBlankImages { get; set; }
    public int AlphaThreshold { get; set; }
    public bool FlattenPaths { get; set; }
    public bool UseIndexes { get; set; }
    public bool Debug { get; set; }
    public bool Fast { get; set; }
    public bool PrettyPrint { get; set; }
    public string Packing { get; set; } = "rectangles";
    public string PixelFormat { get; set; } = "RGBA8888";
    public string AtlasExtension { get; set; } = ".atlas.txt";
    public bool LimitMemory { get; set; } = true;
    public string FilterMin { get; set; } = "Linear";
    public string FilterMag { get; set; } = "Linear";
    public string WrapX { get; set; } = "ClampToEdge";
    public string WrapY { get; set; } = "ClampToEdge";

    public static SpineVisualSettings Load(string exporterConfigPath)
    {
        var config = ExporterConfigLoader.Load(exporterConfigPath);
        var settings = new SpineVisualSettings();
        var packPath = config.Export.ResolvePackSettings(config.ConfigDirectory);
        if (packPath is { Length: > 0 } && File.Exists(packPath)) ReadPackSettings(packPath, settings);
        // Embedded packing uses packAtlas in the data export settings as the source of truth.
        ReadJsonSettings(config, settings);
        settings.AtlasExtension = config.Export.Output.AtlasExtension;
        settings.Scale = Math.Max(0.01f, settings.Scale);
        settings.MaxWidth = Math.Max(16, settings.MaxWidth);
        settings.MaxHeight = Math.Max(16, settings.MaxHeight);
        return settings;
    }

    public static void Save(string exporterConfigPath, SpineVisualSettings settings)
    {
        if (settings.MaxWidth < 16 || settings.MaxHeight < 16) throw new InvalidDataException("Atlas 最大宽高不能小于 16。");
        if (settings.MinWidth < 1 || settings.MinHeight < 1 || settings.MinWidth > settings.MaxWidth || settings.MinHeight > settings.MaxHeight)
            throw new InvalidDataException("页面最小宽高必须大于 0，且不能超过最大宽高。");
        if (settings.AlphaThreshold < 0 || settings.AlphaThreshold > 255) throw new InvalidDataException("Alpha 阈值必须在 0～255 之间。");
        if (settings.AtlasExtension != ".atlas" && settings.AtlasExtension != ".atlas.txt") throw new InvalidDataException("图集扩展名必须是 .atlas 或 .atlas.txt。");
        if (!float.IsFinite(settings.Scale) || settings.Scale <= 0) throw new InvalidDataException("图片缩放必须是大于 0 的有限数字。");
        if (settings.PaddingX < 0 || settings.PaddingY < 0) throw new InvalidDataException("Padding 不能小于 0。");
        var config = ExporterConfigLoader.Load(exporterConfigPath);
        var embeddedAttachments = config.Export.TexturePacker.UsesEmbeddedAttachments;
        if (config.Export.EmitJson) WriteJsonSettings(config.Export.ResolveExportSettings(config.ConfigDirectory, false), settings, binary: false, embeddedAttachments);
        if (config.Export.EmitBinary) WriteJsonSettings(config.Export.ResolveExportSettings(config.ConfigDirectory, true), settings, binary: true, embeddedAttachments);
        var packPath = config.Export.ResolvePackSettings(config.ConfigDirectory);
        if (!string.IsNullOrWhiteSpace(packPath)) WritePackSettings(packPath, settings);
        var configNode = JsonNode.Parse(File.ReadAllText(exporterConfigPath))?.AsObject()
                         ?? throw new InvalidDataException("导出配置 JSON 无法解析。");
        var exportNode = configNode["export"]?.AsObject() ?? throw new InvalidDataException("导出配置缺少 export。");
        exportNode["requirePremultipliedAlpha"] = settings.PremultiplyAlpha;
        exportNode["output"]!["atlasExtension"] = settings.AtlasExtension;
        File.WriteAllText(exporterConfigPath, configNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void ReadJsonSettings(ExporterConfig config, SpineVisualSettings settings)
    {
        var jsonPath = config.Export.EmitJson ? config.Export.ResolveExportSettings(config.ConfigDirectory, false) : null;
        var binaryPath = config.Export.EmitBinary ? config.Export.ResolveExportSettings(config.ConfigDirectory, true) : null;
        if (jsonPath != null)
        {
            settings.JsonNonessential = ReadBool(jsonPath, "nonessential", settings.JsonNonessential);
            settings.JsonPrettyPrint = ReadBool(jsonPath, "prettyPrint", settings.JsonPrettyPrint);
        }
        if (binaryPath != null) settings.BinaryNonessential = ReadBool(binaryPath, "nonessential", settings.BinaryNonessential);
        var dataPath = jsonPath ?? binaryPath;
        var jsonNode = dataPath == null ? null : JsonNode.Parse(File.ReadAllText(dataPath))?.AsObject();
        if (config.Export.TexturePacker.UsesEmbeddedAttachments && jsonNode?["packAtlas"] is JsonObject packAtlas)
            ReadPackSettings(packAtlas, settings);
    }

    private static void ReadPackSettings(string path, SpineVisualSettings settings)
    {
        var node = JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? throw new InvalidDataException("纹理打包配置 JSON 无法解析。");
        ReadPackSettings(node, settings);
    }

    private static void ReadPackSettings(JsonObject node, SpineVisualSettings settings)
    {
        settings.PremultiplyAlpha = ReadBool(node, "premultiplyAlpha", settings.PremultiplyAlpha);
        settings.MultipleOfFour = ReadBool(node, "multipleOfFour", settings.MultipleOfFour);
        settings.Square = ReadBool(node, "square", settings.Square);
        settings.PowerOfTwo = ReadBool(node, "pot", settings.PowerOfTwo);
        settings.MinWidth = ReadInt(node, "minWidth", settings.MinWidth);
        settings.MinHeight = ReadInt(node, "minHeight", settings.MinHeight);
        settings.MaxWidth = ReadInt(node, "maxWidth", settings.MaxWidth);
        settings.MaxHeight = ReadInt(node, "maxHeight", settings.MaxHeight);
        settings.Scale = ReadFloat(node, "scale", settings.Scale);
        settings.PaddingX = ReadInt(node, "paddingX", settings.PaddingX);
        settings.PaddingY = ReadInt(node, "paddingY", settings.PaddingY);
        settings.EdgePadding = ReadBool(node, "edgePadding", settings.EdgePadding);
        settings.DuplicatePadding = ReadBool(node, "duplicatePadding", settings.DuplicatePadding);
        settings.Rotation = ReadBool(node, "rotation", settings.Rotation);
        settings.StripWhitespaceX = ReadBool(node, "stripWhitespaceX", settings.StripWhitespaceX);
        settings.StripWhitespaceY = ReadBool(node, "stripWhitespaceY", settings.StripWhitespaceY);
        settings.Bleed = ReadBool(node, "bleed", settings.Bleed);
        settings.Alias = ReadBool(node, "alias", settings.Alias);
        settings.IgnoreBlankImages = ReadBool(node, "ignoreBlankImages", settings.IgnoreBlankImages);
        settings.AlphaThreshold = ReadInt(node, "alphaThreshold", settings.AlphaThreshold);
        settings.FlattenPaths = ReadBool(node, "flattenPaths", settings.FlattenPaths);
        settings.UseIndexes = ReadBool(node, "useIndexes", settings.UseIndexes);
        settings.Debug = ReadBool(node, "debug", settings.Debug);
        settings.Fast = ReadBool(node, "fast", settings.Fast);
        settings.PrettyPrint = ReadBool(node, "prettyPrint", settings.PrettyPrint);
        settings.Packing = ReadString(node, "packing", settings.Packing);
        settings.PixelFormat = ReadString(node, "format", settings.PixelFormat);
        settings.LimitMemory = ReadBool(node, "limitMemory", settings.LimitMemory);
        settings.FilterMin = ReadString(node, "filterMin", settings.FilterMin);
        settings.FilterMag = ReadString(node, "filterMag", settings.FilterMag);
        settings.WrapX = ReadString(node, "wrapX", settings.WrapX);
        settings.WrapY = ReadString(node, "wrapY", settings.WrapY);
    }

    private static void WriteJsonSettings(string path, SpineVisualSettings settings, bool binary, bool embeddedAttachments)
    {
        var node = JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? new JsonObject();
        node["nonessential"] = binary ? settings.BinaryNonessential : settings.JsonNonessential;
        if (!binary) node["prettyPrint"] = settings.JsonPrettyPrint;
        if (embeddedAttachments)
        {
            node["class"] = binary ? "export-binary" : "export-json";
            var packNode = node["packAtlas"] as JsonObject ?? new JsonObject();
            ApplyPackValues(packNode, settings);
            node["packAtlas"] = packNode;
            node["packSource"] = "attachments";
            node["packTarget"] = "perskeleton";
        }
        File.WriteAllText(path, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void WritePackSettings(string path, SpineVisualSettings settings)
    {
        var node = JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? new JsonObject();
        ApplyPackValues(node, settings);
        File.WriteAllText(path, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void ApplyPackValues(JsonObject node, SpineVisualSettings settings)
    {
        node["premultiplyAlpha"] = settings.PremultiplyAlpha;
        node["multipleOfFour"] = settings.MultipleOfFour;
        node["square"] = settings.Square;
        node["pot"] = settings.PowerOfTwo;
        node["minWidth"] = settings.MinWidth;
        node["minHeight"] = settings.MinHeight;
        node["maxWidth"] = settings.MaxWidth;
        node["maxHeight"] = settings.MaxHeight;
        node["scale"] = new JsonArray(JsonValue.Create(settings.Scale));
        node["paddingX"] = settings.PaddingX;
        node["paddingY"] = settings.PaddingY;
        node["edgePadding"] = settings.EdgePadding;
        node["duplicatePadding"] = settings.DuplicatePadding;
        node["rotation"] = settings.Rotation;
        node["stripWhitespaceX"] = settings.StripWhitespaceX;
        node["stripWhitespaceY"] = settings.StripWhitespaceY;
        node["bleed"] = settings.Bleed;
        node["alias"] = settings.Alias;
        node["ignoreBlankImages"] = settings.IgnoreBlankImages;
        node["alphaThreshold"] = settings.AlphaThreshold;
        node["flattenPaths"] = settings.FlattenPaths;
        node["useIndexes"] = settings.UseIndexes;
        node["debug"] = settings.Debug;
        node["fast"] = settings.Fast;
        node["prettyPrint"] = settings.PrettyPrint;
        node["packing"] = settings.Packing;
        node["format"] = settings.PixelFormat;
        node["atlasExtension"] = settings.AtlasExtension;
        node["limitMemory"] = settings.LimitMemory;
        node["filterMin"] = settings.FilterMin;
        node["filterMag"] = settings.FilterMag;
        node["wrapX"] = settings.WrapX;
        node["wrapY"] = settings.WrapY;
    }

    private static bool ReadBool(string path, string key, bool fallback)
    {
        var node = JsonNode.Parse(File.ReadAllText(path))?.AsObject();
        return node is null ? fallback : ReadBool(node, key, fallback);
    }

    private static bool ReadBool(JsonObject node, string key, bool fallback)
    {
        var value = node[key];
        if (value is null) return fallback;
        if (value is JsonValue jsonValue && jsonValue.TryGetValue<bool>(out var boolValue)) return boolValue;
        return bool.TryParse(value.ToString(), out var parsed) ? parsed : fallback;
    }

    private static int ReadInt(JsonObject node, string key, int fallback) => int.TryParse(node[key]?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    private static float ReadFloat(JsonObject node, string key, float fallback)
    {
        if (node[key] is JsonArray array && array.Count > 0) return float.TryParse(array[0]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;
        return float.TryParse(node[key]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var scalar) ? scalar : fallback;
    }
    private static string ReadString(JsonObject node, string key, string fallback) => node[key]?.ToString() ?? fallback;
}
