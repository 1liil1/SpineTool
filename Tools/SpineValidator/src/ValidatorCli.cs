using System.Text.Json;

namespace IdleGearDefence.SpineValidator;

public static class ValidatorCli
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var batchPath = GetOption(args, "batch");
            if (!string.IsNullOrWhiteSpace(batchPath))
            {
                var rulesPath = GetOption(args, "rules")
                                ?? throw new ValidatorExecutionException("批处理模式仍需要 --rules。");
                return await ValidatorBatchRunner.RunAsync(batchPath, rulesPath).ConfigureAwait(false) ? 0 : 1;
            }

            var options = Parse(args);
            var rules = new ValidatorRulesLoader().Load(options.RulesPath);
            var report = new SpineValidatorEngine().Validate(options.InputPath, rules);
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.ReportPath)) ?? Directory.GetCurrentDirectory());
            await File.WriteAllTextAsync(options.ReportPath, json);
            Console.WriteLine($"Validator report: {options.ReportPath}");
            foreach (var issue in report.Issues)
                Console.WriteLine($"[{issue.Severity}] {issue.Code}: {issue.Message}");
            return report.Passed ? 0 : 1;
        }
        catch (ValidatorConfigurationException exception)
        {
            Console.Error.WriteLine($"[CONFIG] {exception.Message}");
            return 2;
        }
        catch (ValidatorExecutionException exception)
        {
            Console.Error.WriteLine($"[VALIDATOR] {exception.Message}");
            return 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[FATAL] {exception}");
            return 1;
        }
    }

    private static Options Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is "--help" or "-h")
            {
                Console.WriteLine("SpineValidator --input <directory> --rules <Rules.json> --report <report.json>");
                Environment.Exit(0);
            }
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
                throw new ValidatorExecutionException($"参数无效：{args[i]}");
            values[args[i][2..]] = args[++i];
        }
        return new Options(Required(values, "input"), Required(values, "rules"), Required(values, "report"));
    }

    private static string Required(IReadOnlyDictionary<string, string> values, string name)
    {
        if (!values.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
            throw new ValidatorExecutionException($"缺少参数：--{name}");
        return value;
    }

    private static string GetOption(string[] args, string name)
    {
        for (var i = 0; i + 1 < args.Length; i++)
        {
            if (string.Equals(args[i], "--" + name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }

    private sealed record Options(string InputPath, string RulesPath, string ReportPath);
}
