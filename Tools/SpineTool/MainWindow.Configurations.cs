using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using IdleGearDefence.SpineValidator;

namespace IdleGearDefence.SpineTool;

public partial class MainWindow
{
    private readonly ObservableCollection<EventRuleEditorRow> _eventRuleRows = new();
    private ExportConfigCard? SelectedExportConfig => ExportConfigListBox.SelectedItem as ExportConfigCard;
    private ValidationRuleCard? SelectedValidationRule => ValidationRuleListBox.SelectedItem as ValidationRuleCard;
    private string ConfigurationStorage => Path.Combine(_cardStore.DataDirectory, "Configurations");

    private void ManageExportConfig_Click(object sender, RoutedEventArgs e)
    {
        ExportConfigListBox.SelectedItem = TaskExportConfigComboBox.SelectedItem;
        MainTabs.SelectedItem = ExportConfigurationTab;
    }

    private void ManageValidationRule_Click(object sender, RoutedEventArgs e)
    {
        ValidationRuleListBox.SelectedItem = TaskValidationRuleComboBox.SelectedItem;
        MainTabs.SelectedItem = ValidationConfigurationTab;
    }

    private void ExportConfigListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var config = SelectedExportConfig;
        ExportEditorNameTextBox.Text = config?.Name ?? "";
        ExportEditorNoteTextBox.Text = config?.Note ?? "";
        ExportEditorPathTextBox.Text = config?.Path ?? "";
        LoadVisualSettings(config);
    }

    private void ValidationRuleListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var rule = SelectedValidationRule;
        RuleEditorNameTextBox.Text = rule?.Name ?? "";
        RuleEditorNoteTextBox.Text = rule?.Note ?? "";
        RuleEditorPathTextBox.Text = rule?.Path ?? "";
        EventRulesDataGrid.ItemsSource = _eventRuleRows;
        _eventRuleRows.Clear();
        if (rule == null) { RulesEditorStatusTextBlock.Text = "请选择验证规则。"; return; }
        try
        {
            var loaded = new ValidatorRulesLoader().Load(rule.Path);
            var settings = loaded.Settings;
            RulesEnabledCheckBox.IsChecked = loaded.Enabled;
            RulesFailureDialogCheckBox.IsChecked = loaded.ShowFailureDialog;
            RequiredAnimationsTextBox.Text = string.Join(", ", settings.RequiredAnimations ?? new());
            RequiredBonesTextBox.Text = string.Join(", ", settings.RequiredBones ?? new());
            RequiredSlotsTextBox.Text = string.Join(", ", settings.RequiredSlots ?? new());
            WarningBonesTextBox.Text = string.Join(", ", settings.WarningBones ?? new());
            RulesVersionTextBox.Text = settings.DataVersion ?? "";
            RulesNamingTextBox.Text = settings.NamingPattern ?? "";
            RulesRequireJsonCheckBox.IsChecked = settings.RequireJson;
            RulesRequireBinaryCheckBox.IsChecked = settings.RequireBinary;
            RulesRequireAtlasCheckBox.IsChecked = settings.RequireAtlas;
            RulesRequireTexturesCheckBox.IsChecked = settings.RequireTextures;
            foreach (var item in settings.Events ?? new()) _eventRuleRows.Add(new EventRuleEditorRow(item));
            RulesEditorStatusTextBlock.Text = BuildValidationRulesSummary(rule.Path);
        }
        catch (Exception exception) { RulesEditorStatusTextBlock.Text = "读取失败：" + exception.Message; }
    }

    private void SaveExportConfig_Click(object sender, RoutedEventArgs e) => RunConfigurationAction(() => SaveExportEditor());
    private void SaveValidationRule_Click(object sender, RoutedEventArgs e) => RunConfigurationAction(() => SaveRuleEditor());

    private ExportConfigCard SaveExportEditor()
    {
        var card = SelectedExportConfig ?? throw new InvalidOperationException("请先选择导出配置。");
        // Save to user-owned files so a local rebuild cannot overwrite the edited presets.
        var name = Require(ExportEditorNameTextBox.Text.Trim(), "导出配置名称");
        var copy = ConfigurationExchange.CopySettings(card, ConfigurationStorage);
        SaveVisualSettings(copy);
        card.Path = copy.Path;
        card.Name = name;
        card.Note = ExportEditorNoteTextBox.Text.Trim();
        RefreshConfigurationReferences();
        ExportEditorPathTextBox.Text = card.Path;
        LoadVisualSettings(card);
        SetStatus("导出配置已保存；验证规则保持独立");
        return card;
    }

    private ValidationRuleCard SaveRuleEditor()
    {
        var card = SelectedValidationRule ?? throw new InvalidOperationException("请先选择验证规则。");
        if (!EventRulesDataGrid.CommitEdit(DataGridEditingUnit.Cell, true) || !EventRulesDataGrid.CommitEdit(DataGridEditingUnit.Row, true))
            throw new InvalidOperationException("请先完成当前事件规则的编辑。");
        var name = Require(RuleEditorNameTextBox.Text.Trim(), "验证规则名称");
        var events = _eventRuleRows.Where(x => !x.IsEmpty).Select(x => x.ToRule()).ToList();
        var naming = RulesNamingTextBox.Text.Trim();
        if (naming.Length > 0) _ = new Regex(naming);
        var settings = new ValidatorProfile
        {
            RequiredAnimations = ParseRuleNames(RequiredAnimationsTextBox.Text),
            RequiredBones = ParseRuleNames(RequiredBonesTextBox.Text),
            RequiredSlots = ParseRuleNames(RequiredSlotsTextBox.Text),
            WarningBones = ParseRuleNames(WarningBonesTextBox.Text),
            DataVersion = RulesVersionTextBox.Text.Trim(),
            NamingPattern = naming,
            RequireJson = RulesRequireJsonCheckBox.IsChecked,
            RequireBinary = RulesRequireBinaryCheckBox.IsChecked,
            RequireAtlas = RulesRequireAtlasCheckBox.IsChecked,
            RequireTextures = RulesRequireTexturesCheckBox.IsChecked,
            Events = events
        };
        var copy = ConfigurationExchange.CopyRules(card, ConfigurationStorage);
        var node = JsonNode.Parse(File.ReadAllText(copy.Path))!.AsObject();
        node["enabled"] = RulesEnabledCheckBox.IsChecked == true;
        node["showFailureDialog"] = RulesFailureDialogCheckBox.IsChecked == true;
        var settingsNode = node["settings"]!.AsObject();
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
        var changed = JsonSerializer.SerializeToNode(settings, options)!.AsObject();
        foreach (var property in changed) settingsNode[property.Key] = property.Value?.DeepClone();
        File.WriteAllText(copy.Path, node.ToJsonString(options));
        new ValidatorRulesLoader().Load(copy.Path);
        card.Name = name;
        card.Note = RuleEditorNoteTextBox.Text.Trim();
        card.Path = copy.Path;
        RefreshConfigurationReferences();
        RuleEditorPathTextBox.Text = card.Path;
        RulesEditorStatusTextBlock.Text = BuildValidationRulesSummary(card.Path);
        SetStatus("验证规则已保存；导出配置保持独立");
        return card;
    }

    private void RefreshConfigurationReferences()
    {
        foreach (var card in _cards)
        {
            var export = _exportConfigs.FirstOrDefault(x => x.Id == card.ExportConfigId);
            var rule = _validationRules.FirstOrDefault(x => x.Id == card.ValidationRuleId);
            if (export != null) card.ExportConfig = new ConfigReference { DisplayName = export.Name, Note = export.Note, Path = export.Path };
            if (rule != null) card.ValidationRules = new ConfigReference { DisplayName = rule.Name, Note = rule.Note, Path = rule.Path };
        }
        ExportConfigListBox.Items.Refresh();
        ValidationRuleListBox.Items.Refresh();
        TaskExportConfigComboBox.Items.Refresh();
        TaskValidationRuleComboBox.Items.Refresh();
        RefreshTaskConfigurationDetails();
        SaveStore();
    }

    private void NewExportConfig_Click(object sender, RoutedEventArgs e) => RunConfigurationAction(() =>
    {
        var source = SelectedExportConfig ?? _exportConfigs.FirstOrDefault() ?? throw new InvalidOperationException("请先导入一份导出配置作为模板。");
        var copy = ConfigurationExchange.CopySettings(source, ConfigurationStorage);
        copy.Name = UniqueName(source.Name + " 副本", _exportConfigs.Select(x => x.Name));
        _exportConfigs.Add(copy);
        ExportConfigListBox.SelectedItem = copy;
        SaveStore();
        SetStatus("已建立独立的导出配置副本");
    });

    private void NewValidationRule_Click(object sender, RoutedEventArgs e) => RunConfigurationAction(() =>
    {
        var source = SelectedValidationRule ?? _validationRules.FirstOrDefault() ?? throw new InvalidOperationException("请先导入一份验证规则作为模板。");
        var copy = ConfigurationExchange.CopyRules(source, ConfigurationStorage);
        copy.Name = UniqueName(source.Name + " 副本", _validationRules.Select(x => x.Name));
        _validationRules.Add(copy);
        ValidationRuleListBox.SelectedItem = copy;
        SaveStore();
        SetStatus("已建立独立的验证规则副本");
    });

    private void ImportExportConfig_Click(object sender, RoutedEventArgs e) => RunConfigurationAction(() =>
    {
        var path = SelectConfigurationFile("导入导出配置");
        if (path == null) return;
        var card = ConfigurationExchange.ImportSettings(path, ConfigurationStorage);
        card.Name = UniqueName(card.Name, _exportConfigs.Select(x => x.Name));
        _exportConfigs.Add(card);
        ExportConfigListBox.SelectedItem = card;
        SaveStore();
        SetStatus("导出配置已导入，可在 Spine 工程中单独选择");
    });

    private void ImportValidationRule_Click(object sender, RoutedEventArgs e) => RunConfigurationAction(() =>
    {
        var path = SelectConfigurationFile("导入验证规则");
        if (path == null) return;
        var card = ConfigurationExchange.ImportRules(path, ConfigurationStorage);
        card.Name = UniqueName(card.Name, _validationRules.Select(x => x.Name));
        _validationRules.Add(card);
        ValidationRuleListBox.SelectedItem = card;
        SaveStore();
        SetStatus("验证规则已导入，可在 Spine 工程中单独选择");
    });

    private void ShareExportConfig_Click(object sender, RoutedEventArgs e) => RunConfigurationAction(() =>
    {
        var card = SaveExportEditor();
        var path = SelectShareDestination(card.Name, ".spine-export.json");
        if (path == null) return;
        ConfigurationExchange.ExportSettings(card, path);
        SetStatus("导出设置分享文件已保存：" + path);
    });

    private void ShareValidationRule_Click(object sender, RoutedEventArgs e) => RunConfigurationAction(() =>
    {
        var card = SaveRuleEditor();
        var path = SelectShareDestination(card.Name, ".spine-rules.json");
        if (path == null) return;
        ConfigurationExchange.ExportRules(card, path);
        SetStatus("验证规则分享文件已保存：" + path);
    });

    private void DeleteExportConfig_Click(object sender, RoutedEventArgs e) => RunConfigurationAction(() =>
    {
        var card = SelectedExportConfig;
        if (card == null) return;
        if (_cards.Any(x => x.ExportConfigId == card.Id)) throw new InvalidOperationException("该导出配置仍被工程卡片使用，请先切换卡片的导出配置。");
        if (card.Id.StartsWith("builtin-", StringComparison.Ordinal)) throw new InvalidOperationException("内置配置请保留，可建立副本后修改。");
        _exportConfigs.Remove(card);
        SaveStore();
        ExportConfigListBox.SelectedIndex = 0;
        SetStatus("导出配置已移除");
    });

    private void DeleteValidationRule_Click(object sender, RoutedEventArgs e) => RunConfigurationAction(() =>
    {
        var card = SelectedValidationRule;
        if (card == null) return;
        if (_cards.Any(x => x.ValidationRuleId == card.Id)) throw new InvalidOperationException("该验证规则仍被工程卡片使用，请先切换卡片的验证规则。");
        if (card.Id.StartsWith("builtin-", StringComparison.Ordinal)) throw new InvalidOperationException("内置规则请保留，可建立副本后修改。");
        _validationRules.Remove(card);
        SaveStore();
        ValidationRuleListBox.SelectedIndex = 0;
        SetStatus("验证规则已移除");
    });

    private void OpenExportEditorDirectory_Click(object sender, RoutedEventArgs e) => OpenDirectory(Path.GetDirectoryName(SelectedExportConfig?.Path));
    private void OpenRuleEditorDirectory_Click(object sender, RoutedEventArgs e) => OpenDirectory(Path.GetDirectoryName(SelectedValidationRule?.Path));

    private void RunConfigurationAction(Action action)
    {
        try { action(); }
        catch (Exception exception)
        {
            _logger.Error(exception.Message);
            SetStatus(exception.Message);
            if (MainTabs.SelectedItem == ValidationConfigurationTab) RulesEditorStatusTextBlock.Text = "操作失败：" + exception.Message;
            else ExportSettingsStatusTextBlock.Text = "操作失败：" + exception.Message;
        }
    }

    private string? SelectConfigurationFile(string title)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = title, Filter = "配置或规则 (*.json)|*.json" };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private string? SelectShareDestination(string name, string suffix)
    {
        var safeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
        var dialog = new Microsoft.Win32.SaveFileDialog { Title = "导出分享文件", FileName = safeName + suffix, Filter = "配置分享文件 (*.json)|*.json", DefaultExt = ".json" };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private static string UniqueName(string name, IEnumerable<string> names)
    {
        var existing = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = name;
        for (var index = 2; existing.Contains(result); index++) result = $"{name} ({index})";
        return result;
    }

    private static List<string> ParseRuleNames(string text)
        => text.Split(new[] { ',', '，', ';', '；', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal).ToList();
}

public sealed class EventRuleEditorRow
{
    public string Animation { get; set; } = "";
    public string EventName { get; set; } = "";
    public string MinCount { get; set; } = "1";
    public string MaxCount { get; set; } = "";
    public string ExpectedTime { get; set; } = "";
    public string TimeTolerance { get; set; } = "0.001";
    public bool IsEmpty => string.IsNullOrWhiteSpace(Animation) && string.IsNullOrWhiteSpace(EventName)
        && string.IsNullOrWhiteSpace(MaxCount) && string.IsNullOrWhiteSpace(ExpectedTime)
        && (string.IsNullOrWhiteSpace(MinCount) || MinCount == "1")
        && (string.IsNullOrWhiteSpace(TimeTolerance) || TimeTolerance == "0.001");
    public EventRuleEditorRow() { }
    public EventRuleEditorRow(AnimationEventRule rule)
    {
        Animation = rule.Animation; EventName = rule.EventName;
        MinCount = rule.MinCount.ToString(CultureInfo.InvariantCulture);
        MaxCount = rule.MaxCount?.ToString(CultureInfo.InvariantCulture) ?? "";
        ExpectedTime = rule.ExpectedTime?.ToString(CultureInfo.InvariantCulture) ?? "";
        TimeTolerance = rule.TimeTolerance.ToString(CultureInfo.InvariantCulture);
    }

    public AnimationEventRule ToRule()
    {
        if (string.IsNullOrWhiteSpace(Animation) || string.IsNullOrWhiteSpace(EventName)) throw new InvalidDataException("事件规则的动画名和事件名均不能为空。");
        if (!int.TryParse(MinCount, out var min) || min < 0) throw new InvalidDataException("事件最少次数必须是非负整数。");
        int? max = string.IsNullOrWhiteSpace(MaxCount) ? null : int.Parse(MaxCount, CultureInfo.InvariantCulture);
        if (max < min) throw new InvalidDataException("事件最多次数不能小于最少次数。");
        float? time = string.IsNullOrWhiteSpace(ExpectedTime) ? null : float.Parse(ExpectedTime, CultureInfo.InvariantCulture);
        var tolerance = float.Parse(TimeTolerance, CultureInfo.InvariantCulture);
        if (time is { } t && (!float.IsFinite(t) || t < 0) || !float.IsFinite(tolerance) || tolerance < 0) throw new InvalidDataException("事件时间和容差必须是非负数字。");
        return new AnimationEventRule(Animation.Trim(), EventName.Trim(), min, max, time, tolerance);
    }
}
