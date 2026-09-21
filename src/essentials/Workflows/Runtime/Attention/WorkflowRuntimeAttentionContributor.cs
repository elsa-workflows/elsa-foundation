using Elsa.Attention.Core;

namespace Elsa.Workflows.Runtime.Attention;

public sealed class WorkflowRuntimeAttentionContributor(IWorkflowRuntimeAttentionQuery query) : IAttentionContributor
{
    private const int MaximumItems = 5;

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

        var items = snapshot.Records
            .OrderBy(record => SeverityOrder(record.Kind))
            .ThenByDescending(record => record.LastObservedAt)
            .Take(MaximumItems)
            .Select(Map)
            .ToArray();

        return AttentionContribution.Ready(items, snapshot.TotalCount, snapshot.TotalCount > items.Length);
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

    private static int SeverityOrder(WorkflowRuntimeAttentionKind kind) =>
        ToSeverity(kind) == AttentionSeverity.Critical ? 0 : 1;
}
