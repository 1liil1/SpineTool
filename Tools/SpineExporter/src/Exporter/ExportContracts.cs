namespace IdleGearDefence.SpineExporter;

public sealed class ExportRequest
{
    public string SpineProjectPath { get; init; } = string.Empty;

    public string OutputDirectory { get; init; } = string.Empty;
}

public sealed class ExportResult
{
    public bool Succeeded { get; init; }

    public string? OutputDirectory { get; init; }

    public string? StagingDirectory { get; init; }

    public IReadOnlyList<string> Artifacts { get; init; } = Array.Empty<string>();

    public string? ErrorMessage { get; init; }

    public static ExportResult Success(
        ExportRequest request,
        string outputDirectory,
        string stagingDirectory,
        IReadOnlyList<string> artifacts)
    {
        return new ExportResult
        {
            Succeeded = true,
            OutputDirectory = outputDirectory,
            StagingDirectory = stagingDirectory,
            Artifacts = artifacts
        };
    }

    public static ExportResult Failure(
        ExportRequest request,
        string? stagingDirectory,
        string errorMessage)
    {
        return new ExportResult
        {
            Succeeded = false,
            StagingDirectory = stagingDirectory,
            ErrorMessage = errorMessage
        };
    }
}

public sealed record NormalizedArtifact(
    string Kind,
    string SourcePath,
    string DestinationPath);

public sealed record SpineProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    string? ErrorMessage)
{
    public bool Succeeded => ExitCode == 0 && string.IsNullOrEmpty(ErrorMessage);
}
