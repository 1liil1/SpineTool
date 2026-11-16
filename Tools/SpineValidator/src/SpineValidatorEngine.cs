using System.Security.Cryptography;
using System.Text;
using Spine;

namespace IdleGearDefence.SpineValidator;

public sealed class SpineValidatorEngine
{
    public ValidationReport Validate(
        string resourceDirectory,
        ValidatorRules rules)
    {
        var directory = Path.GetFullPath(resourceDirectory);
        if (!Directory.Exists(directory))
            throw new ValidatorExecutionException($"资源目录不存在：{directory}");

        var skeletonFiles = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .Where(IsSkeletonFile)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var atlasFiles = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .Where(IsAtlasFile)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var textureFiles = Directory.EnumerateFiles(directory, "*.png", SearchOption.TopDirectoryOnly)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var resourceName = ResolveResourceName(directory, skeletonFiles);
        var profile = rules.Resolve();
        var report = new ValidationReport
        {
            ResourceName = resourceName
        };

        if (!rules.Enabled)
            return report;

        AddFileInfo(report, skeletonFiles, "skeleton");
        AddFileInfo(report, atlasFiles, "atlas");
        AddFileInfo(report, textureFiles, "texture");

        var jsonFiles = skeletonFiles.Where(IsJsonSkeleton).ToArray();
        var binaryFiles = skeletonFiles.Where(x => !IsJsonSkeleton(x)).ToArray();
        CheckPresence(report, profile, jsonFiles, binaryFiles, atlasFiles, textureFiles);
        CheckNaming(report, profile, resourceName, directory, skeletonFiles);
        CheckAtlas(report, atlasFiles, textureFiles);

        SkeletonData parsedData = null;
        string parsedFile = null;
        var parseCandidates = binaryFiles.Concat(jsonFiles).ToArray();
        foreach (var file in parseCandidates)
        {
            try
            {
                parsedData = ParseSkeleton(file, atlasFiles);
                parsedFile = file;
                break;
            }
            catch (Exception exception)
            {
                report.Add(
                    "SKELETON_PARSE_FAILED",
                    ValidationSeverity.Blocker,
                    $"无法解析 Spine 数据：{Path.GetFileName(file)}；{exception.Message}",
                    file);
            }
        }

        if (parsedData is null)
            return report;

        CheckVersion(report, profile, parsedData, parsedFile);
        CheckSkeletonStructure(report, profile, parsedData, parsedFile);
        CheckEvents(report, profile, parsedData, parsedFile);
        return report;
    }

    private static SkeletonData ParseSkeleton(string skeletonPath, IReadOnlyList<string> atlasFiles)
    {
        var atlases = atlasFiles
            .Select(path =>
            {
                using var reader = File.OpenText(path);
                return new Atlas(reader, Path.GetDirectoryName(path) ?? string.Empty, new NoOpTextureLoader());
            })
            .ToArray();
        try
        {
            var loader = new AtlasAttachmentLoader(atlases);
            if (IsJsonSkeleton(skeletonPath))
            {
                using var reader = File.OpenText(skeletonPath);
                var jsonData = new SkeletonJson(loader).ReadSkeletonData(reader);
                if (jsonData == null)
                    throw new InvalidDataException("JSON 数据无法由当前 Spine Runtime 解析。");
                return jsonData;
            }

            using var stream = File.OpenRead(skeletonPath);
            var binaryData = new SkeletonBinary(loader).ReadSkeletonData(stream);
            if (binaryData == null)
                throw new InvalidDataException("SKEL 数据无法由当前 Spine Runtime 解析，可能是 Spine major/minor 版本不兼容。");
            return binaryData;
        }
        finally
        {
            foreach (var atlas in atlases)
                atlas.Dispose();
        }
    }

    private static void CheckPresence(
        ValidationReport report,
        ValidatorProfile profile,
        IReadOnlyList<string> jsonFiles,
        IReadOnlyList<string> binaryFiles,
        IReadOnlyList<string> atlasFiles,
        IReadOnlyList<string> textureFiles)
    {
        if (profile.RequireJson == true && jsonFiles.Count == 0)
            report.Add("JSON_MISSING", ValidationSeverity.Blocker, "缺少 JSON 骨架数据。");
        if (profile.RequireBinary == true && binaryFiles.Count == 0)
            report.Add("SKEL_MISSING", ValidationSeverity.Blocker, "缺少 SKEL 二进制骨架数据。");
        if (profile.RequireAtlas != false && atlasFiles.Count == 0)
            report.Add("ATLAS_MISSING", ValidationSeverity.Blocker, "缺少 Atlas 文件。");
        if (profile.RequireTextures != false && textureFiles.Count == 0)
            report.Add("TEXTURE_MISSING", ValidationSeverity.Blocker, "缺少 PNG 纹理。");
    }

    private static void CheckVersion(
        ValidationReport report,
        ValidatorProfile profile,
        SkeletonData data,
        string file)
    {
        if (string.IsNullOrWhiteSpace(profile.DataVersion))
            return;
        if (!VersionMatches(data.Version, profile.DataVersion))
        {
            report.Add(
                "SPINE_VERSION_MISMATCH",
                ValidationSeverity.Blocker,
                $"Spine 数据版本为 {data.Version ?? "unknown"}，要求 {profile.DataVersion}。",
                file);
        }
    }

    private static void CheckSkeletonStructure(
        ValidationReport report,
        ValidatorProfile profile,
        SkeletonData data,
        string file)
    {
        foreach (var animation in profile.RequiredAnimations ?? Enumerable.Empty<string>())
        {
            if (data.FindAnimation(animation) is null)
                report.Add("ANIMATION_MISSING", ValidationSeverity.Blocker, $"缺少必需动画：{animation}。", file);
        }

        foreach (var bone in profile.RequiredBones ?? Enumerable.Empty<string>())
        {
            if (data.FindBone(bone) is null)
                report.Add("BONE_MISSING", ValidationSeverity.Blocker, $"缺少必需骨骼：{bone}。", file);
        }

        foreach (var bone in profile.WarningBones ?? Enumerable.Empty<string>())
        {
            if (data.FindBone(bone) is null)
                report.Add("BONE_MISSING_WARNING", ValidationSeverity.Warning, $"缺少建议骨骼：{bone}。", file);
        }

        foreach (var slot in profile.RequiredSlots ?? Enumerable.Empty<string>())
        {
            if (data.FindSlot(slot) is null)
                report.Add("SLOT_MISSING", ValidationSeverity.Blocker, $"缺少必需插槽：{slot}。", file);
        }
    }

    private static void CheckEvents(
        ValidationReport report,
        ValidatorProfile profile,
        SkeletonData data,
        string file)
    {
        foreach (var rule in profile.Events ?? Enumerable.Empty<AnimationEventRule>())
        {
            var animation = data.FindAnimation(rule.Animation);
            if (animation is null)
            {
                report.Add("EVENT_ANIMATION_MISSING", ValidationSeverity.Blocker,
                    $"事件规则引用的动画不存在：{rule.Animation}。", file);
                continue;
            }

            var events = new List<Event>();
            foreach (var timeline in animation.Timelines.Items.Take(animation.Timelines.Count).OfType<EventTimeline>())
            {
                events.AddRange(timeline.Events);
            }

            var matching = events.Where(x => string.Equals(x.Data.Name, rule.EventName, StringComparison.Ordinal)).ToArray();
            if (matching.Length < rule.MinCount || rule.MaxCount.HasValue && matching.Length > rule.MaxCount.Value)
            {
                report.Add("EVENT_COUNT_MISMATCH", ValidationSeverity.Blocker,
                    $"动画 {rule.Animation} 的事件 {rule.EventName} 数量为 {matching.Length}，要求 {rule.MinCount}～{rule.MaxCount?.ToString() ?? "∞"}。", file);
            }

            if (rule.ExpectedTime.HasValue)
            {
                if (!matching.Any(x => Math.Abs(x.Time - rule.ExpectedTime.Value) <= rule.TimeTolerance))
                {
                    report.Add("EVENT_TIME_MISMATCH", ValidationSeverity.Blocker,
                        $"动画 {rule.Animation} 的事件 {rule.EventName} 未在 {rule.ExpectedTime.Value:F3}s ± {rule.TimeTolerance:F3}s 出现。", file);
                }
            }
        }
    }

    private static void CheckNaming(
        ValidationReport report,
        ValidatorProfile profile,
        string resourceName,
        string directory,
        IReadOnlyList<string> skeletonFiles)
    {
        if (!string.IsNullOrWhiteSpace(profile.NamingPattern))
        {
            try
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(resourceName, profile.NamingPattern))
                    report.Add("NAME_INVALID", ValidationSeverity.Blocker, $"资源名不符合规则：{resourceName}。");
            }
            catch (ArgumentException exception)
            {
                report.Add("RULE_INVALID", ValidationSeverity.Blocker, $"命名规则正则无效：{exception.Message}");
            }
        }

        if (skeletonFiles.Any(x => !string.Equals(Path.GetDirectoryName(x), directory, StringComparison.OrdinalIgnoreCase)))
            report.Add("DIRECTORY_INVALID", ValidationSeverity.Blocker, "Spine 标准资源必须直接位于资源目录中。");
    }

    private static void CheckAtlas(
        ValidationReport report,
        IReadOnlyList<string> atlasFiles,
        IReadOnlyList<string> textureFiles)
    {
        var textureMap = textureFiles.ToDictionary(x => Path.GetFileName(x), StringComparer.OrdinalIgnoreCase);
        foreach (var atlasPath in atlasFiles)
        {
            try
            {
                Atlas atlas;
                using (var reader = File.OpenText(atlasPath))
                    atlas = new Atlas(reader, Path.GetDirectoryName(atlasPath) ?? string.Empty, new NoOpTextureLoader());
                foreach (var page in atlas.Pages)
                {
                    if (!textureMap.TryGetValue(Path.GetFileName(page.name), out var texturePath))
                    {
                        report.Add("ATLAS_PAGE_MISSING", ValidationSeverity.Blocker,
                            $"Atlas 页面引用的 PNG 不存在：{page.name}。", atlasPath);
                        continue;
                    }

                    if (!PngInspector.TryReadSize(texturePath, out var width, out var height, out var error))
                    {
                        report.Add("PNG_INVALID", ValidationSeverity.Blocker, error!, texturePath);
                        continue;
                    }

                    if (width != page.width || height != page.height)
                    {
                        report.Add("ATLAS_TEXTURE_SIZE_MISMATCH", ValidationSeverity.Blocker,
                            $"Atlas 页面 {page.name} 声明尺寸 {page.width}x{page.height}，PNG 实际为 {width}x{height}。", atlasPath);
                    }
                }

                foreach (var region in atlas.Regions)
                {
                    var page = region.page;
                    if (region.x < 0 || region.y < 0 || region.x + region.width > page.width || region.y + region.height > page.height)
                    {
                        report.Add("ATLAS_REGION_OUT_OF_BOUNDS", ValidationSeverity.Blocker,
                            $"Atlas 区域越界：{region.name}。", atlasPath);
                    }
                }

                atlas.Dispose();
            }
            catch (Exception exception)
            {
                report.Add("ATLAS_PARSE_FAILED", ValidationSeverity.Blocker,
                    $"Atlas 无法解析：{exception.Message}。", atlasPath);
            }
        }
    }

    private static void AddFileInfo(ValidationReport report, IEnumerable<string> files, string kind)
    {
        foreach (var path in files)
        {
            report.Files.Add(new ValidationFileInfo(kind, path, new FileInfo(path).Length, ComputeSha256(path)));
        }
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string ResolveResourceName(string directory, IReadOnlyList<string> skeletonFiles)
    {
        if (skeletonFiles.Count > 0)
            return RemoveSkeletonSuffix(Path.GetFileName(skeletonFiles[0]));
        return new DirectoryInfo(directory).Name;
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

    private static bool IsJsonSkeleton(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var length = Math.Min(bytes.Length, 256);
        var index = 0;
        if (length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            index = 3;
        while (index < length && char.IsWhiteSpace((char)bytes[index])) index++;
        return index < length && (bytes[index] == '{' || bytes[index] == '[');
    }

    private static string RemoveSkeletonSuffix(string name)
    {
        foreach (var suffix in new[] { ".skel.bytes", ".skel.json", ".skel", ".json" })
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return name[..^suffix.Length];
        }
        return Path.GetFileNameWithoutExtension(name);
    }

    private static bool VersionMatches(string actual, string expected)
    {
        if (string.IsNullOrWhiteSpace(actual)) return false;
        var actualParts = actual.Split('.');
        var expectedParts = expected.Split('.');
        return actualParts.Length >= 2 && expectedParts.Length >= 2
            && actualParts[0] == expectedParts[0]
            && actualParts[1] == expectedParts[1];
    }

    private sealed class NoOpTextureLoader : TextureLoader
    {
        public void Load(AtlasPage page, string path) { }
        public void Unload(object texture) { }
    }
}

public sealed class ValidatorExecutionException : Exception
{
    public ValidatorExecutionException(string message) : base(message) { }
}

internal static class PngInspector
{
    private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    public static bool TryReadSize(string path, out int width, out int height, out string error)
    {
        width = 0;
        height = 0;
        error = null;
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < 24 || !bytes.AsSpan(0, 8).SequenceEqual(Signature))
            {
                error = $"PNG 文件格式无效：{path}";
                return false;
            }
            width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4));
            height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));
            if (width <= 0 || height <= 0)
            {
                error = $"PNG 尺寸无效：{path}";
                return false;
            }
            return true;
        }
        catch (Exception exception)
        {
            error = $"PNG 无法读取：{path}；{exception.Message}";
            return false;
        }
    }
}
