namespace IdleGearDefence.SpineExporter;

public sealed class SpineExporter
{
    private readonly ExporterConfig _config;
    private readonly ISpineProcessInvoker _processInvoker;
    private readonly IExportLogger _logger;

    public SpineExporter(
        ExporterConfig config,
        ISpineProcessInvoker processInvoker,
        IExportLogger logger)
    {
        _config = config;
        _processInvoker = processInvoker;
        _logger = logger;
    }

    public async Task<ExportResult> ExportAsync(
        ExportRequest request,
        CancellationToken cancellationToken = default)
    {
        string? jobRoot = null;
        CompositeExportLogger? jobLogger = null;

        try
        {
            ValidateRequest(request);
            var profile = _config.GetExportSettings();
            var executable = ResolveSpineExecutable();
            var packSettings = string.Equals(profile.TexturePacker.Mode, "Separate", StringComparison.OrdinalIgnoreCase)
                ? profile.ResolvePackSettings(_config.ConfigDirectory)
                : null;
            var projectName = Path.GetFileNameWithoutExtension(request.SpineProjectPath);
            var stagingRoot = _config.Toolchain.ResolveStagingRoot(_config.ConfigDirectory);
            jobRoot = Path.Combine(stagingRoot, BuildJobId(), projectName);
            var exportDirectory = Path.Combine(jobRoot, "export");
            Directory.CreateDirectory(exportDirectory);
            jobLogger = new CompositeExportLogger(_logger, Path.Combine(jobRoot, "exporter.log"));

            jobLogger.Info($"开始导出：{request.SpineProjectPath}");
            jobLogger.Info($"Staging：{jobRoot}");
            jobLogger.Info($"Spine CLI：{executable}");
            await VerifySpineVersionAsync(executable, jobRoot, cancellationToken).ConfigureAwait(false);

            if (profile.EmitJson)
                await RunDataExportAsync(
                    executable,
                    profile.ResolveExportSettings(_config.ConfigDirectory, binary: false),
                    request,
                    profile,
                    exportDirectory,
                    jobRoot,
                    "JSON",
                    profile.TexturePacker.UsesEmbeddedAttachments,
                    cancellationToken).ConfigureAwait(false);

            if (profile.EmitBinary)
                await RunDataExportAsync(
                    executable,
                    profile.ResolveExportSettings(_config.ConfigDirectory, binary: true),
                    request,
                    profile,
                    exportDirectory,
                    jobRoot,
                    "SKEL",
                    profile.TexturePacker.UsesEmbeddedAttachments && !profile.EmitJson,
                    cancellationToken).ConfigureAwait(false);

            if (profile.TexturePacker.UsesEmbeddedAttachments)
                jobLogger.Info("使用 Spine Attachments 打包模式：仅打包工程中被附件引用的图片，未引用的 images 文件不会进入输出。Pack JSON 不参与此模式。");

            if (string.Equals(profile.TexturePacker.Mode, "Separate", StringComparison.OrdinalIgnoreCase))
            {
                if (packSettings is null)
                    throw new ExportConfigurationException("Separate Texture Packer 模式缺少 pack.json。");

                var inputDirectory = ResolvePackInputDirectory(profile.TexturePacker.InputDirectory, request, exportDirectory);
                var packArguments = SpineCliCommandBuilder.BuildPackArguments(
                    inputDirectory,
                    exportDirectory,
                    packSettings,
                    string.IsNullOrWhiteSpace(profile.TexturePacker.Name) ? projectName : profile.TexturePacker.Name);
                var packResult = await RunCliAsync(
                    executable,
                    packArguments,
                    exportDirectory,
                    Path.Combine(jobRoot, "spine-pack.log"),
                    "纹理打包",
                    cancellationToken).ConfigureAwait(false);
                if (!packResult.Succeeded)
                    return Fail(request, jobRoot, $"Spine Texture Packer 失败：{DescribeProcessFailure(packResult)}", jobLogger);
            }

            var outputDirectory = ResolveOutputDirectory(request, profile, projectName);
            var artifacts = ArtifactNormalizer.Normalize(
                exportDirectory,
                outputDirectory,
                profile.Output,
                profile.EmitJson,
                profile.EmitBinary,
                profile.RequireAtlas,
                profile.RequireTextures);
            if (profile.RequirePremultipliedAlpha && !HasPremultipliedAlphaAtlas(outputDirectory))
                throw new ExportExecutionException("导出配置要求预乘 Alpha，但输出 Atlas 未包含 pma:true。请确认 Spine Export Settings 已勾选预乘。");
            jobLogger.Info($"标准化输出完成：{outputDirectory}");
            jobLogger.Info($"输出文件数：{artifacts.Count}");

            jobLogger.Dispose();
            jobLogger = null;
            if (!_config.Toolchain.KeepStagingOnSuccess)
                DeleteDirectory(jobRoot);

            return ExportResult.Success(
                request,
                outputDirectory,
                jobRoot,
                artifacts.Select(x => x.DestinationPath).ToArray());
        }
        catch (OperationCanceledException)
        {
            jobLogger?.Error("导出已取消。");
            throw;
        }
        catch (Exception exception) when (exception is ExportConfigurationException or ExportExecutionException or IOException)
        {
            return Fail(request, jobRoot, exception.Message, jobLogger);
        }
        finally
        {
            jobLogger?.Dispose();
            if (jobRoot is not null && !_config.Toolchain.KeepStagingOnFailure)
                DeleteDirectory(jobRoot);
        }
    }

    private async Task RunDataExportAsync(
        string executable,
        string exportSettings,
        ExportRequest request,
        ExportProfile profile,
        string exportDirectory,
        string jobRoot,
        string dataKind,
        bool packAttachments,
        CancellationToken cancellationToken)
    {
        _logger.Info($"{dataKind} Export Settings：{exportSettings}");
        if (packAttachments)
            _logger.Info($"{dataKind} 使用 Spine Attachments 打包：仅导出骨骼附件实际引用的图片。");
        var exportArguments = SpineCliCommandBuilder.BuildExportArguments(
            Path.GetFullPath(request.SpineProjectPath),
            exportDirectory,
            exportSettings,
            profile.CleanAnimation);
        var exportResult = await RunCliAsync(
            executable,
            exportArguments,
            exportDirectory,
            Path.Combine(jobRoot, $"spine-export-{dataKind.ToLowerInvariant()}.log"),
            $"{dataKind} 骨架导出",
            cancellationToken).ConfigureAwait(false);
        if (!exportResult.Succeeded)
            throw new ExportExecutionException($"Spine {dataKind} 骨架导出失败：{DescribeProcessFailure(exportResult)}");
    }

    private async Task VerifySpineVersionAsync(
        string executable,
        string jobRoot,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_config.Toolchain.RequiredSpineVersion))
            return;

        var result = await RunCliAsync(
            executable,
            SpineCliCommandBuilder.BuildVersionArguments(),
            jobRoot,
            Path.Combine(jobRoot, "spine-version.log"),
            "Spine 版本检查",
            cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
            throw new ExportExecutionException($"无法读取 Spine CLI 版本：{DescribeProcessFailure(result)}");

        var actualVersion = SpineVersionParser.Parse(result.StandardOutput + Environment.NewLine + result.StandardError);
        if (actualVersion is null)
            throw new ExportExecutionException("Spine CLI 版本输出无法解析。");

        if (!SpineVersionParser.IsCompatible(actualVersion, _config.Toolchain.RequiredSpineVersion))
        {
            throw new ExportExecutionException(
                $"Spine CLI 版本不兼容：实际 {actualVersion}，要求 major.minor {_config.Toolchain.RequiredSpineVersion}。");
        }

        _logger.Info($"Spine CLI 版本：{actualVersion}");
    }

    private async Task<SpineProcessResult> RunCliAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string logPath,
        string operation,
        CancellationToken cancellationToken)
    {
        _logger.Info($"执行 {operation}：{string.Join(" ", arguments.Select(QuoteForLog))}");
        var result = await _processInvoker.RunAsync(
            executable,
            arguments,
            workingDirectory,
            logPath,
            cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
            _logger.Info(result.StandardOutput.TrimEnd());
        if (!string.IsNullOrWhiteSpace(result.StandardError))
            _logger.Warning(result.StandardError.TrimEnd());
        return result;
    }

    private string ResolveSpineExecutable()
    {
        return string.IsNullOrWhiteSpace(_config.Toolchain.SpineExecutable)
            ? "Spine.com"
            : Path.GetFullPath(Path.IsPathRooted(_config.Toolchain.SpineExecutable)
                ? _config.Toolchain.SpineExecutable
                : Path.Combine(_config.ConfigDirectory, _config.Toolchain.SpineExecutable));
    }

    private static string ResolvePackInputDirectory(
        string configuredInput,
        ExportRequest request,
        string exportDirectory)
    {
        if (string.IsNullOrWhiteSpace(configuredInput))
            throw new ExportConfigurationException("Separate Texture Packer 模式必须配置 texturePacker.inputDirectory。");

        var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(request.SpineProjectPath))
                               ?? Directory.GetCurrentDirectory();
        var imagesDirectory = Path.Combine(projectDirectory, "images");
        if (configuredInput.Contains("{imagesDirectory}", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var file in Directory.EnumerateFiles(exportDirectory, "*.json"))
            {
                using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
                if (!json.RootElement.TryGetProperty("skeleton", out var skeleton)
                    || !skeleton.TryGetProperty("images", out var images)
                    || string.IsNullOrWhiteSpace(images.GetString())) continue;
                imagesDirectory = Path.GetFullPath(images.GetString()!, projectDirectory);
                break;
            }
        }
        var expanded = configuredInput
            .Replace("{projectDirectory}", projectDirectory, StringComparison.OrdinalIgnoreCase)
            .Replace("{imagesDirectory}", imagesDirectory, StringComparison.OrdinalIgnoreCase)
            .Replace("{exportDirectory}", exportDirectory, StringComparison.OrdinalIgnoreCase);
        var inputDirectory = Path.GetFullPath(Path.IsPathRooted(expanded)
            ? expanded
            : Path.Combine(projectDirectory, expanded));
        if (!Directory.Exists(inputDirectory))
            throw new ExportConfigurationException($"Texture Packer 输入目录不存在：{inputDirectory}");
        return inputDirectory;
    }

    private static string ResolveOutputDirectory(
        ExportRequest request,
        ExportProfile profile,
        string projectName)
    {
        var subdirectory = profile.Output.OutputSubdirectoryTemplate
            .Replace("{resourceName}", projectName, StringComparison.OrdinalIgnoreCase);
        if (Path.IsPathRooted(subdirectory))
            throw new ExportConfigurationException("output.outputSubdirectoryTemplate 不能生成绝对路径。");

        var outputRoot = Path.GetFullPath(request.OutputDirectory);
        var destination = Path.GetFullPath(Path.Combine(outputRoot, subdirectory));
        if (!destination.StartsWith(outputRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)
            && !string.Equals(destination, outputRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new ExportConfigurationException("输出目录模板越过了指定输出根目录。");
        }

        return destination;
    }

    private void ValidateRequest(ExportRequest request)
    {
        if (request is null)
            throw new ExportExecutionException("导出请求不能为空。");
        if (!File.Exists(request.SpineProjectPath))
            throw new ExportExecutionException($"Spine 工程文件不存在：{request.SpineProjectPath}");
        if (string.IsNullOrWhiteSpace(request.OutputDirectory))
            throw new ExportExecutionException("输出目录不能为空。");
    }

    private ExportResult Fail(
        ExportRequest request,
        string? jobRoot,
        string message,
        IExportLogger? logger = null)
    {
        (logger ?? _logger).Error(message);
        return ExportResult.Failure(request, jobRoot, message);
    }

    private static string BuildJobId()
    {
        return $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
    }

    private static string DescribeProcessFailure(SpineProcessResult result)
    {
        return result.ErrorMessage
               ?? $"退出码 {result.ExitCode}；stderr：{result.StandardError.Trim()}";
    }

    private static bool HasPremultipliedAlphaAtlas(string directory)
    {
        return Directory.EnumerateFiles(directory, "*.atlas*", SearchOption.TopDirectoryOnly)
            .Any(path => System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(path), @"(?im)^\s*pma\s*:\s*true\s*$"));
    }

    private static string QuoteForLog(string value)
    {
        return value.Contains(' ') ? $"\"{value}\"" : value;
    }

    private static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Cleanup failure must not replace the export result or original exception.
        }
    }
}
