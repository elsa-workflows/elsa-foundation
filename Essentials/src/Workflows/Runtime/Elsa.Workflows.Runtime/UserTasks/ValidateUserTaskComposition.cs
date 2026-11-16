using Elsa.Tasks.Core;
using Microsoft.Extensions.DependencyInjection;
using Elsa.Workflows.Runtime.Core.UserTasks;

namespace Elsa.Workflows.Runtime.UserTasks;

internal sealed record UserTaskCompositionDescriptors(Func<ServiceDescriptor[]> Read);

internal sealed class ValidateUserTaskComposition(UserTaskCompositionDescriptors descriptors, IEnumerable<IUserTaskFormProvider> forms) : IStartupTask
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var registrations = descriptors.Read();
        int Count<T>() => registrations.Count(x => x.ServiceType == typeof(T));
        if (Count<IUserTaskStore>() != 1 || Count<IUserTaskAccess>() != 1 || Count<IUserTaskOperations>() != 1 || Count<IUserTaskRuntime>() != 1 || Count<IUserTaskPersistenceBoundary>() > 1)
            throw new InvalidOperationException("User Tasks requires one owner for each replacement contract and at most one persistence boundary.");
        if (forms.Any(x => string.IsNullOrWhiteSpace(x.Provider)) || forms.GroupBy(x => x.Provider, StringComparer.Ordinal).Any(x => x.Count() != 1))
            throw new InvalidOperationException("User Task form provider identities must be nonempty and unique.");
        return Task.CompletedTask;
    }
}
