using System.Diagnostics;
using System.ComponentModel;
using System.Text;

namespace IdleGearDefence.SpineExporter;

public interface ISpineProcessInvoker
{
    Task<SpineProcessResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string logPath,
        CancellationToken cancellationToken);
}

public sealed class SpineCliProcessInvoker : ISpineProcessInvoker
{
    public async Task<SpineProcessResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string logPath,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(workingDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(logPath) ?? workingDirectory);

        var standardOutput = new StringBuilder();
        var standardError = new StringBuilder();

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            },
            EnableRaisingEvents = true
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        try
        {
            if (!process.Start())
            {
                return new SpineProcessResult(-1, string.Empty, string.Empty, "无法启动 Spine CLI 进程。");
            }
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return new SpineProcessResult(-1, string.Empty, string.Empty, $"无法启动 Spine CLI：{exception.Message}");
        }

        var stdoutTask = ReadStreamAsync(process.StandardOutput, standardOutput, cancellationToken);
        var stderrTask = ReadStreamAsync(process.StandardError, standardError, cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        var output = standardOutput.ToString();
        var error = standardError.ToString();
        await File.WriteAllTextAsync(
            logPath,
            BuildLog(arguments, process.ExitCode, output, error),
            Encoding.UTF8,
            cancellationToken).ConfigureAwait(false);

        return new SpineProcessResult(process.ExitCode, output, error, null);
    }

    private static async Task ReadStreamAsync(
        StreamReader reader,
        StringBuilder destination,
        CancellationToken cancellationToken)
    {
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            destination.AppendLine(line);
        }
    }

    private static string BuildLog(
        IReadOnlyList<string> arguments,
        int exitCode,
        string output,
        string error)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"ExitCode: {exitCode}");
        builder.AppendLine($"Arguments: {string.Join(" ", arguments.Select(QuoteForLog))}");
        builder.AppendLine("--- stdout ---");
        builder.Append(output);
        builder.AppendLine("--- stderr ---");
        builder.Append(error);
        return builder.ToString();
    }

    private static string QuoteForLog(string value)
    {
        return value.Contains(' ') ? $"\"{value}\"" : value;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // The cancellation path must preserve the original cancellation exception.
        }
    }
}

public static class SpineCliCommandBuilder
{
    public static IReadOnlyList<string> BuildVersionArguments()
    {
        return new[] { "--version" };
    }

    public static IReadOnlyList<string> BuildExportArguments(
        string projectPath,
        string stagingDirectory,
        string exportSettingsPath,
        bool cleanAnimation)
    {
        var args = new List<string>
        {
            "-i", projectPath,
            "-o", stagingDirectory,
            "-e", exportSettingsPath
        };

        if (cleanAnimation)
            args.Add("-m");

        return args;
    }

    public static IReadOnlyList<string> BuildPackArguments(
        string inputDirectory,
        string stagingDirectory,
        string packSettingsPath,
        string? name)
    {
        var args = new List<string>
        {
            "-i", inputDirectory,
            "-o", stagingDirectory,
            "-p", packSettingsPath
        };

        if (!string.IsNullOrWhiteSpace(name))
        {
            args.Insert(4, "-n");
            args.Insert(5, name);
        }

        return args;
    }
}

public static class SpineVersionParser
{
    private static readonly System.Text.RegularExpressions.Regex VersionRegex =
        new(@"(?<!\d)(\d+\.\d+(?:\.\d+)?)(?!\d)",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public static string? Parse(string output)
    {
        return VersionRegex.Match(output).Groups[1].Value is { Length: > 0 } version
            ? version
            : null;
    }

    public static bool IsCompatible(string actual, string required)
    {
        if (string.IsNullOrWhiteSpace(required))
            return true;

        var actualParts = actual.Split('.');
        var requiredParts = required.Split('.');
        return actualParts.Length >= 2
            && requiredParts.Length >= 2
            && actualParts[0] == requiredParts[0]
            && actualParts[1] == requiredParts[1];
    }
}
