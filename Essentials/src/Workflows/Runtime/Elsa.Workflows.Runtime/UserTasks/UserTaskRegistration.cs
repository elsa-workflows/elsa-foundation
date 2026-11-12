using Elsa.Events.Core.Extensions;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Events;
using Elsa.Workflows.Runtime.Core.UserTasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.UserTasks;

internal static class UserTaskRegistration
{
    internal static void AddUserTasks(IServiceCollection services)
    {
        services.AddOptions<UserTaskOptions>();
        services.TryAddSingleton<UserTaskMemoryState>();
        services.TryAddSingleton(new UserTaskCompositionDescriptors(() => services.ToArray()));
        services.TryAddSingleton<UserTaskCursorCodec>();
        services.TryAddScoped<IUserTaskStore, MemoryUserTaskStore>();
        services.TryAddScoped<IUserTaskAccess, UnavailableUserTaskAccess>();
        services.TryAddScoped<IUserTaskOperations, UserTaskOperations>();
        services.TryAddScoped<UserTaskRuntime>();
        services.TryAddScoped<IUserTaskRuntime>(sp => sp.GetRequiredService<UserTaskRuntime>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IUserTaskFormProvider, BasicUserTaskForms>());
        services.TryAddEventHandler<ActivityResumeValidating, UserTaskResumeGuard>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRecurringTask, UserTaskRecoveryTask>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IStartupTask, ValidateUserTaskComposition>());
    }
}

internal sealed class UnavailableUserTaskAccess : IUserTaskAccess
{
    public ValueTask<UserTaskActor> GetActorAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromException<UserTaskActor>(new UserTaskException("forbidden", "This host has no trusted task identity adapter."));
    public ValueTask AuthorizeAsync(UserTaskRecord task, string operation, CancellationToken cancellationToken = default) =>
        ValueTask.FromException(new UserTaskException("forbidden", "This host has no trusted task permission adapter."));
    public ValueTask ValidateAssignmentAsync(string tenantId, string? assigneeId, string? groupId, CancellationToken cancellationToken = default) =>
        ValueTask.FromException(new UserTaskException("forbidden", "This host has no trusted assignment adapter."));
}
