using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Dispatch;

/// <summary>
/// When a test run's scope must be open, and what open means. Every store applies this one rule inside its own atomic
/// boundary, against the scope record it read there (spec 102).
/// </summary>
/// <remarks>
/// Openness is checked only where test work is admitted: when a root test run starts (FR-024) and when a child dispatch is
/// added (research Decision 4). A child's own start is not re-checked. Its admission (<c>TryAdmitAsync</c>) is what
/// serializes against cleanup, and a child that won admission before teardown must start and then be cancelled by cleanup
/// (FR-011), even when its scope began closing before the child's first checkpoint.
/// </remarks>
public static class WorkflowTestScopeAdmission
{
    /// <summary>The scope that must be open for this checkpoint to create <paramref name="execution"/>, if any.</summary>
    public static WorkflowTestScope? ScopeRequiredToStart(WorkflowExecutionState execution, bool executionExists)
    {
        ArgumentNullException.ThrowIfNull(execution);
        return execution is { TestScope: { } scope, ParentWorkflowExecutionId: null } && !executionExists ? scope : null;
    }

    /// <summary>The scope that must be open for this checkpoint to add <paramref name="dispatch"/>, if any.</summary>
    public static WorkflowTestScope? ScopeRequiredToAdd(WorkflowDispatchRecord dispatch, bool dispatchExists)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        return dispatchExists ? null : dispatch.TestScope;
    }

    /// <summary>
    /// Ensures the persisted scope is the same scope, still open, and unexpired at <paramref name="observedAt"/>. The message
    /// deliberately names no scope, tenant, or owner.
    /// </summary>
    public static void EnsureOpen(WorkflowTestScopeRecord? persisted, WorkflowTestScope expected, DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (persisted is null ||
            persisted.State != WorkflowTestScopeState.Open ||
            persisted.Scope.IsExpired(observedAt) ||
            !WorkflowTestScope.ContextEquals(persisted.Scope, expected))
        {
            throw new TestScopeAdmissionException("The workflow test scope is not open in the current persistence context.");
        }
    }
}
