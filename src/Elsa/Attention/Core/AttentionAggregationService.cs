namespace Elsa.Attention.Core;

public sealed class AttentionAggregationService(
    IAttentionContributorRegistry registry,
    IAttentionPermissionEvaluator permissionEvaluator,
    AttentionOptions options,
    TimeProvider? timeProvider = null) : IAttentionAggregationService
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<AttentionAggregationResult> AggregateAsync(
        AttentionQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateOptions();

        var explicitIds = NormalizeContributorIds(query.ContributorIds);
        var registrations = SelectRegistrations(explicitIds);
        var tasks = registrations.Select(x => EvaluateAsync(x, query, explicitIds is not null, cancellationToken));
        var results = await Task.WhenAll(tasks);

        return new AttentionAggregationResult(
            _timeProvider.GetUtcNow(),
            results.Where(x => x is not null).Select(x => x!).ToArray());
    }

    private async Task<AttentionContributorResult?> EvaluateAsync(
        AttentionContributorRegistration registration,
        AttentionQuery query,
        bool explicitlySelected,
        CancellationToken cancellationToken)
    {
        var descriptor = registration.Descriptor;
        if (!await IsAuthorizedAsync(descriptor, query.Context, cancellationToken))
            return explicitlySelected ? Failure(descriptor, AttentionContributorStatus.Forbidden, "CONTRIBUTOR_FORBIDDEN") : null;

        var lifetime = registration.Lifetime;
        using var timeout = new CancellationTokenSource(options.ContributorTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            lifetime,
            timeout.Token);

        try
        {
            var context = new AttentionContributorContext(
                query.Context,
                new AttentionExecutionBudget(options.MaxDownstreamCallsPerContributor),
                ResolveThresholds(descriptor));
            var contribution = await registration.Contributor.EvaluateAsync(context, linked.Token).AsTask()
                .WaitAsync(options.ContributorTimeout, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            if (lifetime.IsCancellationRequested)
                return Failure(descriptor, AttentionContributorStatus.Unavailable, "CONTRIBUTOR_UNLOADED");

            return Normalize(descriptor, contribution);
        }
        catch (TimeoutException)
        {
            linked.Cancel();
            return Failure(descriptor, AttentionContributorStatus.TimedOut, "CONTRIBUTOR_TIMEOUT");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            return Failure(descriptor, AttentionContributorStatus.TimedOut, "CONTRIBUTOR_TIMEOUT");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && lifetime.IsCancellationRequested)
        {
            return Failure(descriptor, AttentionContributorStatus.Unavailable, "CONTRIBUTOR_UNLOADED");
        }
        catch (AttentionBudgetExceededException)
        {
            return Failure(descriptor, AttentionContributorStatus.Failed, "CONTRIBUTOR_BUDGET_EXCEEDED");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (!IsCriticalException(exception))
        {
            return Failure(descriptor, AttentionContributorStatus.Failed, "CONTRIBUTOR_FAILED");
        }
    }

    private static bool IsCriticalException(Exception exception) =>
        exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException
            or AppDomainUnloadedException
            or BadImageFormatException
            or CannotUnloadAppDomainException
            or InvalidProgramException;

    private async ValueTask<bool> IsAuthorizedAsync(
        AttentionContributorDescriptor descriptor,
        AttentionQueryContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(descriptor.RequiredPermission))
            return true;

        return await permissionEvaluator.IsAllowedAsync(
            context.Principal,
            descriptor.RequiredPermission,
            context.TenantId,
            cancellationToken);
    }

    private AttentionContributorResult Normalize(
        AttentionContributorDescriptor descriptor,
        AttentionContribution contribution)
    {
        ArgumentNullException.ThrowIfNull(contribution);
        if (contribution.Status == AttentionContributionStatus.Unavailable)
            return Failure(
                descriptor,
                AttentionContributorStatus.Unavailable,
                contribution.ErrorCode ?? "CONTRIBUTOR_UNAVAILABLE",
                contribution.Detail,
                contribution.DiagnosticDestination);

        foreach (var item in contribution.Items)
            AttentionItemValidator.Validate(item);

        if (contribution.TotalCount < contribution.Items.Count)
            throw new ArgumentException("Attention contribution total cannot be smaller than its returned item count.");

        var items = contribution.Items
            .Take(options.MaxItemsPerContributor)
            .Select(ApplySensitivityPolicy)
            .ToArray();
        var totalCount = contribution.TotalCount;
        return new AttentionContributorResult(
            descriptor.Id,
            descriptor.DisplayName,
            AttentionContributorStatus.Ready,
            _timeProvider.GetUtcNow(),
            totalCount,
            contribution.IsTruncated || totalCount > items.Length,
            items);
    }

    private AttentionItem ApplySensitivityPolicy(AttentionItem item) =>
        item.Sensitivity == AttentionSensitivity.Restricted && !options.ExposeRestrictedSummaries
            ? item with { Summary = null }
            : item;

    private IReadOnlyDictionary<string, string> ResolveThresholds(AttentionContributorDescriptor descriptor)
    {
        var values = (descriptor.Thresholds ?? [])
            .ToDictionary(x => x.Key, x => x.DefaultValue, StringComparer.OrdinalIgnoreCase);
        if (!options.ContributorThresholdOverrides.TryGetValue(descriptor.Id, out var overrides))
            return values;

        foreach (var (key, value) in overrides)
        {
            if (!values.ContainsKey(key))
                throw new InvalidOperationException($"Unknown threshold '{key}' for attention contributor '{descriptor.Id}'.");
            values[key] = value;
        }

        return values;
    }

    private AttentionContributorResult Failure(
        AttentionContributorDescriptor descriptor,
        AttentionContributorStatus status,
        string errorCode,
        string? detail = null,
        AttentionDestination? destination = null) =>
        new(
            descriptor.Id,
            descriptor.DisplayName,
            status,
            _timeProvider.GetUtcNow(),
            0,
            false,
            [],
            errorCode,
            detail,
            destination);

    private IReadOnlyCollection<AttentionContributorRegistration> SelectRegistrations(IReadOnlyCollection<string>? explicitIds)
    {
        var registrations = registry.List();
        if (explicitIds is null)
            return registrations;

        var byId = registrations.ToDictionary(x => x.Descriptor.Id, StringComparer.OrdinalIgnoreCase);
        var unknown = explicitIds.Where(x => !byId.ContainsKey(x)).ToArray();
        if (unknown.Length > 0)
            throw new AttentionQueryException($"Unknown attention contributor: {string.Join(", ", unknown)}.");

        return explicitIds.Select(x => byId[x]).ToArray();
    }

    private static IReadOnlyCollection<string>? NormalizeContributorIds(IReadOnlyCollection<string>? contributorIds)
    {
        if (contributorIds is null || contributorIds.Count == 0)
            return null;
        if (contributorIds.Any(string.IsNullOrWhiteSpace))
            throw new AttentionQueryException("Contributor IDs cannot be empty.");

        return contributorIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private void ValidateOptions()
    {
        if (options.ContributorTimeout <= TimeSpan.Zero)
            throw new InvalidOperationException("Attention contributor timeout must be positive.");
        if (options.MaxItemsPerContributor <= 0)
            throw new InvalidOperationException("Attention item limit must be positive.");
        if (options.MaxDownstreamCallsPerContributor < 0)
            throw new InvalidOperationException("Attention downstream call limit cannot be negative.");
    }
}

internal static class AttentionItemValidator
{
    public static void Validate(AttentionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Generation))
            throw new ArgumentException("Attention item identity and generation are required.");
        if (string.IsNullOrWhiteSpace(item.Title) || item.Count < 1)
            throw new ArgumentException("Attention item title and a positive count are required.");
        if (item.LastObservedAt < item.OccurredAt)
            throw new ArgumentException("Attention item observation time cannot precede occurrence time.");
        if (string.IsNullOrWhiteSpace(item.Destination.Path) || !item.Destination.Path.StartsWith('/'))
            throw new ArgumentException("Attention item destinations must use host-relative paths.");
        if (string.IsNullOrWhiteSpace(item.Destination.Label))
            throw new ArgumentException("Attention item destination label is required.");
        if (item.Correlations.Any(x => string.IsNullOrWhiteSpace(x.Kind) || string.IsNullOrWhiteSpace(x.Value)))
            throw new ArgumentException("Attention correlations require a kind and value.");
    }
}
