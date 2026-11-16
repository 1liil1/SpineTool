using System.Text;

namespace IdleGearDefence.SpineExporter;

public static class ArtifactNormalizer
{
    public static IReadOnlyList<NormalizedArtifact> Normalize(
        string stagingDirectory,
        string outputDirectory,
        OutputPolicy policy,
        bool emitJson,
        bool emitBinary,
        bool requireAtlas,
        bool requireTextures)
    {
        if (!Directory.Exists(stagingDirectory))
            throw new ExportExecutionException($"Spine CLI 输出目录不存在：{stagingDirectory}");

        var candidates = Directory.EnumerateFiles(stagingDirectory, "*", SearchOption.AllDirectories)
            .Where(IsSupportedCandidate)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var normalized = new List<NormalizedArtifact>();
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var sourcePath in candidates)
        {
            var classification = Classify(sourcePath);
            if (classification is null)
                continue;

            if ((classification.Kind == "json" && !emitJson)
                || (classification.Kind == "binary" && !emitBinary))
            {
                continue;
            }

            var destinationName = classification.Kind switch
            {
                "json" => classification.Stem + ".json",
                "binary" => classification.Stem + policy.BinaryExtension,
                "atlas" => classification.Stem + policy.AtlasExtension,
                "texture" => Path.GetFileName(sourcePath),
                _ => throw new InvalidOperationException($"Unsupported Spine artifact kind: {classification.Kind}")
            };

            var destinationPath = Path.Combine(outputDirectory, destinationName);
            if (!destinations.Add(destinationPath))
                throw new ExportExecutionException($"多个导出文件归一化后产生同名文件：{destinationName}");

            normalized.Add(new NormalizedArtifact(classification.Kind, sourcePath, destinationPath));
        }

        ValidateRequiredArtifacts(normalized, emitJson, emitBinary, requireAtlas, requireTextures, outputDirectory);

        foreach (var artifact in normalized)
        {
            if (File.Exists(artifact.DestinationPath) && !policy.AllowOverwrite)
                throw new ExportExecutionException($"输出文件已存在，当前 Profile 禁止覆盖：{artifact.DestinationPath}");
        }

        Directory.CreateDirectory(outputDirectory);
        foreach (var artifact in normalized)
            File.Copy(artifact.SourcePath, artifact.DestinationPath, policy.AllowOverwrite);

        return normalized;
    }

    private static void ValidateRequiredArtifacts(
        IReadOnlyList<NormalizedArtifact> artifacts,
        bool emitJson,
        bool emitBinary,
        bool requireAtlas,
        bool requireTextures,
        string outputDirectory)
    {
        if (emitJson && !artifacts.Any(x => x.Kind == "json"))
            throw new ExportExecutionException($"导出结果缺少 JSON 骨架数据：{outputDirectory}");

        if (emitBinary && !artifacts.Any(x => x.Kind == "binary"))
            throw new ExportExecutionException($"导出结果缺少 SKEL 二进制骨架数据：{outputDirectory}");

        if (requireAtlas && !artifacts.Any(x => x.Kind == "atlas"))
            throw new ExportExecutionException($"导出结果缺少 Atlas 文件：{outputDirectory}");

        if (requireTextures && !artifacts.Any(x => x.Kind == "texture"))
            throw new ExportExecutionException($"导出结果缺少 PNG 纹理：{outputDirectory}");
    }

    private static bool IsSupportedCandidate(string path)
    {
        var name = Path.GetFileName(path);
        return name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".skel", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".skel.bytes", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".skel.json", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".atlas", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".atlas.txt", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
    }

    private static ArtifactClassification? Classify(string path)
    {
        var name = Path.GetFileName(path);
        if (name.EndsWith(".atlas.txt", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".atlas", StringComparison.OrdinalIgnoreCase))
        {
            return new ArtifactClassification("atlas", RemoveSuffix(name, ".atlas.txt", ".atlas"));
        }

        if (name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            return new ArtifactClassification("texture", Path.GetFileNameWithoutExtension(name));

        if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".skel", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".skel.bytes", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".skel.json", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = File.ReadAllBytes(path);
            var isJson = LooksLikeJson(bytes);
            var stem = RemoveSuffix(name, ".skel.bytes", ".skel.json", ".skel", ".json");
            return new ArtifactClassification(isJson ? "json" : "binary", stem);
        }

        return null;
    }

    private static string RemoveSuffix(string name, params string[] suffixes)
    {
        foreach (var suffix in suffixes)
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return name[..^suffix.Length];
        }

        return Path.GetFileNameWithoutExtension(name);
    }

    private static bool LooksLikeJson(byte[] bytes)
    {
        var length = Math.Min(bytes.Length, 256);
        var index = 0;
        if (length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            index = 3;

        while (index < length && char.IsWhiteSpace((char)bytes[index]))
            index++;

        if (index >= length || (bytes[index] != '{' && bytes[index] != '['))
            return false;

        var preview = Encoding.UTF8.GetString(bytes, index, length - index);
        return preview.Contains('"', StringComparison.Ordinal);
    }

    private sealed record ArtifactClassification(string Kind, string Stem);
}

public sealed class ExportExecutionException : Exception
{
    public ExportExecutionException(string message)
        : base(message)
    {
    }

    public ExportExecutionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
