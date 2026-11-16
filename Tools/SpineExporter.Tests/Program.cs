using System.Text;
using IdleGearDefence.SpineExporter;

var tests = new List<(string Name, Func<Task> Run)>
{
    ("Spine CLI command arguments", TestCommandArgumentsAsync),
    ("Artifact normalization", TestArtifactNormalizationAsync),
    ("Failed normalization does not publish partial output", TestFailedNormalizationAsync),
    ("Exporter orchestration with fake CLI", TestExporterOrchestrationAsync),
    ("Attachments export uses Spine embedded packing", TestEmbeddedAttachmentsOrchestrationAsync),
    ("Config loading", TestConfigLoadingAsync)
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {test.Name}: {exception.Message}");
    }
}

Console.WriteLine($"Tests: {tests.Count}, Failures: {failures}");
return failures == 0 ? 0 : 1;

static Task TestCommandArgumentsAsync()
{
    var args = SpineCliCommandBuilder.BuildExportArguments("C:/art/project.spine", "C:/stage", "C:/config/export.json", false);
    AssertSequence(args, "-i", "C:/art/project.spine", "-o", "C:/stage", "-e", "C:/config/export.json");

    var pack = SpineCliCommandBuilder.BuildPackArguments("C:/images", "C:/stage", "C:/config/pack.json", "Monster");
    AssertSequence(pack, "-i", "C:/images", "-o", "C:/stage", "-n", "Monster", "-p", "C:/config/pack.json");
    return Task.CompletedTask;
}

static Task TestArtifactNormalizationAsync()
{
    using var temp = new TempDirectory();
    var staging = Path.Combine(temp.Path, "stage");
    var output = Path.Combine(temp.Path, "output");
    Directory.CreateDirectory(staging);
    File.WriteAllText(Path.Combine(staging, "Monster.json"), "{\"skeleton\":{\"spine\":\"4.1.24\"}}", Encoding.UTF8);
    File.WriteAllBytes(Path.Combine(staging, "Monster.skel.json"), new byte[] { 0, 1, 2, 3 });
    File.WriteAllText(Path.Combine(staging, "Monster.atlas"), "Monster.png\nsize:10,10\n");
    File.WriteAllBytes(Path.Combine(staging, "Monster.png"), new byte[] { 137, 80, 78, 71 });

    var artifacts = ArtifactNormalizer.Normalize(
        staging,
        output,
        new OutputPolicy
        {
            OutputSubdirectoryTemplate = "{resourceType}/{resourceName}",
            AtlasExtension = ".atlas.txt",
            BinaryExtension = ".skel.bytes"
        },
        emitJson: true,
        emitBinary: true,
        requireAtlas: true,
        requireTextures: true);

    Assert(File.Exists(Path.Combine(output, "Monster.json")), "JSON was not normalized.");
    Assert(File.Exists(Path.Combine(output, "Monster.skel.bytes")), "SKEL was not normalized.");
    Assert(File.Exists(Path.Combine(output, "Monster.atlas.txt")), "Atlas extension was not normalized.");
    Assert(File.Exists(Path.Combine(output, "Monster.png")), "PNG was not copied.");
    Assert(artifacts.Count == 4, $"Expected four artifacts, got {artifacts.Count}.");
    return Task.CompletedTask;
}

static Task TestFailedNormalizationAsync()
{
    using var temp = new TempDirectory();
    var staging = Path.Combine(temp.Path, "stage");
    var output = Path.Combine(temp.Path, "output");
    Directory.CreateDirectory(staging);
    File.WriteAllText(Path.Combine(staging, "Monster.json"), "{\"skeleton\":{\"spine\":\"4.1.24\"}}", Encoding.UTF8);

    try
    {
        ArtifactNormalizer.Normalize(
            staging,
            output,
            new OutputPolicy
            {
                OutputSubdirectoryTemplate = "{resourceType}/{resourceName}",
                AtlasExtension = ".atlas.txt",
                BinaryExtension = ".skel.bytes"
            },
            emitJson: true,
            emitBinary: true,
            requireAtlas: true,
            requireTextures: true);
        throw new InvalidOperationException("Expected normalization to fail for incomplete output.");
    }
    catch (ExportExecutionException)
    {
        Assert(!Directory.Exists(output), "Incomplete output created a partial destination directory.");
    }

    return Task.CompletedTask;
}

static async Task TestExporterOrchestrationAsync()
{
    using var temp = new TempDirectory();
    var configDirectory = Path.Combine(temp.Path, "config");
    Directory.CreateDirectory(configDirectory);
    var settingsPath = Path.Combine(configDirectory, "export.json");
    File.WriteAllText(settingsPath, "{}");
    var projectPath = Path.Combine(temp.Path, "Monster.spine");
    File.WriteAllText(projectPath, "placeholder");
    var imagesDirectory = Directory.CreateDirectory(Path.Combine(temp.Path, "art", "Images with spaces")).FullName;

    var config = new ExporterConfig
    {
        SchemaVersion = 2,
        Toolchain = new ToolchainConfig
        {
            SpineExecutable = "Spine.com",
            RequiredSpineVersion = "4.1",
            StagingRoot = Path.Combine(temp.Path, "staging"),
            KeepStagingOnSuccess = true,
            KeepStagingOnFailure = true
        },
        Export = new ExportProfile
        {
            ExportSettingsFile = settingsPath,
            TexturePacker = new TexturePackerConfig { Mode = "Separate", SettingsFile = settingsPath, InputDirectory = "{imagesDirectory}" },
            RequirePremultipliedAlpha = true,
            Output = new OutputPolicy
            {
                OutputSubdirectoryTemplate = "{resourceName}",
                AtlasExtension = ".atlas.txt",
                BinaryExtension = ".skel.bytes"
            }
        }
    };

    var fake = new FakeSpineProcessInvoker();
    var exporter = new SpineExporter(config, fake, new SilentLogger());
    var result = await exporter.ExportAsync(new ExportRequest
    {
        SpineProjectPath = projectPath,
        OutputDirectory = Path.Combine(temp.Path, "published")
    });

    Assert(result.Succeeded, result.ErrorMessage ?? "Exporter failed.");
    Assert(fake.Commands.Count == 4, $"Expected version, two exports and texture pack, got {fake.Commands.Count}.");
    Assert(result.Artifacts.Count == 4, "Exporter did not return normalized artifacts.");
    var pack = fake.Commands.Single(x => x.Contains("-p"));
    Assert(pack[1] == imagesDirectory, "Image directory must resolve relative to the source project, including spaces.");
    Assert(pack[5] == "Monster", "Atlas must use project name when no pack name is specified.");
}

static async Task TestEmbeddedAttachmentsOrchestrationAsync()
{
    using var temp = new TempDirectory();
    var configDirectory = Path.Combine(temp.Path, "config");
    Directory.CreateDirectory(configDirectory);
    var settingsPath = Path.Combine(configDirectory, "export.json");
    File.WriteAllText(settingsPath, "{}");
    var projectPath = Path.Combine(temp.Path, "Monster.spine");
    File.WriteAllText(projectPath, "placeholder");
    var config = new ExporterConfig
    {
        SchemaVersion = 2,
        Toolchain = new ToolchainConfig
        {
            SpineExecutable = "Spine.com",
            RequiredSpineVersion = "4.1",
            StagingRoot = Path.Combine(temp.Path, "staging"),
            KeepStagingOnSuccess = true,
            KeepStagingOnFailure = true
        },
        Export = new ExportProfile
        {
            ExportSettingsFile = settingsPath,
            TexturePacker = new TexturePackerConfig { Mode = "EmbeddedAttachments" },
            RequirePremultipliedAlpha = true,
            Output = new OutputPolicy { OutputSubdirectoryTemplate = "{resourceName}" }
        }
    };

    var fake = new FakeSpineProcessInvoker();
    var result = await new SpineExporter(config, fake, new SilentLogger()).ExportAsync(new ExportRequest
    {
        SpineProjectPath = projectPath,
        OutputDirectory = Path.Combine(temp.Path, "published")
    });

    Assert(result.Succeeded, result.ErrorMessage ?? "Attachments exporter failed.");
    Assert(fake.Commands.Count == 3, $"Expected version and two exports, got {fake.Commands.Count}.");
    Assert(fake.Commands.Any(x => x.Contains(settingsPath)), "JSON export must use the saved export settings file.");
    Assert(fake.Commands.All(x => !x.Contains("-p")), "Attachments mode must not invoke separate Texture Packer.");
}

static Task TestConfigLoadingAsync()
{
    var repoRoot = FindRepositoryRoot();
    var path = Path.Combine(repoRoot, "Tools", "SpineExporter", "config", "SpineExporter.json");
    var config = ExporterConfigLoader.Load(path);
    Assert(config.SchemaVersion == 2, "Unexpected config schema.");
    var export = config.Export ?? throw new InvalidOperationException("Generic export settings missing.");
    Assert(export.TexturePacker.UsesEmbeddedAttachments, "Generic profile must use embedded Attachments packing.");
    return Task.CompletedTask;
}

static string FindRepositoryRoot()
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null && !Directory.Exists(Path.Combine(current.FullName, ".git")))
        current = current.Parent;
    return current?.FullName ?? throw new InvalidOperationException("Repository root not found.");
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void AssertSequence(IReadOnlyList<string> actual, params string[] expected)
{
    Assert(actual.SequenceEqual(expected), $"Unexpected arguments: {string.Join(" ", actual)}");
}

sealed class FakeSpineProcessInvoker : ISpineProcessInvoker
{
    public List<IReadOnlyList<string>> Commands { get; } = new();

    public async Task<SpineProcessResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string logPath,
        CancellationToken cancellationToken)
    {
        Commands.Add(arguments);
        Directory.CreateDirectory(workingDirectory);
        if (arguments.Contains("--version"))
            return new SpineProcessResult(0, "Spine 4.1.24", string.Empty, null);

        var outputIndex = Array.IndexOf(arguments.ToArray(), "-o");
        var output = arguments[outputIndex + 1];
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "Monster.json"), "{\"skeleton\":{\"spine\":\"4.1.24\",\"images\":\"art/Images with spaces\"}}", Encoding.UTF8);
        File.WriteAllBytes(Path.Combine(output, "Monster.skel.bytes"), new byte[] { 0, 1, 2 });
        File.WriteAllText(Path.Combine(output, "Monster.atlas.txt"), "Monster.png\nsize:10,10\npma: true\n");
        File.WriteAllBytes(Path.Combine(output, "Monster.png"), new byte[] { 137, 80, 78, 71 });
        await File.WriteAllTextAsync(logPath, "fake", cancellationToken);
        return new SpineProcessResult(0, string.Empty, string.Empty, null);
    }
}

sealed class SilentLogger : IExportLogger
{
    public void Info(string message) { }

    public void Warning(string message) { }

    public void Error(string message) { }
}

sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SpineExporterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
            Directory.Delete(Path, true);
    }
}
