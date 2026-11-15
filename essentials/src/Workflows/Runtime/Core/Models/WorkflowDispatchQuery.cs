namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Bounded provider-neutral dispatch query. Tenant scope comes from the active store context.</summary>
public sealed class WorkflowDispatchQuery
{
    public const int MaximumTake = 100;

    public WorkflowDispatchQuery(
        string? parentWorkflowExecutionId = null,
        string? childWorkflowExecutionId = null,
        WorkflowDispatchStatus? status = null,
        int take = MaximumTake,
        DateTimeOffset? afterCreatedAt = null,
        string? afterDispatchId = null,
        string? testScopeId = null)
    {
        ValidateOptional(parentWorkflowExecutionId, nameof(parentWorkflowExecutionId));
        ValidateOptional(childWorkflowExecutionId, nameof(childWorkflowExecutionId));
        if (testScopeId is not null)
            WorkflowTestScope.ValidateScopeId(testScopeId, nameof(testScopeId));
        if (parentWorkflowExecutionId is null && childWorkflowExecutionId is null && status is null && testScopeId is null)
            throw new ArgumentException("A workflow dispatch query requires at least one operational filter.");
        if (take is <= 0 or > MaximumTake)
            throw new ArgumentOutOfRangeException(nameof(take), $"Workflow dispatch query take must be between 1 and {MaximumTake}.");
        if ((afterCreatedAt is null) != (afterDispatchId is null))
            throw new ArgumentException("Workflow dispatch query continuation requires both creation time and dispatch ID.", nameof(afterDispatchId));
        ValidateOptional(afterDispatchId, nameof(afterDispatchId));

        ParentWorkflowExecutionId = parentWorkflowExecutionId;
        ChildWorkflowExecutionId = childWorkflowExecutionId;
        Status = status;
        Take = take;
        AfterCreatedAt = afterCreatedAt;
        AfterDispatchId = afterDispatchId;
        TestScopeId = testScopeId;
    }

    public string? ParentWorkflowExecutionId { get; }
    public string? ChildWorkflowExecutionId { get; }
    public WorkflowDispatchStatus? Status { get; }
    public int Take { get; }
    public DateTimeOffset? AfterCreatedAt { get; }
    public string? AfterDispatchId { get; }
    public string? TestScopeId { get; }

    private static void ValidateOptional(string? value, string parameterName)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Workflow dispatch query filters cannot be blank.", parameterName);
    }
}
