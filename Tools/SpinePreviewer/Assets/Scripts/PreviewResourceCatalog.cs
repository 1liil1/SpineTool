#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

public sealed class PreviewResourceEntry
{
    public string Name { get; }
    public string AtlasPath { get; }
    public string[] SkeletonPaths { get; }
    public string Error { get; }

    public PreviewResourceEntry(string name, string atlasPath, string[] skeletonPaths, string error)
    {
        Name = name;
        AtlasPath = atlasPath;
        SkeletonPaths = skeletonPaths;
        Error = error;
    }
}

/// <summary>Pairs each skeleton with its own atlas and groups JSON/binary alternatives.</summary>
public static class PreviewResourceCatalog
{
    private static readonly string[] DataExtensions = { ".skel.bytes", ".skel.json", ".skel", ".json" };

    public static IReadOnlyList<PreviewResourceEntry> Discover(string directory, PreviewManifest manifest = null)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            throw new DirectoryNotFoundException("资源目录不存在：" + directory);
        directory = Path.GetFullPath(directory);
        var files = Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly);
        var atlasPaths = files.Where(IsAtlas).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        var dataPaths = files.Where(IsSkeleton).ToList();
        if (!string.IsNullOrWhiteSpace(manifest?.skeletonFile))
        {
            var explicitPath = Path.GetFullPath(Path.Combine(directory, manifest.skeletonFile));
            if (File.Exists(explicitPath) && !dataPaths.Contains(explicitPath, StringComparer.OrdinalIgnoreCase))
                dataPaths.Add(explicitPath);
        }
        var manifestName = !string.IsNullOrWhiteSpace(manifest?.skeletonFile)
            ? GetResourceName(manifest.skeletonFile)
            : manifest?.resourceName;
        var results = new List<PreviewResourceEntry>();
        foreach (var group in dataPaths.GroupBy(GetResourceName, StringComparer.OrdinalIgnoreCase).OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            string atlas = null;
            if (string.Equals(manifestName, group.Key, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(manifest?.atlasFile))
                atlas = Path.GetFullPath(Path.Combine(directory, manifest.atlasFile));
            else
                atlas = atlasPaths.Where(x => string.Equals(GetAtlasName(x), group.Key, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => x.EndsWith(".atlas.txt", StringComparison.OrdinalIgnoreCase) ? 0 : 1).FirstOrDefault();

            // Spine can export multiple skeletons using one shared atlas.
            if (atlas == null && atlasPaths.Length == 1) atlas = atlasPaths[0];
            var error = atlas == null ? (atlasPaths.Length > 1
                ? "没有同名 Atlas，目录存在多份 Atlas，无法确定配对：" + group.Key
                : "未找到 Atlas：" + group.Key)
                : !File.Exists(atlas) ? "指定的 Atlas 不存在：" + atlas : string.Empty;
            var skeletons = group.OrderBy(x => LooksLikeJson(x) ? 1 : 0).ThenBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
            results.Add(new PreviewResourceEntry(group.Key, atlas, skeletons, error));
        }
        return results;
    }

    public static PreviewResourceEntry Select(IReadOnlyList<PreviewResourceEntry> resources, string name, PreviewManifest manifest)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            var previous = resources.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (previous != null) return previous;
        }
        var preferred = !string.IsNullOrWhiteSpace(manifest?.skeletonFile) ? GetResourceName(manifest.skeletonFile) : manifest?.resourceName;
        return resources.FirstOrDefault(x => string.Equals(x.Name, preferred, StringComparison.OrdinalIgnoreCase)) ?? resources.FirstOrDefault();
    }

    public static string GetResourceName(string path)
    {
        var fileName = Path.GetFileName(path);
        foreach (var extension in DataExtensions)
            if (fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) return fileName.Substring(0, fileName.Length - extension.Length);
        return Path.GetFileNameWithoutExtension(fileName);
    }

    public static bool LooksLikeJson(string path)
    {
        using var reader = new StreamReader(path);
        while (reader.Peek() >= 0)
        {
            var ch = (char)reader.Read();
            if (char.IsWhiteSpace(ch) || ch == '\uFEFF') continue;
            return ch == '{' || ch == '[';
        }
        return false;
    }

    private static bool IsSkeleton(string path)
    {
        if (!DataExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))) return false;
        if (!LooksLikeJson(path)) return !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".skel.json", StringComparison.OrdinalIgnoreCase);
        // Configuration files in the same directory must not become resource buttons.
        using var reader = new StreamReader(path);
        var buffer = new char[16384];
        var count = reader.ReadBlock(buffer, 0, buffer.Length);
        return Regex.IsMatch(new string(buffer, 0, count), "\"bones\"\\s*:");
    }

    private static bool IsAtlas(string path) => path.EndsWith(".atlas", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".atlas.txt", StringComparison.OrdinalIgnoreCase);
    private static string GetAtlasName(string path)
    {
        var name = Path.GetFileName(path);
        return name.Substring(0, name.Length - (name.EndsWith(".atlas.txt", StringComparison.OrdinalIgnoreCase) ? 10 : 6));
    }
}
