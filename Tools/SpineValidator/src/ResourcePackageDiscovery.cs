namespace IdleGearDefence.SpineValidator;

public sealed class DiscoveredSpineResource
{
    public string Directory { get; init; } = string.Empty;

    public string ResourceName { get; init; } = string.Empty;

    public bool HasSkeleton { get; init; }

    public bool HasAtlas { get; init; }

    public bool HasTexture { get; init; }
}

public static class ResourcePackageDiscovery
{
    private static readonly HashSet<string> IgnoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".staging", "Library", "Temp", "Obj", "Logs", "bin", "obj", "node_modules", "UserSettings"
    };

    public static IReadOnlyList<DiscoveredSpineResource> Discover(string rootDirectory)
    {
        var root = Path.GetFullPath(rootDirectory);
        if (!Directory.Exists(root))
            throw new ValidatorExecutionException($"批量验证目录不存在：{root}");

        var result = new List<DiscoveredSpineResource>();
        Walk(root, root, result);
        return result
            .OrderBy(x => x.ResourceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void Walk(
        string root,
        string directory,
        List<DiscoveredSpineResource> result)
    {
        if (ShouldIgnore(directory, root))
            return;

        var files = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly).ToArray();
        var hasSkeleton = files.Any(IsSkeletonFile);
        var hasAtlas = files.Any(IsAtlasFile);
        var hasTexture = files.Any(IsTextureFile);

        if (hasSkeleton || hasAtlas)
        {
            var name = ResolveResourceName(directory, files);
            result.Add(new DiscoveredSpineResource
            {
                Directory = directory,
                ResourceName = name,
                HasSkeleton = hasSkeleton,
                HasAtlas = hasAtlas,
                HasTexture = hasTexture
            });
            return;
        }

        foreach (var child in Directory.EnumerateDirectories(directory).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            Walk(root, child, result);
    }

    private static string ResolveResourceName(string directory, IReadOnlyList<string> files)
    {
        var skeleton = files.FirstOrDefault(IsSkeletonFile);
        if (skeleton != null)
        {
            var name = Path.GetFileName(skeleton);
            foreach (var suffix in new[] { ".skel.bytes", ".skel.json", ".skel", ".json" })
            {
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return name[..^suffix.Length];
            }
        }
        return new DirectoryInfo(directory).Name;
    }

    private static bool ShouldIgnore(string directory, string root)
    {
        if (string.Equals(directory, root, StringComparison.OrdinalIgnoreCase))
            return false;
        return IgnoredDirectoryNames.Contains(new DirectoryInfo(directory).Name);
    }

    private static bool IsSkeletonFile(string path)
    {
        var name = Path.GetFileName(path);
        return name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".skel", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".skel.bytes", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".skel.json", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAtlasFile(string path)
    {
        var name = Path.GetFileName(path);
        return name.EndsWith(".atlas", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".atlas.txt", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTextureFile(string path)
    {
        return Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase);
    }
}
