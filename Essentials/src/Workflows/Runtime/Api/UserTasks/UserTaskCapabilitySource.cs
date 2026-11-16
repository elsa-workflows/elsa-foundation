using Elsa.Api.Capabilities.Contracts;
using Elsa.Api.Capabilities.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.UserTasks;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.Api.UserTasks;

internal sealed class UserTaskCapabilitySource(IServiceProvider services) : IApiCapabilitySource
{
    public ValueTask<IReadOnlyCollection<ApiCapabilityDeclaration>> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        var journal = services.GetRequiredService<IUserTaskStore>();
        var executions = services.GetRequiredService<IWorkflowExecutionStateStore>();
        var physical = executions.GetType() == typeof(CoalescingWorkflowExecutionStateStore)
            ? services.GetService<CoalescingInner<IWorkflowExecutionStateStore>>()?.Value : executions;
        var ready = journal.IsDurable
            ? physical is not null && services.GetService<IUserTaskPersistenceBoundary>()?.CanRetainCompletionEvidence(journal, physical) == true
            : services.GetService<IOptions<UserTaskOptions>>()?.Value.AllowVolatileTasksForDevelopment == true &&
                physical?.GetType() == typeof(InMemoryWorkflowExecutionStateStore);
        var activities = services.GetService<IActivityExecutionStateStore>();
        var physicalActivities = activities?.GetType() == typeof(CoalescingActivityExecutionStateStore)
            ? services.GetService<CoalescingInner<IActivityExecutionStateStore>>()?.Value : activities;
        var bookmarks = services.GetService<IBookmarkStateStore>();
        var checkpoints = services.GetService<IRuntimeCheckpointCommitStore>();
        ready = ready && physical is not null && physicalActivities is not null && bookmarks is not null &&
            checkpoints?.SupportsExactBookmarkResume == true && checkpoints.CanCommitExactBookmarkResume(bookmarks, physical, physicalActivities);
        return ValueTask.FromResult<IReadOnlyCollection<ApiCapabilityDeclaration>>(ready ? [Declaration] : []);
    }
    private static readonly ApiCapabilityDeclaration Declaration = new Elsa.Api.Capabilities.Models.ApiCapabilityDeclaration("elsa.api.user-tasks", 1,
            [new("tasks", "runtime/user-tasks"), new("task", "runtime/user-tasks/{id}", templated: true),
             new("claim", "runtime/user-tasks/{id}/claim", templated: true), new("release", "runtime/user-tasks/{id}/release", templated: true),
             new("complete", "runtime/user-tasks/{id}/complete", templated: true), new("reassign", "runtime/user-tasks/{id}/reassign", templated: true),
             new("forms", "runtime/user-task-forms"), new("form-version", "runtime/user-task-forms/{id}/versions/{version}", templated: true), new("targets", "runtime/user-task-targets"), new("identity", "runtime/user-task-identity")], "WorkflowsRuntimeApi.UserTasks");
}
