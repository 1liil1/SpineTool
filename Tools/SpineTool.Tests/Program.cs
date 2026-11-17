using System.IO;
using System.Text.Json.Nodes;
using IdleGearDefence.SpineExporter;
using IdleGearDefence.SpineValidator;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;
using ListBox = System.Windows.Controls.ListBox;
using IdleGearDefence.SpineTool;

using var temp = new TempDirectory();
var path = Path.Combine(temp.Path, "cards.json");
var store = new CardStore(path, temp.Path);
var cards = new List<SpineTaskCard>
{
    new()
    {
        Name = "Monster Test",
        Note = "怪物测试卡片",
        SpineProjectPath = "D:/art/Monster_1001.spine",
        ProjectGroup = "齿轮项目",
        Tags = new List<string> { "主线", "怪物" },
        OutputRootPath = "D:/art/output",
        ExportConfig = new ConfigReference { DisplayName = "Monster Export", Note = "PMA 配置", Path = "D:/config/export.json" },
        ValidationRules = new ConfigReference { DisplayName = "Monster Rules", Note = "基础规则", Path = "D:/config/rules.json" }
    }
};
var pair = new ConfigPairCard
{
    Name = "Monster 导出与检查",
    ExportConfigId = cards[0].ExportConfigId,
    ValidationRuleId = cards[0].ValidationRuleId
};
cards[0].ConfigPairId = pair.Id;
store.SaveData(new ProjectCardStoreData
{
    Projects = cards,
    ConfigPairs = new List<ConfigPairCard> { pair }
});
var loaded = store.Load();
if (loaded.Count != 1 || loaded[0].ExportConfig.Note != "PMA 配置" || loaded[0].ValidationRules.Note != "基础规则" || loaded[0].OutputRootPath != "D:/art/output" || loaded[0].Tags.Count != 2)
    throw new InvalidOperationException("CardStore round trip failed.");
var loadedData = store.LoadData();
if (loadedData.ConfigPairs.Count != 1 || loaded[0].ConfigPairId != loadedData.ConfigPairs[0].Id)
    throw new InvalidOperationException("Config pair round trip failed.");

var clone = loaded[0].Clone();
if (clone.Id == loaded[0].Id || clone.Name == loaded[0].Name)
    throw new InvalidOperationException("Card clone identity failed.");

var historyPath = Path.Combine(temp.Path, "output-directories.json");
var history = new OutputDirectoryHistory(historyPath);
for (var index = 0; index < 22; index++) history.Remember(Path.Combine(temp.Path, "output" + index));
history.Remember(Path.Combine(temp.Path, "output10") + Path.DirectorySeparatorChar);
var restoredHistory = new OutputDirectoryHistory(historyPath);
if (restoredHistory.Directories.Count != 20 || restoredHistory.Directories[0] != Path.Combine(temp.Path, "output10")
    || restoredHistory.Directories.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 20)
    throw new InvalidOperationException("Output history must persist recent directories, deduplicate paths and keep at most 20 items.");

var originalApp = Path.Combine(temp.Path, "original");
var movedApp = Path.Combine(temp.Path, "moved with spaces");
Directory.CreateDirectory(Path.Combine(originalApp, "config"));
File.WriteAllText(Path.Combine(originalApp, "portable.flag"), "");
foreach (var file in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "config"), "*.json", SearchOption.AllDirectories))
{
    var destination = Path.Combine(originalApp, "config", Path.GetRelativePath(Path.Combine(AppContext.BaseDirectory, "config"), file));
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    File.Copy(file, destination);
}
var portableStore = new CardStore(applicationDirectory: originalApp);
var portableData = portableStore.LoadData();
if (portableData.ConfigPairs.Count != 2 || portableData.ConfigPairs.All(x => x.Name != "齿轮士兵"))
    throw new InvalidOperationException("Fresh installation must contain both built-in configurations.");
portableStore.SaveData(portableData);
if (File.ReadAllText(portableStore.Path).Contains(originalApp, StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Portable configuration references must not store the original absolute application path.");
Directory.Move(originalApp, movedApp);
var movedStore = new CardStore(applicationDirectory: movedApp);
var movedData = movedStore.LoadData();
if (movedData.ExportConfigs.Any(x => !File.Exists(x.Path)) || movedData.ValidationRules.Any(x => !File.Exists(x.Path)))
    throw new InvalidOperationException("Built-in configurations must resolve after relocating the entire package.");

// Sharing must be self-contained, omit local executable paths and keep settings/rules independent.
var exchangeDirectory = Path.Combine(temp.Path, "exchange");
Directory.CreateDirectory(exchangeDirectory);
var exportSource = movedData.ExportConfigs.Single(x => x.Name == "齿轮士兵");
var ruleSource = movedData.ValidationRules.Single(x => x.Name == "齿轮士兵");
var exportShare = Path.Combine(exchangeDirectory, "soldier.spine-export.json");
var ruleShare = Path.Combine(exchangeDirectory, "soldier.spine-rules.json");
ConfigurationExchange.ExportSettings(exportSource, exportShare);
ConfigurationExchange.ExportRules(ruleSource, ruleShare);
var recipient = Path.Combine(temp.Path, "recipient");
var importedExport = ConfigurationExchange.ImportSettings(exportShare, recipient);
var importedRule = ConfigurationExchange.ImportRules(ruleShare, recipient);
var importedProfile = ExporterConfigLoader.Load(importedExport.Path);
if (importedProfile.Toolchain.SpineExecutable != "" || Path.IsPathRooted(importedProfile.Toolchain.StagingRoot))
    throw new InvalidOperationException("Shared export must not retain the sender's executable/staging paths.");
var visual = SpineVisualSettings.Load(importedExport.Path);
if (!visual.PremultiplyAlpha || !importedProfile.Export.TexturePacker.UsesEmbeddedAttachments)
    throw new InvalidOperationException("Imported settings lost PMA or Attachments mode.");
visual.MaxWidth = 2048;
visual.FilterMin = "Nearest";
SpineVisualSettings.Save(importedExport.Path, visual);
var restoredVisual = SpineVisualSettings.Load(importedExport.Path);
var importedJson = JsonNode.Parse(File.ReadAllText(importedProfile.Export.ResolveExportSettings(Path.GetDirectoryName(importedExport.Path)!, false)))!;
if (restoredVisual.MaxWidth != 2048 || restoredVisual.FilterMin != "Nearest" || importedJson["packAtlas"]!["pot"]?.GetValue<bool>() != true)
    throw new InvalidOperationException("Visual settings must persist while preserving unexposed pack options.");
var restoredRule = new ValidatorRulesLoader().Load(importedRule.Path);
if (!restoredRule.Settings.RequiredAnimations.Contains("attack") || restoredRule.Settings.Events[0].EventName != "attack" || !restoredRule.Settings.WarningBones.Contains("BulletPostion"))
    throw new InvalidOperationException("Shared rules lost animation, event or warning requirements.");
try { ConfigurationExchange.ImportRules(exportShare, recipient); throw new InvalidOperationException("Wrong file type accepted."); }
catch (InvalidDataException) { }
// The imported profile remains usable after the source bundle is no longer available.
Directory.Move(Path.Combine(movedApp, "config"), Path.Combine(movedApp, "config-hidden"));
SpineVisualSettings.Load(importedExport.Path);
new ValidatorRulesLoader().Load(importedRule.Path);
Directory.Move(Path.Combine(movedApp, "config-hidden"), Path.Combine(movedApp, "config"));
// Legacy pair-only cards migrate once; an independent selection must win over a stale pair.
var migrationStore = new CardStore(Path.Combine(temp.Path, "migration.json"), movedApp);
var legacyPair = movedData.ConfigPairs.Single(x => x.Name == "齿轮士兵");
var noneExport = movedData.ExportConfigs.Single(x => x.Name == "无");
migrationStore.SaveData(new ProjectCardStoreData
{
    ExportConfigs = movedData.ExportConfigs,
    ValidationRules = movedData.ValidationRules,
    ConfigPairs = movedData.ConfigPairs,
    Projects = new() { new SpineTaskCard { Name = "legacy", ConfigPairId = legacyPair.Id }, new SpineTaskCard { Name = "independent", ConfigPairId = legacyPair.Id, ExportConfigId = noneExport.Id, ValidationRuleId = legacyPair.ValidationRuleId } }
});
var migrated = migrationStore.LoadData();
if (migrated.Projects[0].ExportConfigId != legacyPair.ExportConfigId || migrated.Projects[1].ExportConfigId != noneExport.Id)
    throw new InvalidOperationException("Legacy pair migration changed an independent selection.");

// A directory may contain multiple complete exports and a shared atlas export.
var multiDirectory = Path.Combine(temp.Path, "multi resources");
Directory.CreateDirectory(multiDirectory);
foreach (var name in new[] { "Role_015WindGoddess", "Role_015WindGoddess_bullet", "Role_015WindGoddess_skillbuff" })
{
    File.WriteAllText(Path.Combine(multiDirectory, name + ".json"), "{\"skeleton\":{\"spine\":\"4.1\"},\"bones\":[{\"name\":\"root\"}]}");
    File.WriteAllBytes(Path.Combine(multiDirectory, name + ".skel.bytes"), new byte[] { 0, 1, 2 });
    File.WriteAllText(Path.Combine(multiDirectory, name + ".atlas.txt"), name + ".png\nsize: 2,2\n");
}
File.WriteAllText(Path.Combine(multiDirectory, "preview-manifest.json"), "{\"displayMode\":\"SkeletonAnimation\"}");
File.WriteAllText(Path.Combine(multiDirectory, "rules.json"), "{\"settings\":{}}");
var resources = PreviewResourceCatalog.Discover(multiDirectory);
if (resources.Count != 3 || resources.Any(x => x.SkeletonPaths.Length != 2 || Path.GetFileName(x.AtlasPath) != x.Name + ".atlas.txt" || x.Error != ""))
    throw new InvalidOperationException("Catalog must group alternate data formats and pair each resource with its own atlas.");
var chosen = PreviewResourceCatalog.Select(resources, "Role_015WindGoddess_bullet", new PreviewManifest());
if (chosen.Name != "Role_015WindGoddess_bullet" || !chosen.SkeletonPaths[0].EndsWith(".skel.bytes"))
    throw new InvalidOperationException("Resource selection must keep the selected resource and prefer binary.");
File.WriteAllText(Path.Combine(multiDirectory, "orphan.json"), "{\"bones\":[]}");
var orphan = PreviewResourceCatalog.Discover(multiDirectory).Single(x => x.Name == "orphan");
if (orphan.Error == "" || orphan.AtlasPath != null) throw new InvalidOperationException("An ambiguous atlas must never be paired with an unrelated resource.");
File.Delete(Path.Combine(multiDirectory, "orphan.json"));
File.Delete(Path.Combine(multiDirectory, "Role_015WindGoddess_bullet.json"));
File.Delete(Path.Combine(multiDirectory, "Role_015WindGoddess_bullet.skel.bytes"));
var refreshed = PreviewResourceCatalog.Discover(multiDirectory);
if (refreshed.Count != 2 || PreviewResourceCatalog.Select(refreshed, "Role_015WindGoddess_bullet", new PreviewManifest()).Name == "Role_015WindGoddess_bullet")
    throw new InvalidOperationException("Catalog must refresh removals and select an available resource.");
var sharedDirectory = Path.Combine(temp.Path, "shared atlas");
Directory.CreateDirectory(sharedDirectory);
File.WriteAllText(Path.Combine(sharedDirectory, "shared.atlas"), "shared.png\nsize: 2,2\n");
foreach (var name in new[] { "one", "two" }) File.WriteAllText(Path.Combine(sharedDirectory, name + ".json"), "{\"bones\":[]}");
if (PreviewResourceCatalog.Discover(sharedDirectory).Any(x => Path.GetFileName(x.AtlasPath) != "shared.atlas" || x.Error != ""))
    throw new InvalidOperationException("Multiple skeletons must support one shared atlas.");

PreviewUpgradeChecks.Run(temp.Path);

Exception? uiFailure = null;
var uiThread = new Thread(() =>
{
    try
    {
        var renderWidth = int.TryParse(Environment.GetEnvironmentVariable("SPINETOOL_TEST_WIDTH"), out var configuredWidth) ? configuredWidth : 1280;
        var window = new MainWindow(movedStore);
        window.Width = renderWidth;
        window.Height = 900;
        window.Measure(new System.Windows.Size(renderWidth, 900));
        window.Arrange(new System.Windows.Rect(0, 0, renderWidth, 900));
        window.UpdateLayout();
        var exportSelector = (ComboBox)window.FindName("TaskExportConfigComboBox");
        var ruleSelector = (ComboBox)window.FindName("TaskValidationRuleComboBox");
        var outputSelector = (ComboBox)window.FindName("OutputPathComboBox");
        if (exportSelector.Items.Count != 2 || ruleSelector.Items.Count != 2 || (exportSelector.SelectedItem as ExportConfigCard)?.Name != "无" || (ruleSelector.SelectedItem as ValidationRuleCard)?.Name != "无")
            throw new InvalidOperationException("New project card must select separate bundled defaults.");
        exportSelector.SelectedItem = exportSelector.Items.Cast<ExportConfigCard>().Single(x => x.Name == "齿轮士兵");
        if ((ruleSelector.SelectedItem as ValidationRuleCard)?.Name != "无") throw new InvalidOperationException("Selecting export changed validation rules.");
        ruleSelector.SelectedItem = ruleSelector.Items.Cast<ValidationRuleCard>().Single(x => x.Name == "齿轮士兵");
        exportSelector.SelectedItem = exportSelector.Items.Cast<ExportConfigCard>().Single(x => x.Name == "无");
        var rulesPath = ((TextBox)window.FindName("ValidatorRulesTextBox")).Text;
        if (!rulesPath.EndsWith("Rules.GearSoldier.json") || (exportSelector.SelectedItem as ExportConfigCard)?.Name != "无" || !outputSelector.IsEditable || window.FindName("PreviewHistoryComboBox") != null)
            throw new InvalidOperationException("Independent switching, editable output history or hidden preview history failed.");
        var savedCard = movedStore.LoadData().Projects.Single();
        if (savedCard.ExportConfigId != noneExport.Id || savedCard.ValidationRuleId != legacyPair.ValidationRuleId || savedCard.ConfigPairId != "")
            throw new InvalidOperationException("Independent task selections must persist without a pair.");
        var exportManager = (ListBox)window.FindName("ExportConfigListBox");
        var ruleManager = (ListBox)window.FindName("ValidationRuleListBox");
        exportManager.SelectedItem = exportManager.Items.Cast<ExportConfigCard>().Single(x => x.Name == "齿轮士兵");
        ruleManager.SelectedItem = ruleManager.Items.Cast<ValidationRuleCard>().Single(x => x.Name == "齿轮士兵");
        var ruleBeforeExportEdit = File.ReadAllText(((ValidationRuleCard)ruleManager.SelectedItem).Path);
        ((TextBox)window.FindName("PackMaxWidthTextBox")).Text = "2048";
        ((TextBox)window.FindName("PackScaleTextBox")).Text = "0.5";
        ((System.Windows.Controls.CheckBox)window.FindName("PackMultipleOfFourCheckBox")).IsChecked = true;
        ((System.Windows.Controls.CheckBox)window.FindName("PackSquareCheckBox")).IsChecked = true;
        ((System.Windows.Controls.CheckBox)window.FindName("PackBleedCheckBox")).IsChecked = true;
        window.UpdateLayout();
        if (((TextBox)window.FindName("PackScaleDetailTextBox")).Text != "0.5" || ((System.Windows.Controls.CheckBox)window.FindName("PackSquareDetailCheckBox")).IsChecked != true)
            throw new InvalidOperationException("Quick settings and Spine-style detail controls must stay synchronized.");
        ((System.Windows.Controls.Button)window.FindName("SaveExportConfigButton")).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        var exportAfterEdit = (ExportConfigCard)exportManager.SelectedItem;
        if (SpineVisualSettings.Load(exportAfterEdit.Path).MaxWidth != 2048 || File.ReadAllText(((ValidationRuleCard)ruleManager.SelectedItem).Path) != ruleBeforeExportEdit)
            throw new InvalidOperationException("Saving export options must not modify validation rules.");
        var commonOptions = SpineVisualSettings.Load(exportAfterEdit.Path);
        if (commonOptions.Scale != 0.5f || !commonOptions.MultipleOfFour || !commonOptions.Square || !commonOptions.Bleed)
            throw new InvalidOperationException("Artist common pack settings must persist.");
        var sharedQuickSettings = Path.Combine(exchangeDirectory, "common.spine-export.json");
        ConfigurationExchange.ExportSettings(exportAfterEdit, sharedQuickSettings);
        var importedQuick = ConfigurationExchange.ImportSettings(sharedQuickSettings, recipient);
        var restoredQuick = SpineVisualSettings.Load(importedQuick.Path);
        if (restoredQuick.Scale != 0.5f || !restoredQuick.MultipleOfFour || !restoredQuick.Square || !restoredQuick.Bleed || !restoredQuick.PremultiplyAlpha)
            throw new InvalidOperationException("Common artist settings must survive sharing.");
        var exportPathAfterEdit = exportAfterEdit.Path;
        var exportTextAfterEdit = File.ReadAllText(exportAfterEdit.Path);
        ((TextBox)window.FindName("WarningBonesTextBox")).Text = "BulletPostion, TestMuzzle";
        ((System.Windows.Controls.Button)window.FindName("SaveValidationRuleButton")).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        var ruleAfterEdit = (ValidationRuleCard)ruleManager.SelectedItem;
        if (!new ValidatorRulesLoader().Load(ruleAfterEdit.Path).Settings.WarningBones.Contains("TestMuzzle") || exportAfterEdit.Path != exportPathAfterEdit || File.ReadAllText(exportAfterEdit.Path) != exportTextAfterEdit)
            throw new InvalidOperationException("Saving validation rules must not modify export settings.");
        var savedPresets = movedStore.LoadData();
        if (savedPresets.ExportConfigs.Single(x => x.Id == exportAfterEdit.Id).Path != exportAfterEdit.Path || savedPresets.ValidationRules.Single(x => x.Id == ruleAfterEdit.Id).Path != ruleAfterEdit.Path)
            throw new InvalidOperationException("Edited built-in presets must retain their user-owned files after reload.");
        var tab = Environment.GetEnvironmentVariable("SPINETOOL_TEST_TAB");
        if (!string.IsNullOrEmpty(tab)) ((System.Windows.Controls.TabControl)window.FindName("MainTabs")).SelectedItem = window.FindName(tab);
        window.Width = renderWidth;
        window.Height = 900;
        window.Measure(new System.Windows.Size(renderWidth, 900));
        window.Arrange(new System.Windows.Rect(0, 0, renderWidth, 900));
        window.UpdateLayout();
        var imagePath = Environment.GetEnvironmentVariable("SPINETOOL_TEST_RENDER");
        if (!string.IsNullOrWhiteSpace(imagePath))
        {
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(renderWidth, 900, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            var content = (System.Windows.FrameworkElement)window.Content;
            content.Measure(new System.Windows.Size(renderWidth, 900));
            content.Arrange(new System.Windows.Rect(0, 0, renderWidth, 900));
            content.UpdateLayout();
            bitmap.Render(content);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var stream = File.Create(imagePath);
            encoder.Save(stream);
        }
        window.Close();
    }
    catch (Exception exception) { uiFailure = exception; }
});
uiThread.SetApartmentState(ApartmentState.STA);
uiThread.Start();
uiThread.Join();
if (uiFailure != null) throw new InvalidOperationException("WPF smoke test failed.", uiFailure);

Console.WriteLine("SpineTool: migration, independent selections, self-contained sharing, common pack settings, multi-resource catalog, history, relocation and WPF smoke tests passed.");

sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SpineToolTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path)) Directory.Delete(Path, true);
    }
}
