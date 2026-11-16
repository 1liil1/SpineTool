using System.Text.Json;

namespace IdleGearDefence.SpineValidator;

public sealed class ValidatorBatchFile
{
    public List<ValidatorBatchItem> Items { get; set; } = new();
}

public sealed class ValidatorBatchItem
{
    public string Input { get; set; } = string.Empty;
    public string Report { get; set; } = string.Empty;
}

public static class ValidatorBatchRunner
{
    public static async Task<bool> RunAsync(string batchPath, string rulesPath)
    {
        var batch = JsonSerializer.Deserialize<ValidatorBatchFile>(await File.ReadAllTextAsync(batchPath), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new ValidatorExecutionException("批处理文件为空。");
        var rules = new ValidatorRulesLoader().Load(rulesPath);
        var engine = new SpineValidatorEngine();
        var allPassed = true;
        foreach (var item in batch.Items)
        {
            var report = engine.Validate(item.Input, rules);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(item.Report)) ?? Directory.GetCurrentDirectory());
            await File.WriteAllTextAsync(item.Report, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            if (!report.Passed) allPassed = false;
        }
        return allPassed;
    }
}
