using System.Text;
using IdleGearDefence.SpineValidator;

using var temp = new TempDirectory();
File.WriteAllText(Path.Combine(temp.Path, "Test.json"), "{\"skeleton\":{\"hash\":\"test\",\"spine\":\"4.1.24\"},\"bones\":[{\"name\":\"root\"}],\"slots\":[{\"name\":\"slot\",\"bone\":\"root\"}],\"animations\":{\"idle\":{}}}", Encoding.UTF8);
File.WriteAllText(Path.Combine(temp.Path, "Test.atlas.txt"), "page.png\nsize: 16,16\nformat: RGBA8888\nfilter: Linear,Linear\nrepeat: none\n", Encoding.UTF8);
File.WriteAllBytes(Path.Combine(temp.Path, "page.png"), BuildPngHeader(16, 16));

var rules = new ValidatorRules
{
    SchemaVersion = 2,
    Settings = new ValidatorProfile
    {
        RequireJson = true,
        RequireBinary = false,
        RequireAtlas = true,
        RequireTextures = true,
        RequiredAnimations = new List<string> { "idle" },
        RequiredBones = new List<string> { "root" },
        RequiredSlots = new List<string> { "slot" },
        WarningBones = new List<string> { "muzzle" },
        DataVersion = "4.1",
        NamingPattern = "^Test$"
    }
};

var report = new SpineValidatorEngine().Validate(temp.Path, rules);
Assert(report.Passed, string.Join("; ", report.Issues.Select(x => x.Message)));
Assert(report.Issues.Any(x => x.Code == "BONE_MISSING_WARNING" && x.Severity == ValidationSeverity.Warning), "Warning bone was not reported as warning.");

rules.Settings.RequiredAnimations = new List<string> { "attack" };
var failed = new SpineValidatorEngine().Validate(temp.Path, rules);
Assert(!failed.Passed && failed.Issues.Any(x => x.Code == "ANIMATION_MISSING"), "Missing animation was not reported.");

var disabledRules = new ValidatorRules { SchemaVersion = 2, Enabled = false };
var disabledReport = new SpineValidatorEngine().Validate(temp.Path, disabledRules);
Assert(disabledReport.Passed, "Disabled validation should not block.");

Console.WriteLine("Validator tests passed.");

using var discoveryRoot = new TempDirectory();
var monsterDirectory = Directory.CreateDirectory(Path.Combine(discoveryRoot.Path, "Monster", "Monster_1001")).FullName;
File.WriteAllText(Path.Combine(monsterDirectory, "Monster_1001.skel.json"), "{}");
File.WriteAllText(Path.Combine(monsterDirectory, "Monster_1001.atlas.txt"), "");
File.WriteAllBytes(Path.Combine(monsterDirectory, "Monster_1001.png"), BuildPngHeader(16, 16));
var unknownDirectory = Directory.CreateDirectory(Path.Combine(discoveryRoot.Path, "Misc", "Vfx_Test")).FullName;
File.WriteAllText(Path.Combine(unknownDirectory, "Vfx_Test.skel.json"), "{}");
File.WriteAllText(Path.Combine(unknownDirectory, "Vfx_Test.atlas.txt"), "");
var discovered = ResourcePackageDiscovery.Discover(discoveryRoot.Path);
Assert(discovered.Count == 2, "Resource discovery did not find both packages.");

static byte[] BuildPngHeader(int width, int height)
{
    var bytes = new byte[24];
    bytes[0] = 137; bytes[1] = 80; bytes[2] = 78; bytes[3] = 71;
    bytes[4] = 13; bytes[5] = 10; bytes[6] = 26; bytes[7] = 10;
    bytes[16] = (byte)(width >> 24); bytes[17] = (byte)(width >> 16); bytes[18] = (byte)(width >> 8); bytes[19] = (byte)width;
    bytes[20] = (byte)(height >> 24); bytes[21] = (byte)(height >> 16); bytes[22] = (byte)(height >> 8); bytes[23] = (byte)height;
    return bytes;
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SpineValidatorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path)) Directory.Delete(Path, true);
    }
}
