using Elsa.Attention.Core;

namespace Elsa.Workflows.Runtime.Attention;

public sealed class WorkflowRuntimeAttentionContributor(
    IWorkflowRuntimeAttentionQuery query,
    IEnumerable<IWorkflowRuntimePlacementAttention>? placementAttention = null) : IAttentionContributor
{
    private const int MaximumItems = 5;
    private readonly IWorkflowRuntimePlacementAttention[] _placementAttention = placementAttention?.ToArray() ?? [];

    public AttentionContributorDescriptor Descriptor { get; } = new(
        "workflows.runtime",
        "Workflow runtime",
        "*");

    public async ValueTask<AttentionContribution> EvaluateAsync(
        AttentionContributorContext context,
        CancellationToken cancellationToken = default)
    {
        context.Budget.ConsumeDownstreamCall();
        var snapshot = await query.QueryAsync(new(context.Query, MaximumItems), cancellationToken);
        if (!snapshot.IsAvailable)
            return AttentionContribution.Unavailable(snapshot.ErrorCode!, snapshot.Detail, new("/workflows/instances", "Inspect workflow runs"));

        if (snapshot.TotalCount < snapshot.Records.Count)
            throw new InvalidOperationException("Workflow runtime attention total cannot be smaller than the returned record count.");

        var unplaceable = new List<UnplaceableWorkReport>();
        foreach (var source in _placementAttention)
            unplaceable.AddRange(await source.ListUnplaceableWorkAsync(cancellationToken));

        var totalCount = snapshot.TotalCount + unplaceable.Count;
        var items = snapshot.Records
            .Select(Map)
            .Concat(unplaceable.Select(MapUnplaceable))
            .OrderBy(item => item.Severity == AttentionSeverity.Critical ? 0 : 1)
            .ThenByDescending(item => item.LastObservedAt)
            .Take(MaximumItems)
            .ToArray();

        return AttentionContribution.Ready(items, totalCount, totalCount > items.Length);
    }

    /// <summary>
    /// Spec 184, FR-017: a warning naming what the waiting executions need and how many wait, without naming a host.
    /// Its identity is the requirement, so the same condition keeps one item while its count moves.
    /// </summary>
    private static AttentionItem MapUnplaceable(UnplaceableWorkReport report)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(report.Requirement);
        if (report.WaitingExecutions < 1)
            throw new ArgumentOutOfRangeException(nameof(report), "An unplaceable-work report must count at least one waiting execution.");

        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(report.Requirement)))[..16];
        return new(
            $"placement:{key}",
            $"{key}:{report.WaitingExecutions}",
            AttentionSeverity.Warning,
            "Work is waiting for a member that can run it",
            $"{report.WaitingExecutions} workflow execution(s) waiting: {report.Requirement}.",
            report.FirstObservedAt,
            report.LastObservedAt < report.FirstObservedAt ? report.FirstObservedAt : report.LastObservedAt,
            report.WaitingExecutions,
            new("/workflows/instances", "Inspect workflow runs"),
            [],
            AttentionSensitivity.Restricted);
    }

    private static AttentionItem Map(WorkflowRuntimeAttentionRecord record)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(record.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.WorkflowDefinitionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.Generation);

        var incident = !string.IsNullOrWhiteSpace(record.IncidentId);
        var id = incident ? $"incident:{record.IncidentId}" : $"execution:{record.WorkflowExecutionId}";
        var title = record.Kind switch
        {
            WorkflowRuntimeAttentionKind.BlockingIncident => "Workflow run has a blocking incident",
            WorkflowRuntimeAttentionKind.OpenIncident => "Workflow run has an open incident",
            _ => "Workflow run faulted"
        };
        var correlations = new List<AttentionCorrelation>
        {
            new("workflowExecution", record.WorkflowExecutionId),
            new("workflowDefinition", record.WorkflowDefinitionId)
        };
        if (incident)
            correlations.Add(new("incident", record.IncidentId!));

        return new(
            id,
            record.Generation,
            ToSeverity(record.Kind),
            title,
            record.SanitizedSummary,
            record.OccurredAt,
            record.LastObservedAt,
            record.Count,
            new($"/workflows/instances/{Uri.EscapeDataString(record.WorkflowExecutionId)}", "Inspect run"),
            correlations,
            AttentionSensitivity.Restricted);
    }

    private static AttentionSeverity ToSeverity(WorkflowRuntimeAttentionKind kind) => kind switch
    {
        WorkflowRuntimeAttentionKind.FaultedExecution or WorkflowRuntimeAttentionKind.BlockingIncident => AttentionSeverity.Critical,
        _ => AttentionSeverity.Warning
    };
}
