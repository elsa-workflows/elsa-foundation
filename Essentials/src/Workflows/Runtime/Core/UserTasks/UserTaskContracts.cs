using Elsa.Workflows.Runtime.Core.Contracts;
using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.UserTasks;

/// <summary>Replacement journal. Conditional writes commit aggregate/history/decision together; stale writes return false.</summary>
public interface IUserTaskStore
{
    bool IsDurable { get; }
    ValueTask<UserTaskRecord?> FindAsync(string tenantId, string id, CancellationToken cancellationToken = default);
    ValueTask<bool> TryWriteAsync(UserTaskRecord record, long? expectedRevision, CancellationToken cancellationToken = default);
    ValueTask<UserTaskPage> QueryAsync(UserTaskQuery query, CancellationToken cancellationToken = default);
    /// <summary>Fair bounded nonterminal scan within the current persistence partition; never global.</summary>
    ValueTask<UserTaskPage> RecoverAsync(int limit, string? after, CancellationToken cancellationToken = default);
    ValueTask<UserTaskFormVersion?> FindFormAsync(string tenantId, string id, string version, CancellationToken cancellationToken = default);
    ValueTask<UserTaskFormPage> ListFormsAsync(string tenantId, int limit, string? after = null, CancellationToken cancellationToken = default);
    /// <summary>Immutable insert. Existing identical versions may replay; differing content is a conflict.</summary>
    ValueTask<bool> TryCreateFormAsync(UserTaskFormVersion version, CancellationToken cancellationToken = default);
}

/// <summary>Replacement current identity/eligibility authority. Unsupported hosts refuse; inputs never grant authority.</summary>
public interface IUserTaskAccess
{
    ValueTask<UserTaskActor> GetActorAsync(CancellationToken cancellationToken = default);
    ValueTask AuthorizeAsync(UserTaskRecord task, string operation, CancellationToken cancellationToken = default);
    ValueTask ValidateAssignmentAsync(string tenantId, string? assigneeId, string? groupId, CancellationToken cancellationToken = default);
}

/// <summary>Additive provider integration with unique Provider identity and authoritative response validation.</summary>
public interface IUserTaskFormProvider
{
    string Provider { get; }
    void ValidateDefinition(UserTaskForm form);
    ValueTask<IReadOnlyDictionary<string, string>> ValidateResponseAsync(UserTaskForm form, JsonElement prefill, JsonElement response, bool requireFields, CancellationToken cancellationToken = default);
}

/// <summary>Activity-facing staging/result seam; no HTTP or Design dependency.</summary>
public interface IUserTaskRuntime
{
    ValueTask<UserTaskRecord> StageAsync(UserTaskCreation request, CancellationToken cancellationToken = default);
    ValueTask<UserTaskResponse> ReadAcceptedAsync(string tenantId, string taskId, string completionId, CancellationToken cancellationToken = default);
    ValueTask ReconcileAsync(UserTaskRecord task, CancellationToken cancellationToken = default);
}

/// <summary>Participant/manager operations; each call obtains fresh trusted authority.</summary>
public interface IUserTaskOperations
{
    ValueTask<UserTaskPage> ListAsync(string view, int limit = 25, string? after = null, string? search = null, CancellationToken cancellationToken = default);
    ValueTask<UserTaskRecord> GetAsync(string id, CancellationToken cancellationToken = default);
    ValueTask<UserTaskRecord> ClaimAsync(string id, UserTaskMutation request, CancellationToken cancellationToken = default);
    ValueTask<UserTaskRecord> ReleaseAsync(string id, UserTaskMutation request, CancellationToken cancellationToken = default);
    ValueTask<UserTaskRecord> ReassignAsync(string id, UserTaskAssignment request, CancellationToken cancellationToken = default);
    ValueTask<UserTaskRecord> CompleteAsync(string id, UserTaskCompletion request, CancellationToken cancellationToken = default);
    ValueTask<UserTaskFormPage> ListFormsAsync(int limit = 25, string? after = null, CancellationToken cancellationToken = default);
    ValueTask<UserTaskFormVersion> GetFormAsync(string id, string version, CancellationToken cancellationToken = default);
    ValueTask<UserTaskFormVersion> CreateFormAsync(UserTaskForm form, CancellationToken cancellationToken = default);
}

/// <summary>Provider qualification for retaining canonical completion evidence until the selected task journal acknowledges it.</summary>
public interface IUserTaskPersistenceBoundary
{
    bool CanRetainCompletionEvidence(IUserTaskStore journal, IWorkflowExecutionStateStore executions);
}
