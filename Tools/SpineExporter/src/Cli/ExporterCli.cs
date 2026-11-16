namespace IdleGearDefence.SpineExporter;

public static class ExporterCli
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Any(x => string.Equals(x, "--help", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(x, "-h", StringComparison.OrdinalIgnoreCase)))
        {
            PrintUsage();
            return 0;
        }

        try
        {
            var batchPath = GetOption(args, "batch");
            if (!string.IsNullOrWhiteSpace(batchPath))
            {
                var configPath = GetOption(args, "config")
                                 ?? throw new ExportExecutionException("批处理模式仍需要 --config。");
                var batchLogger = new ConsoleExportLogger();
                return await ExportBatchRunner.RunAsync(batchPath, configPath, batchLogger).ConfigureAwait(false) ? 0 : 1;
            }

            var options = ParseOptions(args);
            var config = ExporterConfigLoader.Load(options.ConfigPath);
            var logger = new ConsoleExportLogger();
            var exporter = new SpineExporter(config, new SpineCliProcessInvoker(), logger);
            var result = await exporter.ExportAsync(new ExportRequest
            {
                SpineProjectPath = options.ProjectPath,
                OutputDirectory = options.OutputDirectory
            }).ConfigureAwait(false);

            if (result.Succeeded)
            {
                logger.Info($"导出成功：{result.OutputDirectory}");
                return 0;
            }

            return 1;
        }
        catch (ExportConfigurationException exception)
        {
            Console.Error.WriteLine($"[CONFIG] {exception.Message}");
            return 2;
        }
        catch (ExportExecutionException exception)
        {
            Console.Error.WriteLine($"[EXPORT] {exception.Message}");
            return 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[FATAL] {exception}");
            return 1;
        }
    }

    private static CliOptions ParseOptions(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
                throw new ExportExecutionException($"无法识别的参数：{arg}");
            if (i + 1 >= args.Length)
                throw new ExportExecutionException($"参数缺少值：{arg}");
            values[arg[2..]] = args[++i];
        }

        return new CliOptions(
            Required(values, "config"),
            Required(values, "project"),
            Required(values, "output"));
    }

    private static string? GetOption(string[] args, string name)
    {
        for (var i = 0; i + 1 < args.Length; i++)
        {
            if (string.Equals(args[i], "--" + name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }

    private static string Required(IReadOnlyDictionary<string, string> values, string name)
    {
        if (!values.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
            throw new ExportExecutionException($"缺少必需参数：--{name}");
        return value;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("SpineExporter 阶段 1：标准化导出");
        Console.WriteLine();
        Console.WriteLine("用法：");
        Console.WriteLine("  SpineExporter --config <config.json> --project <project.spine> --output <directory>");
        Console.WriteLine();
        Console.WriteLine("说明：Exporter 只负责调用 Spine CLI、写入 staging 并生成标准化 JSON/SKEL/Atlas/PNG 目录。");
    }

    private sealed record CliOptions(
        string ConfigPath,
        string ProjectPath,
        string OutputDirectory);
}
