namespace Elsa.Attention.Core;

public interface IAttentionPermissionEvaluator
{
    ValueTask<bool> IsAllowedAsync(
        System.Security.Claims.ClaimsPrincipal principal,
        string permission,
        string? tenantId,
        CancellationToken cancellationToken = default);
}

public interface IAttentionContributor
{
    AttentionContributorDescriptor Descriptor { get; }

    ValueTask<AttentionContribution> EvaluateAsync(
        AttentionContributorContext context,
        CancellationToken cancellationToken = default);
}

public interface IAttentionContributorRegistry
{
    IReadOnlyCollection<AttentionContributorRegistration> List();

    IDisposable Register(IAttentionContributor contributor);
}

public interface IAttentionAggregationService
{
    Task<AttentionAggregationResult> AggregateAsync(
        AttentionQuery query,
        CancellationToken cancellationToken = default);
}

public sealed record AttentionContributorContext(
    AttentionQueryContext Query,
    AttentionExecutionBudget Budget,
    IReadOnlyDictionary<string, string> Thresholds);
