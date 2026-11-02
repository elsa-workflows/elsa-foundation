using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.UserTasks;

namespace Elsa.Workflows.Runtime.UserTasks;

internal sealed class ValidateUserTaskComposition(IEnumerable<IUserTaskStore> stores, IEnumerable<IUserTaskAccess> access,
    IEnumerable<IUserTaskOperations> operations, IEnumerable<IUserTaskRuntime> runtime, IEnumerable<IUserTaskFormProvider> forms,
    IEnumerable<IUserTaskPersistenceBoundary> boundaries) : IStartupTask
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (stores.Count() != 1 || access.Count() != 1 || operations.Count() != 1 || runtime.Count() != 1 || boundaries.Count() > 1)
            throw new InvalidOperationException("User Tasks requires one owner for each replacement contract and at most one persistence boundary.");
        if (forms.Any(x => string.IsNullOrWhiteSpace(x.Provider)) || forms.GroupBy(x => x.Provider, StringComparer.Ordinal).Any(x => x.Count() != 1))
            throw new InvalidOperationException("User Task form provider identities must be nonempty and unique.");
        return Task.CompletedTask;
    }
}
