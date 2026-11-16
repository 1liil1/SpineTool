using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using IdleGearDefence.SpineExporter;
using IdleGearDefence.SpineValidator;
using ExporterEngine = IdleGearDefence.SpineExporter.SpineExporter;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace IdleGearDefence.SpineTool;

public partial class MainWindow : Window
{
    private readonly UiLogger _logger;
    private readonly CardStore _cardStore;
    private readonly OutputDirectoryHistory _outputHistory;
    private readonly ObservableCollection<ExportConfigCard> _exportConfigs = new();
    private readonly ObservableCollection<ValidationRuleCard> _validationRules = new();
    private readonly ObservableCollection<ConfigPairCard> _configPairs = new();
    private readonly ObservableCollection<SpineTaskCard> _cards = new();
    private readonly ObservableCollection<BatchValidationRow> _batchRows = new();
    private readonly ObservableCollection<PreviewHistoryEntry> _previewHistory = new();
    private bool _loadingFilters;
    private readonly ICollectionView _cardsView;
    private readonly ICollectionView _batchView;
    private CancellationTokenSource? _operationCts;
    private SpineTaskCard? _selectedCard;
    private BatchValidationResult? _lastBatchResult;
    private string? _lastTemporaryDirectory;
    private bool _loadingCard;

    public ICollectionView CardsView => _cardsView;

    public MainWindow() : this(new CardStore()) { }

    public MainWindow(CardStore cardStore)
    {
        _cardStore = cardStore;
        _outputHistory = new OutputDirectoryHistory(Path.Combine(cardStore.DataDirectory, "output-directories.json"));
        _logger = new UiLogger(AppendLog);
        _cardsView = new ListCollectionView(_cards);
        _cardsView.Filter = CardMatchesSearch;
        _cardsView.SortDescriptions.Add(new SortDescription(nameof(SpineTaskCard.IsPinned), ListSortDirection.Descending));
        _cardsView.SortDescriptions.Add(new SortDescription(nameof(SpineTaskCard.LastUsedAt), ListSortDirection.Descending));
        _cardsView.SortDescriptions.Add(new SortDescription(nameof(SpineTaskCard.Name), ListSortDirection.Ascending));
        _batchView = new ListCollectionView(_batchRows);
        InitializeComponent();
        DataContext = this;
        CardListBox.ItemsSource = _cardsView;
        OutputPathComboBox.ItemsSource = _outputHistory.Directories;
        BatchDataGrid.ItemsSource = _batchView;
        ExportConfigListBox.ItemsSource = _exportConfigs;
        ValidationRuleListBox.ItemsSource = _validationRules;
        TaskExportConfigComboBox.ItemsSource = _exportConfigs;
        TaskValidationRuleComboBox.ItemsSource = _validationRules;
        LoadPreviewHistory();
        LoadCards();
        RefreshCardFilters();
        ExportConfigListBox.SelectedIndex = 0;
        ValidationRuleListBox.SelectedIndex = 0;
        UpdatePathStatuses();
    }

    private void LoadCards()
    {
        var data = _cardStore.LoadData();
        foreach (var config in data.ExportConfigs) _exportConfigs.Add(config);
        foreach (var rule in data.ValidationRules) _validationRules.Add(rule);
        foreach (var pair in data.ConfigPairs) _configPairs.Add(pair);
        foreach (var card in data.Projects)
        {
            _cards.Add(NormalizeCard(card));
        }
        if (_cards.Count == 0)
            NewCard_Click(this, new RoutedEventArgs());
        else
            CardListBox.SelectedIndex = 0;
    }

    private static SpineTaskCard NormalizeCard(SpineTaskCard card)
    {
        card.ExportConfig ??= new ConfigReference();
        card.ValidationRules ??= new ConfigReference();
        card.Id = string.IsNullOrWhiteSpace(card.Id) ? Guid.NewGuid().ToString("N") : card.Id;
        return card;
    }

    private void RefreshTaskConfigurationDetails()
    {
        var export = TaskExportConfigComboBox.SelectedItem as ExportConfigCard;
        var rule = TaskValidationRuleComboBox.SelectedItem as ValidationRuleCard;
        ExportConfigNameTextBox.Text = export?.Name ?? string.Empty;
        ExportConfigNoteTextBox.Text = export?.Note ?? string.Empty;
        ExporterConfigTextBox.Text = export?.Path ?? string.Empty;
        ValidationRulesNameTextBox.Text = rule?.Name ?? string.Empty;
        ValidationRulesNoteTextBox.Text = rule?.Note ?? string.Empty;
        ValidatorRulesTextBox.Text = rule?.Path ?? string.Empty;
        UpdatePathStatuses();
    }

    private void ApplyIndependentConfigurations(SpineTaskCard card)
    {
        card.ConfigPairId = string.Empty;
        if (TaskExportConfigComboBox.SelectedItem is ExportConfigCard export)
        {
            card.ExportConfigId = export.Id;
            card.ExportConfig = new ConfigReference { DisplayName = export.Name, Note = export.Note, Path = export.Path };
        }
        if (TaskValidationRuleComboBox.SelectedItem is ValidationRuleCard rule)
        {
            card.ValidationRuleId = rule.Id;
            card.ValidationRules = new ConfigReference { DisplayName = rule.Name, Note = rule.Note, Path = rule.Path };
        }
    }

    private void TaskConfiguration_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingCard) return;
        RefreshTaskConfigurationDetails();
        if (_selectedCard != null)
        {
            ApplyIndependentConfigurations(_selectedCard);
            SaveStore();
        }
    }

    private void SaveStore()
    {
        _cardStore.SaveData(new ProjectCardStoreData
        {
            Projects = _cards.ToList(),
            ExportConfigs = _exportConfigs.ToList(),
            ValidationRules = _validationRules.ToList(),
            ConfigPairs = _configPairs.ToList()
        });
    }

    private void CardListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CardListBox.SelectedItem is SpineTaskCard card)
        {
            _selectedCard = card;
            LoadCardToEditor(card);
        }
    }

    private void LoadCardToEditor(SpineTaskCard card)
    {
        _loadingCard = true;
        CardNameTextBox.Text = card.Name;
        CardNoteTextBox.Text = card.Note;
        ProjectPathTextBox.Text = card.SpineProjectPath;
        OutputPathComboBox.Text = card.OutputRootPath;
        ProjectGroupTextBox.Text = card.ProjectGroup;
        ProjectTagsTextBox.Text = string.Join(", ", card.Tags ?? new List<string>());
        TaskExportConfigComboBox.SelectedItem = _exportConfigs.FirstOrDefault(x => x.Id == card.ExportConfigId);
        TaskValidationRuleComboBox.SelectedItem = _validationRules.FirstOrDefault(x => x.Id == card.ValidationRuleId);
        RefreshTaskConfigurationDetails();
        CardStateTextBlock.Text = BuildCardStateText(card);
        _loadingCard = false;
        UpdatePathStatuses();
    }

    private void NewCard_Click(object sender, RoutedEventArgs e)
    {
        var card = new SpineTaskCard { Name = "新 Spine 任务" };
        card.ExportConfigId = (_exportConfigs.FirstOrDefault(x => x.Name == "无") ?? _exportConfigs.FirstOrDefault())?.Id ?? string.Empty;
        card.ValidationRuleId = (_validationRules.FirstOrDefault(x => x.Name == "无") ?? _validationRules.FirstOrDefault())?.Id ?? string.Empty;
        _cards.Add(card);
        CardListBox.SelectedItem = card;
        CardNameTextBox.Focus();
        CardNameTextBox.SelectAll();
    }

    private void SaveCard_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var card = _selectedCard ?? throw new InvalidOperationException("请先选择或新建卡片。");
            UpdateCardFromEditor(card);
            RememberOutputDirectory();
            SaveStore();
            RefreshCardFilters();
            _cardsView.Refresh();
            SetStatus("卡片已保存");
        }
        catch (Exception exception)
        {
            _logger.Error(exception.Message);
            SetStatus("卡片保存失败");
        }
    }

    private void SaveAsCard_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var source = _selectedCard ?? throw new InvalidOperationException("请先选择卡片。");
            UpdateCardFromEditor(source);
            var copy = source.Clone();
            _cards.Add(copy);
            _selectedCard = copy;
            CardListBox.SelectedItem = copy;
            SaveStore();
            RefreshCardFilters();
            SetStatus("已另存为新卡片");
        }
        catch (Exception exception) { _logger.Error(exception.Message); }
    }

    private void DeleteCard_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCard == null) return;
        if (System.Windows.MessageBox.Show(this, $"确定删除卡片“{_selectedCard.Name}”吗？", "删除卡片", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _cards.Remove(_selectedCard);
        SaveStore();
        _selectedCard = null;
        if (_cards.Count == 0) NewCard_Click(this, new RoutedEventArgs());
        else CardListBox.SelectedIndex = 0;
        SetStatus("卡片已删除");
    }

    private void PinCard_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCard == null) return;
        _selectedCard.IsPinned = !_selectedCard.IsPinned;
        SaveStore();
        _cardsView.Refresh();
        SetStatus(_selectedCard.IsPinned ? "卡片已置顶" : "已取消置顶");
    }

    private void UpdateCardFromEditor(SpineTaskCard card)
    {
        card.Name = string.IsNullOrWhiteSpace(CardNameTextBox.Text) ? "未命名 Spine 任务" : CardNameTextBox.Text.Trim();
        card.Note = CardNoteTextBox.Text.Trim();
        card.SpineProjectPath = ProjectPathTextBox.Text.Trim();
        card.ProjectGroup = string.IsNullOrWhiteSpace(ProjectGroupTextBox.Text) ? "齿轮项目" : ProjectGroupTextBox.Text.Trim();
        card.Tags = ParseTags(ProjectTagsTextBox.Text);
        card.OutputRootPath = OutputPathComboBox.Text.Trim();
        card.LastProjectWriteTimeUtc = File.Exists(card.SpineProjectPath) ? File.GetLastWriteTimeUtc(card.SpineProjectPath) : null;
        ApplyIndependentConfigurations(card);
    }

    private async void ExportOnly_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async token =>
        {
            var request = ReadRequest(requireExport: true);
            var temp = await ExportToTemporaryAsync(request, token);
            if (temp == null) { SetStatus("导出失败"); return; }
            var finalDirectory = PublishOutput(temp, request);
            SetStatus($"导出完成：{finalDirectory}");
        });
    }

    private async void ValidateOnly_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(_ =>
        {
            var request = ReadRequest(requireValidation: true);
            var target = ResolveCurrentResourceDirectory(request);
            var report = ValidateDirectory(target, request.Card);
            request.Card.LastValidationAt = DateTime.Now;
            request.Card.LastValidationPassed = report.Passed;
            SaveStore();
            SetStatus(report.Passed ? "验证通过" : "验证失败");
            return Task.CompletedTask;
        });
    }

    private void PreviewOnly_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var request = ReadRequest();
            LaunchPreviewer(ResolveCurrentResourceDirectory(request));
        }
        catch (Exception exception)
        {
            _logger.Error(exception.Message);
            SetStatus("预览启动失败");
        }
    }

    private async void RunSelectedOperations_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async token =>
        {
            if (ExportToggle.IsChecked != true && ValidateToggle.IsChecked != true && PreviewToggle.IsChecked != true)
                throw new InvalidOperationException("请至少启用一个操作。");

            var request = ReadRequest(requireExport: ExportToggle.IsChecked == true, requireValidation: ValidateToggle.IsChecked == true);
            string target;
            if (ExportToggle.IsChecked == true)
            {
                target = await ExportToTemporaryAsync(request, token) ?? throw new InvalidOperationException("导出失败，无法继续。");
            }
            else
            {
                target = ResolveCurrentResourceDirectory(request);
            }

            ValidationReport? report = null;
            if (ValidateToggle.IsChecked == true)
            {
                report = ValidateDirectory(target, request.Card);
                request.Card.LastValidationAt = DateTime.Now;
                request.Card.LastValidationPassed = report.Passed;
                if (!report.Passed)
                {
                    SetStatus("验证失败，临时资源保留");
                    if (PreviewToggle.IsChecked == true) LaunchPreviewer(target);
                    return;
                }
            }

            if (ExportToggle.IsChecked == true) target = PublishOutput(target, request);

            if (PreviewToggle.IsChecked == true) LaunchPreviewer(target);
            SetStatus(report == null ? "操作完成" : "导出和验证通过");
        });
    }

    private async Task<string?> ExportToTemporaryAsync(ToolRequest request, CancellationToken cancellationToken)
    {
        var config = LoadExportConfig(request.Card.ExportConfig.Path);
        var tempOutput = Path.Combine(Path.GetTempPath(), "IdleGearDefence", "SpineTool", Guid.NewGuid().ToString("N"));
        var result = await new ExporterEngine(config, new SpineCliProcessInvoker(), _logger).ExportAsync(new ExportRequest
        {
            SpineProjectPath = request.Card.SpineProjectPath,
            OutputDirectory = tempOutput
        }, cancellationToken);
        _lastTemporaryDirectory = result.OutputDirectory;
        return result.Succeeded ? result.OutputDirectory : null;
    }

    private string PublishOutput(string source, ToolRequest request)
    {
        var destination = GetResourceDirectory(request.OutputRoot, request.Card);
        CopyDirectory(source, destination);
        request.Card.LastExportAt = DateTime.Now;
        request.Card.LastUsedAt = DateTime.Now;
        request.Card.LastExportArtifactCount = Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories).Count();
        SaveStore();
        CardStateTextBlock.Text = BuildCardStateText(request.Card);
        _logger.Info("资源已输出：" + destination);
        return destination;
    }

    private ExporterConfig LoadExportConfig(string path)
    {
        var config = ExporterConfigLoader.Load(path);
        var executablePath = Path.Combine(_cardStore.DataDirectory, "spine-executable.txt");
        if (File.Exists(executablePath) && string.IsNullOrWhiteSpace(config.Toolchain.SpineExecutable))
            config.Toolchain.SpineExecutable = File.ReadAllText(executablePath).Trim();
        return config;
    }

    private void SelectSpineExecutable_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "选择 Spine 4.1 程序", Filter = "Spine 命令行程序 (Spine.com)|Spine.com|所有文件 (*.*)|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        Directory.CreateDirectory(_cardStore.DataDirectory);
        File.WriteAllText(Path.Combine(_cardStore.DataDirectory, "spine-executable.txt"), dialog.FileName);
        SetStatus("已设置 Spine 程序：" + dialog.FileName);
    }

    private string ResolveCurrentResourceDirectory(ToolRequest request)
    {
        var output = GetResourceDirectory(request.OutputRoot, request.Card);
        if (Directory.Exists(output)) return output;
        if (!string.IsNullOrWhiteSpace(_lastTemporaryDirectory) && Directory.Exists(_lastTemporaryDirectory)) return _lastTemporaryDirectory;
        throw new DirectoryNotFoundException($"找不到资源目录：{output}");
    }

    private async void ExportAndValidate_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async token =>
        {
            var request = ReadRequest(requireExport: true, requireValidation: true);
            var config = LoadExportConfig(request.Card.ExportConfig.Path);
            var tempOutput = Path.Combine(Path.GetTempPath(), "IdleGearDefence", "SpineTool", Guid.NewGuid().ToString("N"));
            var result = await new ExporterEngine(config, new SpineCliProcessInvoker(), _logger).ExportAsync(new ExportRequest
            {
                SpineProjectPath = request.Card.SpineProjectPath,
                OutputDirectory = tempOutput
            }, token);
            _lastTemporaryDirectory = result.OutputDirectory;
            if (!result.Succeeded) { SetStatus("导出失败"); return; }
            var report = ValidateDirectory(result.OutputDirectory!, request.Card);
            if (!report.Passed) { _logger.Error($"检查失败，临时资源保留在：{result.OutputDirectory}"); SetStatus("检查失败，未发布"); return; }
            var finalDirectory = GetResourceDirectory(request.OutputRoot, request.Card);
            CopyDirectory(result.OutputDirectory!, finalDirectory);
            request.Card.LastUsedAt = DateTime.Now;
            request.Card.LastExportAt = DateTime.Now;
            request.Card.LastExportArtifactCount = result.Artifacts.Count;
            request.Card.LastValidationAt = DateTime.Now;
            request.Card.LastValidationPassed = report.Passed;
            request.Card.LastProjectWriteTimeUtc = File.Exists(request.Card.SpineProjectPath) ? File.GetLastWriteTimeUtc(request.Card.SpineProjectPath) : null;
            SaveStore();
            _cardsView.Refresh();
            _logger.Info($"检查通过，已生成正式输出：{finalDirectory}");
            SetStatus("导出并检查通过");
        });
    }

    private async void ValidateExisting_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(_ =>
        {
            var request = ReadRequest(requireValidation: true);
            var report = ValidateDirectory(GetResourceDirectory(request.OutputRoot, request.Card), request.Card);
            request.Card.LastValidationAt = DateTime.Now;
            request.Card.LastValidationPassed = report.Passed;
            SaveStore();
            SetStatus(report.Passed ? "检查通过" : "检查失败");
            return Task.CompletedTask;
        });
    }

    private async void BatchValidate_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async token =>
        {
            var request = ReadRequest(requireValidation: true, batch: true);
            var root = Require(BatchRootTextBox.Text, "批量验证根目录");
            var progress = new Progress<BatchValidationProgress>(p =>
            {
                ProgressBar.IsIndeterminate = false;
                ProgressBar.Maximum = Math.Max(1, p.Total);
                ProgressBar.Value = p.Current;
                SetStatus($"批量检查 {p.Current}/{p.Total}：{p.Resource.ResourceName}");
            });
            _lastBatchResult = await new BatchValidationService().ValidateAsync(root, request.Card.ValidationRules.Path, token, progress);
            _batchRows.Clear();
            foreach (var item in _lastBatchResult.Items)
                _batchRows.Add(new BatchValidationRow { Resource = item.Resource, Report = item.Report, ErrorMessage = item.ErrorMessage });
            ApplyBatchFilter();
            request.Card.LastUsedAt = DateTime.Now;
            SaveStore();
            SetStatus(_lastBatchResult.Passed ? "批量检查通过" : "批量检查完成，存在失败项");
        });
    }

    private void ApplyBatchFilter()
    {
        _batchView.Filter = item => !BatchFailuresOnlyCheckBox.IsChecked.GetValueOrDefault() || item is BatchValidationRow row && row.Status != "通过";
    }

    private void BatchFilterChanged(object sender, RoutedEventArgs e) => ApplyBatchFilter();

    private void BrowseBatchRoot_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog { SelectedPath = BatchRootTextBox.Text };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) BatchRootTextBox.Text = dialog.SelectedPath;
    }

    private void BatchPreview_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is BatchValidationRow row) LaunchPreviewer(row.Resource.Directory);
    }

    private void BatchOpenDirectory_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is BatchValidationRow row) OpenDirectory(row.Resource.Directory);
    }

    private void BatchDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (BatchDataGrid.SelectedItem is BatchValidationRow row) LaunchPreviewer(row.Resource.Directory);
    }

    private void SaveBatchReport_Click(object sender, RoutedEventArgs e)
    {
        if (_lastBatchResult == null) return;
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "JSON (*.json)|*.json", FileName = "spine-validation-report.json" };
        if (dialog.ShowDialog(this) != true) return;
        File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(_lastBatchResult, new JsonSerializerOptions { WriteIndented = true }));
        SetStatus("汇总报告已保存");
    }

    private void OpenTemporaryDirectory_Click(object sender, RoutedEventArgs e) => OpenDirectory(_lastTemporaryDirectory);

    private void ImportPreview_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog();
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) LaunchPreviewer(dialog.SelectedPath);
    }

    private void LaunchPreviewer(string directory)
    {
        try
        {
            var executable = Environment.GetEnvironmentVariable("SPINE_PREVIEWER_EXE");
            if (string.IsNullOrWhiteSpace(executable))
            {
                executable = Path.Combine(AppContext.BaseDirectory, "Previewer", "SpinePreviewer.exe");
                if (!File.Exists(executable)) executable = Path.Combine(AppContext.BaseDirectory, "SpinePreviewer.exe");
            }
            if (!File.Exists(executable)) throw new FileNotFoundException("找不到 Previewer EXE。请设置 SPINE_PREVIEWER_EXE。", executable);
            var mode = (PreviewModeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "SkeletonAnimation";
            var info = new ProcessStartInfo { FileName = executable, UseShellExecute = true };
            info.ArgumentList.Add("--input"); info.ArgumentList.Add(directory); info.ArgumentList.Add("--display-mode"); info.ArgumentList.Add(mode);
            Process.Start(info);
            RecordPreviewHistory(directory);
            if (_selectedCard != null)
            {
                _selectedCard.LastPreviewAt = DateTime.Now;
                _selectedCard.LastUsedAt = DateTime.Now;
                SaveStore();
                CardStateTextBlock.Text = BuildCardStateText(_selectedCard);
            }
            SetStatus("已启动预览器");
        }
        catch (Exception exception) { _logger.Error(exception.Message); SetStatus("预览器启动失败"); }
    }

    private void RecordPreviewHistory(string directory)
    {
        var fullPath = Path.GetFullPath(directory);
        var item = _previewHistory.FirstOrDefault(x => string.Equals(x.Path, fullPath, StringComparison.OrdinalIgnoreCase));
        if (item == null)
        {
            item = new PreviewHistoryEntry { Path = fullPath };
            _previewHistory.Add(item);
        }
        item.LastUsedAt = DateTime.Now;
        var sorted = _previewHistory.OrderByDescending(x => x.LastUsedAt).Take(20).ToList();
        _previewHistory.Clear();
        foreach (var history in sorted) _previewHistory.Add(history);
        CardStore.SavePreviewHistory(_previewHistory);
    }

    private void CancelOperation_Click(object sender, RoutedEventArgs e) => _operationCts?.Cancel();

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e) => _cardsView.Refresh();

    private void CardFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loadingFilters) _cardsView.Refresh();
    }

    private void ClearCardFilters_Click(object sender, RoutedEventArgs e)
    {
        _loadingFilters = true;
        ProjectFilterComboBox.SelectedIndex = 0;
        TagFilterListBox.UnselectAll();
        SearchTextBox.Clear();
        _loadingFilters = false;
        _cardsView.Refresh();
    }

    private void RefreshCardFilters()
    {
        _loadingFilters = true;
        var projects = _cards.Select(x => x.ProjectGroup).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        projects.Insert(0, "全部项目");
        ProjectFilterComboBox.ItemsSource = projects;
        ProjectFilterComboBox.SelectedIndex = 0;
        var tags = _cards.SelectMany(x => x.Tags ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        TagFilterListBox.ItemsSource = tags;
        TagFilterEmptyTextBlock.Visibility = tags.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _loadingFilters = false;
        _cardsView.Refresh();
    }

    private void LoadPreviewHistory()
    {
        _previewHistory.Clear();
        foreach (var item in CardStore.LoadPreviewHistory().Where(x => Directory.Exists(x.Path)).OrderByDescending(x => x.LastUsedAt)) _previewHistory.Add(item);
    }

    private bool CardMatchesSearch(object item)
    {
        if (item is not SpineTaskCard card) return false;
        var query = SearchTextBox?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(query)) query = string.Empty;
        var values = string.Join("\n", card.Name, card.Note, card.SpineProjectPath, card.ProjectGroup, string.Join(" ", card.Tags ?? new List<string>()), card.ExportConfig.DisplayName, card.ExportConfig.Note, card.ExportConfig.Path, card.ValidationRules.DisplayName, card.ValidationRules.Note, card.ValidationRules.Path);
        if (!string.IsNullOrWhiteSpace(query) && !values.Contains(query, StringComparison.OrdinalIgnoreCase)) return false;
        if (ProjectFilterComboBox?.SelectedItem is string project && project != "全部项目" && !string.Equals(project, card.ProjectGroup, StringComparison.OrdinalIgnoreCase)) return false;
        var selectedTags = TagFilterListBox?.SelectedItems.Cast<string>().ToArray() ?? Array.Empty<string>();
        return selectedTags.Length == 0 || selectedTags.All(tag => (card.Tags ?? new List<string>()).Any(x => string.Equals(x, tag, StringComparison.OrdinalIgnoreCase)));
    }

    private void Window_PreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { SearchTextBox.Focus(); SearchTextBox.SelectAll(); e.Handled = true; }
    }

    private void PathTextBox_TextChanged(object sender, TextChangedEventArgs e) { if (!_loadingCard) UpdatePathStatuses(); }

    private void UpdatePathStatuses()
    {
        SetPathStatus(ExporterConfigStatusTextBlock, ExporterConfigTextBox.Text, "导出配置", BuildExportConfigSummary);
        SetPathStatus(ValidatorRulesStatusTextBlock, ValidatorRulesTextBox.Text, "检查规则", BuildValidationRulesSummary);
    }

    private static void SetPathStatus(TextBlock target, string path, string label, Func<string, string> summaryBuilder)
    {
        var valid = !string.IsNullOrWhiteSpace(path) && File.Exists(path);
        target.Text = string.IsNullOrWhiteSpace(path) ? $"{label}路径未设置" : valid ? summaryBuilder(path) : "路径不存在";
        target.Foreground = string.IsNullOrWhiteSpace(path) ? System.Windows.Media.Brushes.DarkOrange : valid ? System.Windows.Media.Brushes.ForestGreen : System.Windows.Media.Brushes.Firebrick;
    }

    private static string BuildExportConfigSummary(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            var schema = root.TryGetProperty("schemaVersion", out var schemaValue) ? schemaValue.ToString() : "?";
            var export = root.TryGetProperty("export", out var exportValue) ? exportValue : root;
            var output = export.TryGetProperty("output", out var outputValue) ? outputValue : default;
            var template = output.ValueKind != JsonValueKind.Undefined && output.TryGetProperty("outputSubdirectoryTemplate", out var templateValue) ? templateValue.GetString() : "{resourceName}";
            var emitJson = export.TryGetProperty("emitJson", out var jsonValue) && jsonValue.GetBoolean();
            var emitBinary = export.TryGetProperty("emitBinary", out var binaryValue) && binaryValue.GetBoolean();
            var pack = export.TryGetProperty("texturePacker", out var packValue) && packValue.TryGetProperty("mode", out var modeValue) ? modeValue.GetString() : "Embedded";
            var pma = export.TryGetProperty("requirePremultipliedAlpha", out var pmaValue) && pmaValue.GetBoolean();
            return $"有效 · schema {schema} · 输出 {template} · JSON {(emitJson ? "有" : "无")} · SKEL {(emitBinary ? "有" : "无")} · 打包 {pack} · PMA {(pma ? "必需" : "默认")}";
        }
        catch { return "路径有效，但配置内容无法读取"; }
    }

    private static string BuildValidationRulesSummary(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            var schema = root.TryGetProperty("schemaVersion", out var schemaValue) ? schemaValue.ToString() : "?";
            var settings = root.TryGetProperty("settings", out var settingsValue) ? settingsValue : root;
            var version = settings.TryGetProperty("dataVersion", out var versionValue) ? versionValue.GetString() : "未指定";
            var animations = settings.TryGetProperty("requiredAnimations", out var animationsValue) && animationsValue.ValueKind == JsonValueKind.Array
                ? string.Join(",", animationsValue.EnumerateArray().Select(x => x.GetString())) : "无";
            var warningBones = settings.TryGetProperty("warningBones", out var warningBoneValue) && warningBoneValue.ValueKind == JsonValueKind.Array
                ? string.Join(",", warningBoneValue.EnumerateArray().Select(x => x.GetString())) : "无";
            var events = settings.TryGetProperty("events", out var eventsValue) && eventsValue.ValueKind == JsonValueKind.Array ? $"{eventsValue.GetArrayLength()} 条事件规则" : "无事件规则";
            var dialog = root.TryGetProperty("showFailureDialog", out var dialogValue) && dialogValue.GetBoolean();
            return $"有效 · schema {schema} · 数据版本 {version} · 必需动画 {animations} · 事件 {events} · 警告骨骼 {warningBones} · 失败弹窗 {(dialog ? "开" : "关")}";
        }
        catch { return "路径有效，但规则内容无法读取"; }
    }

    private void BrowseProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Spine Project (*.spine)|*.spine|All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) == true)
        {
            ProjectPathTextBox.Text = dialog.FileName;
            if (string.IsNullOrWhiteSpace(CardNameTextBox.Text) || CardNameTextBox.Text == "新 Spine 任务") CardNameTextBox.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
        }
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog { SelectedPath = OutputPathComboBox.Text };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            OutputPathComboBox.Text = dialog.SelectedPath;
            RememberOutputDirectory();
        }
    }

    private void OutputPathComboBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var textBox = OutputPathComboBox.Template.FindName("PART_EditableTextBox", OutputPathComboBox) as System.Windows.Controls.TextBox;
        if (textBox?.IsMouseOver == true && _outputHistory.Directories.Count > 0 && !OutputPathComboBox.IsDropDownOpen)
            OutputPathComboBox.IsDropDownOpen = true;
    }

    private void OutputPathComboBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!_loadingCard && !OutputPathComboBox.IsKeyboardFocusWithin) RememberOutputDirectory();
    }

    private void RememberOutputDirectory()
    {
        if (string.IsNullOrWhiteSpace(OutputPathComboBox.Text)) return;
        try
        {
            var text = OutputPathComboBox.Text;
            _outputHistory.Remember(text);
            OutputPathComboBox.ItemsSource = null;
            OutputPathComboBox.ItemsSource = _outputHistory.Directories;
            OutputPathComboBox.Text = text;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            SetStatus("输出目录历史保存失败：" + exception.Message);
        }
    }

    private void OpenOutputDirectory_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var root = Require(OutputPathComboBox.Text.Trim(), "输出根目录");
            var name = Path.GetFileNameWithoutExtension(ProjectPathTextBox.Text.Trim());
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("请先选择 Spine 工程。");
            var directory = Path.Combine(Path.GetFullPath(root), name);
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("当前资源的输出目录尚不存在，请先执行导出：" + directory);
            RememberOutputDirectory();
            OpenDirectory(directory);
            SetStatus("已打开输出目录：" + directory);
        }
        catch (Exception exception)
        {
            _logger.Warning(exception.Message);
            SetStatus(exception.Message);
        }
    }

    private void BrowseExporterConfig_Click(object sender, RoutedEventArgs e) => BrowseConfig(ExporterConfigTextBox, ExportConfigNameTextBox);
    private void BrowseRules_Click(object sender, RoutedEventArgs e) => BrowseConfig(ValidatorRulesTextBox, ValidationRulesNameTextBox);

    private static void BrowseConfig(WpfTextBox pathBox, WpfTextBox nameBox)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "JSON (*.json)|*.json|All files (*.*)|*.*" };
        if (dialog.ShowDialog() == true)
        {
            pathBox.Text = dialog.FileName;
            if (string.IsNullOrWhiteSpace(nameBox.Text)) nameBox.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
        }
    }

    private void OpenProjectDirectory_Click(object sender, RoutedEventArgs e) => OpenDirectory(Path.GetDirectoryName(ProjectPathTextBox.Text));
    private void OpenExportConfigDirectory_Click(object sender, RoutedEventArgs e) => OpenDirectory(Path.GetDirectoryName(ExporterConfigTextBox.Text));
    private void OpenRulesDirectory_Click(object sender, RoutedEventArgs e) => OpenDirectory(Path.GetDirectoryName(ValidatorRulesTextBox.Text));

    private static void OpenDirectory(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    private string BuildCardStateText(SpineTaskCard card)
    {
        var export = card.LastExportAt.HasValue ? $"上次导出：{card.LastExportAt.Value:yyyy-MM-dd HH:mm}" : "上次导出：未导出";
        var modified = card.LastProjectWriteTimeUtc.HasValue ? $"工程修改：{card.LastProjectWriteTimeUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm}" : "工程修改：未知";
        var validation = card.LastValidationAt.HasValue ? $"最近验证：{card.LastValidationAt.Value:MM-dd HH:mm} {(card.LastValidationPassed == true ? "通过" : "失败")}" : "最近验证：未执行";
        return $"{(card.IsPinned ? "已置顶 · " : string.Empty)}{export} · {modified} · {validation}";
    }

    private void LoadVisualSettings(ExportConfigCard? export)
    {
        if (export == null || string.IsNullOrWhiteSpace(export.Path) || !File.Exists(export.Path))
        {
            ExportSettingsStatusTextBlock.Text = "导出配置文件不存在，无法读取 Spine 常用选项。";
            return;
        }

        try
        {
            var settings = SpineVisualSettings.Load(export.Path);
            JsonNonessentialCheckBox.IsChecked = settings.JsonNonessential;
            JsonPrettyPrintCheckBox.IsChecked = settings.JsonPrettyPrint;
            BinaryNonessentialCheckBox.IsChecked = settings.BinaryNonessential;
            PremultiplyAlphaCheckBox.IsChecked = settings.PremultiplyAlpha;
            PackMultipleOfFourCheckBox.IsChecked = settings.MultipleOfFour;
            PackSquareCheckBox.IsChecked = settings.Square;
            PackPowerOfTwoCheckBox.IsChecked = settings.PowerOfTwo;
            PackMinWidthTextBox.Text = settings.MinWidth.ToString(CultureInfo.InvariantCulture);
            PackMinHeightTextBox.Text = settings.MinHeight.ToString(CultureInfo.InvariantCulture);
            PackAliasCheckBox.IsChecked = settings.Alias;
            PackIgnoreBlankCheckBox.IsChecked = settings.IgnoreBlankImages;
            PackAlphaThresholdTextBox.Text = settings.AlphaThreshold.ToString(CultureInfo.InvariantCulture);
            PackFlattenPathsCheckBox.IsChecked = settings.FlattenPaths;
            PackIndexesCheckBox.IsChecked = settings.UseIndexes;
            PackDebugCheckBox.IsChecked = settings.Debug;
            PackFastCheckBox.IsChecked = settings.Fast;
            PackPrettyPrintCheckBox.IsChecked = settings.PrettyPrint;
            SelectComboItem(PackPackingComboBox, settings.Packing);
            SelectComboItem(PackPixelFormatComboBox, settings.PixelFormat);
            SelectComboItem(PackAtlasExtensionComboBox, settings.AtlasExtension);
            PackMaxWidthTextBox.Text = settings.MaxWidth.ToString(CultureInfo.InvariantCulture);
            PackMaxHeightTextBox.Text = settings.MaxHeight.ToString(CultureInfo.InvariantCulture);
            PackScaleTextBox.Text = settings.Scale.ToString("0.###", CultureInfo.InvariantCulture);
            PackPaddingXTextBox.Text = settings.PaddingX.ToString(CultureInfo.InvariantCulture);
            PackPaddingYTextBox.Text = settings.PaddingY.ToString(CultureInfo.InvariantCulture);
            PackEdgePaddingCheckBox.IsChecked = settings.EdgePadding;
            PackDuplicatePaddingCheckBox.IsChecked = settings.DuplicatePadding;
            PackRotationCheckBox.IsChecked = settings.Rotation;
            PackStripWhitespaceXCheckBox.IsChecked = settings.StripWhitespaceX;
            PackStripWhitespaceYCheckBox.IsChecked = settings.StripWhitespaceY;
            PackBleedCheckBox.IsChecked = settings.Bleed;
            PackLimitMemoryCheckBox.IsChecked = settings.LimitMemory;
            SelectComboItem(PackFilterMinComboBox, settings.FilterMin);
            SelectComboItem(PackFilterMagComboBox, settings.FilterMag);
            SelectComboItem(PackWrapXComboBox, settings.WrapX);
            SelectComboItem(PackWrapYComboBox, settings.WrapY);

            var config = ExporterConfigLoader.Load(export.Path);
            var mode = config.Export.TexturePacker.Mode;
            ExportSettingsStatusTextBlock.Text = string.Equals(mode, "Separate", StringComparison.OrdinalIgnoreCase)
                ? "图片目录模式：打包目录内的全部图片，使用下方图集参数。"
                : "Attachments 模式：只打包工程实际引用的附件图片。图集尺寸、缩放和 PMA 使用下方设置。";
        }
        catch (Exception exception)
        {
            ExportSettingsStatusTextBlock.Text = $"读取失败：{exception.Message}";
        }
    }

    private void SaveVisualSettings(ExportConfigCard export)
    {
        var settings = new SpineVisualSettings
        {
            JsonNonessential = JsonNonessentialCheckBox.IsChecked == true,
            JsonPrettyPrint = JsonPrettyPrintCheckBox.IsChecked == true,
            BinaryNonessential = BinaryNonessentialCheckBox.IsChecked == true,
            PremultiplyAlpha = PremultiplyAlphaCheckBox.IsChecked == true,
            MultipleOfFour = PackMultipleOfFourCheckBox.IsChecked == true,
            Square = PackSquareCheckBox.IsChecked == true,
            PowerOfTwo = PackPowerOfTwoCheckBox.IsChecked == true,
            MinWidth = ParseInt(PackMinWidthTextBox.Text, "页面最小宽度"),
            MinHeight = ParseInt(PackMinHeightTextBox.Text, "页面最小高度"),
            Alias = PackAliasCheckBox.IsChecked == true,
            IgnoreBlankImages = PackIgnoreBlankCheckBox.IsChecked == true,
            AlphaThreshold = ParseInt(PackAlphaThresholdTextBox.Text, "Alpha 阈值"),
            FlattenPaths = PackFlattenPathsCheckBox.IsChecked == true,
            UseIndexes = PackIndexesCheckBox.IsChecked == true,
            Debug = PackDebugCheckBox.IsChecked == true,
            Fast = PackFastCheckBox.IsChecked == true,
            PrettyPrint = PackPrettyPrintCheckBox.IsChecked == true,
            Packing = SelectedComboValue(PackPackingComboBox, "rectangles"),
            PixelFormat = SelectedComboValue(PackPixelFormatComboBox, "RGBA8888"),
            AtlasExtension = SelectedComboValue(PackAtlasExtensionComboBox, ".atlas.txt"),
            MaxWidth = ParseInt(PackMaxWidthTextBox.Text, "Atlas 最大宽度"),
            MaxHeight = ParseInt(PackMaxHeightTextBox.Text, "Atlas 最大高度"),
            Scale = ParseFloat(PackScaleTextBox.Text, "图片缩放"),
            PaddingX = ParseInt(PackPaddingXTextBox.Text, "Padding X"),
            PaddingY = ParseInt(PackPaddingYTextBox.Text, "Padding Y"),
            EdgePadding = PackEdgePaddingCheckBox.IsChecked == true,
            DuplicatePadding = PackDuplicatePaddingCheckBox.IsChecked == true,
            Rotation = PackRotationCheckBox.IsChecked == true,
            StripWhitespaceX = PackStripWhitespaceXCheckBox.IsChecked == true,
            StripWhitespaceY = PackStripWhitespaceYCheckBox.IsChecked == true,
            Bleed = PackBleedCheckBox.IsChecked == true,
            LimitMemory = PackLimitMemoryCheckBox.IsChecked == true,
            FilterMin = SelectedComboValue(PackFilterMinComboBox, "Linear"),
            FilterMag = SelectedComboValue(PackFilterMagComboBox, "Linear"),
            WrapX = SelectedComboValue(PackWrapXComboBox, "ClampToEdge"),
            WrapY = SelectedComboValue(PackWrapYComboBox, "ClampToEdge")
        };
        SpineVisualSettings.Save(export.Path, settings);
    }

    private static int ParseInt(string text, string name)
        => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new InvalidOperationException($"{name}必须是整数。");

    private static float ParseFloat(string text, string name)
        => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : throw new InvalidOperationException($"{name}必须是大于 0 的数字。");

    private static void SelectComboItem(WpfComboBox comboBox, string value)
    {
        var selected = comboBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase));
        if (selected == null)
        {
            selected = new ComboBoxItem { Content = value };
            comboBox.Items.Add(selected);
        }
        comboBox.SelectedItem = selected;
    }

    private static string SelectedComboValue(WpfComboBox comboBox, string fallback)
        => (comboBox.SelectedItem as ComboBoxItem)?.Content?.ToString()
           ?? comboBox.SelectedValue?.ToString()
           ?? fallback;

    private static List<string> ParseTags(string text)
    {
        return text.Split(new[] { ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private ToolRequest ReadRequest(bool requireExport = false, bool requireValidation = false, bool batch = false)
    {
        if (_selectedCard == null) throw new InvalidOperationException("请先选择卡片。");
        UpdateCardFromEditor(_selectedCard);
        RememberOutputDirectory();
        if (!batch && string.IsNullOrWhiteSpace(_selectedCard.SpineProjectPath)) throw new InvalidOperationException("Spine 工程路径不能为空。");
        if (requireExport && TaskExportConfigComboBox.SelectedItem == null) throw new InvalidOperationException("请选择导出配置。");
        if (requireValidation && TaskValidationRuleComboBox.SelectedItem == null) throw new InvalidOperationException("请选择验证规则。");
        return new ToolRequest(_selectedCard, batch ? OutputPathComboBox.Text : Require(OutputPathComboBox.Text, "输出根目录"));
    }

    private static string GetResourceName(SpineTaskCard card) => Path.GetFileNameWithoutExtension(card.SpineProjectPath);
    private static string GetResourceDirectory(string outputRoot, SpineTaskCard card) => Path.Combine(Path.GetFullPath(outputRoot), GetResourceName(card));
    private static string Require(string value, string name) => string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException($"{name}不能为空。") : value;

    private ValidationReport ValidateDirectory(string directory, SpineTaskCard card)
    {
        var rules = new ValidatorRulesLoader().Load(card.ValidationRules.Path);
        var report = new SpineValidatorEngine().Validate(directory, rules);
        foreach (var issue in report.Issues)
        {
            var line = $"[{issue.Severity}] {issue.Code}: {issue.Message}";
            if (issue.Severity == ValidationSeverity.Warning) _logger.Warning(line);
            else _logger.Error(line);
        }
        if (!report.Passed && rules.ShowFailureDialog)
        {
            var blockers = report.Issues
                .Where(x => x.Severity == ValidationSeverity.Blocker)
                .Select(x => $"• {x.Message}")
                .Take(8);
            System.Windows.MessageBox.Show(this, string.Join(Environment.NewLine, blockers), "Spine 检查未通过", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        return report;
    }

    private async Task RunBusyAsync(Func<CancellationToken, Task> action)
    {
        if (_operationCts != null) return;
        _operationCts = new CancellationTokenSource(); BatchCancelButton.IsEnabled = true; ProgressBar.IsIndeterminate = true;
        try { SetStatus("处理中..."); await action(_operationCts.Token); }
        catch (OperationCanceledException) { SetStatus("已取消"); _logger.Warning("操作已取消。"); }
        catch (Exception exception) { _logger.Error(exception.Message); SetStatus("失败"); }
        finally { _operationCts.Dispose(); _operationCts = null; BatchCancelButton.IsEnabled = false; ProgressBar.IsIndeterminate = false; ProgressBar.Value = 0; }
    }

    private static void CopyDirectory(string source, string destination)
    {
        if (Directory.Exists(destination)) throw new IOException($"正式输出目录已存在，当前工具不会自动覆盖：{destination}");
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private void AppendLog(string level, string message) { Dispatcher.Invoke(() => { LogTextBox.AppendText($"[{level}] {message}{Environment.NewLine}"); LogTextBox.ScrollToEnd(); }); }
    private void SetStatus(string value) => StatusTextBlock.Text = value;
    private sealed record ToolRequest(SpineTaskCard Card, string OutputRoot);
    private sealed class UiLogger : IExportLogger { private readonly Action<string, string> _write; public UiLogger(Action<string, string> write) { _write = write; } public void Info(string message) => _write("INFO", message); public void Warning(string message) => _write("WARN", message); public void Error(string message) => _write("ERROR", message); }
}









