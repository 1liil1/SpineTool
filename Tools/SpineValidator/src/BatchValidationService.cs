namespace IdleGearDefence.SpineValidator;

public sealed class BatchValidationResult
{
    public string RootDirectory { get; init; } = string.Empty;

    public IReadOnlyList<BatchValidationItem> Items { get; init; } = Array.Empty<BatchValidationItem>();

    public bool Passed => Items.Count > 0 && Items.All(x => x.Report?.Passed == true);
}

public sealed class BatchValidationItem
{
    public DiscoveredSpineResource Resource { get; init; } = new();

    public ValidationReport Report { get; init; }

    public string ErrorMessage { get; init; }
}

public sealed class BatchValidationService
{
    private readonly SpineValidatorEngine _engine;

    public BatchValidationService(SpineValidatorEngine engine = null)
    {
        _engine = engine ?? new SpineValidatorEngine();
    }

    public async Task<BatchValidationResult> ValidateAsync(
        string rootDirectory,
        string rulesPath,
        CancellationToken cancellationToken = default,
        IProgress<BatchValidationProgress> progress = null)
    {
        var root = Path.GetFullPath(rootDirectory);
        var rules = new ValidatorRulesLoader().Load(rulesPath);
        var resources = ResourcePackageDiscovery.Discover(root);
        var items = new List<BatchValidationItem>(resources.Count);

        for (var i = 0; i < resources.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resource = resources[i];
            progress?.Report(new BatchValidationProgress(i + 1, resources.Count, resource));
            try
            {
                var report = _engine.Validate(resource.Directory, rules);
                items.Add(new BatchValidationItem { Resource = resource, Report = report });
            }
            catch (Exception exception)
            {
                items.Add(new BatchValidationItem { Resource = resource, ErrorMessage = exception.Message });
            }
            await Task.Yield();
        }

        return new BatchValidationResult { RootDirectory = root, Items = items };
    }
}

public sealed record BatchValidationProgress(
    int Current,
    int Total,
    DiscoveredSpineResource Resource);
