#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

public sealed class PreviewResourceEntry
{
    public string Name { get; }
    public string Key { get; }
    public string DisplayName => Key;
    public string Directory { get; }
    public PreviewManifest Manifest { get; }
    public string AtlasPath { get; }
    public string[] SkeletonPaths { get; }
    public string Error { get; }

    public PreviewResourceEntry(string name, string atlasPath, string[] skeletonPaths, string error,
        string key = null, string directory = null, PreviewManifest manifest = null)
    {
        Name = name;
        Key = key ?? name;
        Directory = directory ?? Path.GetDirectoryName(skeletonPaths[0]);
        Manifest = manifest ?? new PreviewManifest();
        AtlasPath = atlasPath;
        SkeletonPaths = skeletonPaths;
        Error = error;
    }
}

/// <summary>Scan recursively, pairing and grouping formats within each resource's own folder.</summary>
public static class PreviewResourceCatalog
{
    private static readonly string[] DataExtensions = { ".skel.bytes", ".skel.json", ".skel", ".json" };

    public static IReadOnlyList<PreviewResourceEntry> Discover(string directory, PreviewManifest manifest = null,
        Func<string, PreviewManifest> readManifest = null, Action<string> reportWarning = null)
    {
        if (string.IsNullOrWhiteSpace(directory) || !System.IO.Directory.Exists(directory))
            throw new DirectoryNotFoundException("资源目录不存在：" + directory);
        directory = Path.GetFullPath(directory);
        var results = new List<PreviewResourceEntry>();
        foreach (var folder in EnumerateFolders(directory, reportWarning))
        {
            try
            {
                var localManifest = string.Equals(folder, directory, StringComparison.OrdinalIgnoreCase) && manifest != null
                    ? manifest : readManifest?.Invoke(folder) ?? new PreviewManifest();
                results.AddRange(DiscoverFolder(directory, folder, localManifest, reportWarning));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            { reportWarning?.Invoke("无法读取目录：" + folder + "；" + exception.Message); }
        }
        return results.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IEnumerable<string> EnumerateFolders(string root, Action<string> warning)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var folder = pending.Pop();
            yield return folder;
            string[] children;
            try { children = System.IO.Directory.GetDirectories(folder); }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                warning?.Invoke("无法扫描子目录：" + folder + "；" + exception.Message);
                continue;
            }
            foreach (var child in children.OrderByDescending(x => x, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    // Avoid junction/symlink cycles and escaping the selected resource tree.
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child);
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                { warning?.Invoke("无法读取子目录：" + child + "；" + exception.Message); }
            }
        }
    }

    private static IEnumerable<PreviewResourceEntry> DiscoverFolder(string root, string directory, PreviewManifest manifest, Action<string> warning)
    {
        var files = System.IO.Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly);
        var atlasPaths = files.Where(IsAtlas).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        var dataPaths = new List<string>();
        foreach (var path in files)
        {
            try { if (IsSkeleton(path)) dataPaths.Add(path); }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            { warning?.Invoke("暂时无法读取资源：" + path + "；" + exception.Message); }
        }
        if (!string.IsNullOrWhiteSpace(manifest.skeletonFile))
        {
            var explicitPath = Path.GetFullPath(Path.Combine(directory, manifest.skeletonFile));
            // Nested resources are discovered locally, not duplicated under their parent.
            if (string.Equals(Path.GetDirectoryName(explicitPath), directory, StringComparison.OrdinalIgnoreCase)
                && File.Exists(explicitPath) && !dataPaths.Contains(explicitPath, StringComparer.OrdinalIgnoreCase)) dataPaths.Add(explicitPath);
        }
        var manifestName = !string.IsNullOrWhiteSpace(manifest.skeletonFile) ? GetResourceName(manifest.skeletonFile) : manifest.resourceName;
        foreach (var group in dataPaths.GroupBy(GetResourceName, StringComparer.OrdinalIgnoreCase))
        {
            string atlas;
            if (string.Equals(manifestName, group.Key, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(manifest.atlasFile))
                atlas = Path.GetFullPath(Path.Combine(directory, manifest.atlasFile));
            else
                atlas = atlasPaths.Where(x => string.Equals(GetAtlasName(x), group.Key, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => x.EndsWith(".atlas.txt", StringComparison.OrdinalIgnoreCase) ? 0 : 1).FirstOrDefault();
            if (atlas == null && atlasPaths.Length == 1) atlas = atlasPaths[0];
            var error = atlas == null ? (atlasPaths.Length > 1
                ? "没有同名 Atlas，目录存在多份 Atlas，无法确定配对：" + group.Key : "未找到 Atlas：" + group.Key)
                : !File.Exists(atlas) ? "指定的 Atlas 不存在：" + atlas : string.Empty;
            var skeletons = group.OrderBy(x => LooksLikeJson(x) ? 1 : 0).ThenBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
            var relative = Path.GetRelativePath(root, directory).Replace('\\', '/');
            var key = relative == "." ? group.Key : relative + "/" + group.Key;
            yield return new PreviewResourceEntry(group.Key, atlas, skeletons, error, key, directory, manifest);
        }
    }

    public static PreviewResourceEntry Select(IReadOnlyList<PreviewResourceEntry> resources, string key, PreviewManifest manifest)
    {
        var previous = resources.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
        if (previous != null) return previous;
        if (!string.IsNullOrWhiteSpace(manifest?.skeletonFile))
        {
            var manifestPath = manifest.skeletonFile.Replace('\\', '/');
            var preferred = resources.FirstOrDefault(x => string.Equals(x.Key, RemoveDataExtension(manifestPath), StringComparison.OrdinalIgnoreCase));
            if (preferred != null) return preferred;
        }
        return resources.FirstOrDefault(x => string.Equals(x.Key, manifest?.resourceName, StringComparison.OrdinalIgnoreCase)) ?? resources.FirstOrDefault();
    }

    public static string GetResourceName(string path) => RemoveDataExtension(Path.GetFileName(path));
    private static string RemoveDataExtension(string path)
    {
        foreach (var extension in DataExtensions)
            if (path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) return path.Substring(0, path.Length - extension.Length);
        return path;
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
