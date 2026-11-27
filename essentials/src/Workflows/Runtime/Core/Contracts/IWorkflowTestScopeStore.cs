using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Single-provider replacement contract for durable workflow test-scope lifecycle storage.
/// Feature composition must reject multiple implementations; consumers must not resolve this contract as a contribution collection.
/// </summary>
public interface IWorkflowTestScopeStore
{
    ValueTask<WorkflowTestScopeRecord> CreateAsync(
        WorkflowTestScope scope,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default);

    ValueTask<WorkflowTestScopeRecord?> FindAsync(
        string scopeId,
        CancellationToken cancellationToken = default);

    ValueTask<WorkflowTestScopeCloseResult> CloseAsync(
        WorkflowTestScopeCloseRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<WorkflowTestScopeRecord> CompleteAsync(
        string scopeId,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default);

    ValueTask<WorkflowTestScopePage> QueryAsync(
        WorkflowTestScopePageQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Single-provider replacement contract for asserting immutable test-scope context inside a provider-owned admission transaction.
/// Feature composition must reject multiple implementations; this precondition must not be implemented as a racy service-level check.
/// </summary>
public interface IWorkflowTestScopeAdmissionStore
{
    ValueTask AssertOpenAsync(
        WorkflowTestScope scope,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits one admission for an exact isolated actor while its immutable scope is open and unexpired.
    /// A committed admission may reach the actor while teardown is in progress. It grants no later admission.
    /// </summary>
    /// <exception cref="TestScopeAdmissionException">The store cannot prove admission for the selected actor.</exception>
    ValueTask AdmitIsolatedActorAsync(
        WorkflowTestScope scope,
        string actorReference,
        Func<DateTimeOffset> readClock,
        CancellationToken cancellationToken = default) =>
        throw new TestScopeAdmissionException("The store does not support isolated Test Run actor admission.");
}

/// <summary>
/// Single-provider replacement contract for atomically resolving bounded detached dispatches against scope teardown.
/// Feature composition must reject multiple implementations; waited dispatches are outside this contract.
/// </summary>
public interface IWorkflowTestScopeCleanupStore
{
    ValueTask<WorkflowTestScopeCleanupResult> CleanupAsync(
        WorkflowTestScope scope,
        DateTimeOffset requestedAt,
        int pageSize,
        IReadOnlyDictionary<string, RuntimePostCommitIntent> cancellationIntents,
        string? continuationToken = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Application service that closes and reconciles detached work for one authoritative test scope.</summary>
public interface IWorkflowTestScopeCleaner
{
    ValueTask<WorkflowTestScopeCleanupResult> CloseAsync(
        string scopeId,
        WorkflowTestScopeCloseReason reason,
        DateTimeOffset requestedAt,
        CancellationToken cancellationToken = default);

    ValueTask<int> SweepAsync(
        DateTimeOffset observedAt,
        CancellationToken cancellationToken = default);
}
