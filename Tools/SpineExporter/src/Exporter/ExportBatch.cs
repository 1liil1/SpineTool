using System.Text.Json;

namespace IdleGearDefence.SpineExporter;

public sealed class ExportBatchFile
{
    public List<ExportBatchItem> Items { get; set; } = new();
}

public sealed class ExportBatchItem
{
    public string Project { get; set; } = string.Empty;
    public string OutputDirectory { get; set; } = string.Empty;
}

public static class ExportBatchRunner
{
    public static async Task<bool> RunAsync(string batchPath, string configPath, IExportLogger logger)
    {
        var batch = JsonSerializer.Deserialize<ExportBatchFile>(await File.ReadAllTextAsync(batchPath), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new ExportExecutionException("批处理文件为空。");
        var config = ExporterConfigLoader.Load(configPath);
        var exporter = new SpineExporter(config, new SpineCliProcessInvoker(), logger);
        var allSucceeded = true;
        foreach (var item in batch.Items)
        {
            logger.Info($"开始批处理资源：{item.Project}");
            var result = await exporter.ExportAsync(new ExportRequest
            {
                SpineProjectPath = item.Project,
                OutputDirectory = item.OutputDirectory
            });
            if (!result.Succeeded) allSucceeded = false;
        }
        return allSucceeded;
    }
}
