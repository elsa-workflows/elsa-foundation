using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Persistence.Core.Stores;

namespace Elsa.Activities.Design.Tests.Integration;

/// <summary>
/// Inert read ports used to construct the lookup in tests that only exercise paths going
/// through <see cref="Microsoft.EntityFrameworkCore.IDbContextFactory{TContext}"/>. Every method
/// throws — if a test inadvertently routes through these dependencies, the test fails loudly
/// rather than silently bypassing the picker-visibility query.
/// </summary>
internal sealed class ThrowingActivityDefinitionStore : IActivityDefinitionStore
{
    private const string Message = "Test routed through ThrowingActivityDefinitionStore; this test should only exercise the IDbContextFactory path.";

    public Task<ActivityDefinition> GetAsync(string id, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
    public Task<ActivityDefinition?> FindAsync(ActivityDefinitionFilter filter, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
    public Task<ActivityDefinition?> FindByIdOrActivityTypeKeyAsync(string id, string activityTypeKey, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
    public Task<bool> ExistsByActivityTypeKeyAsync(string activityTypeKey, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
    public Task<IReadOnlyList<ActivityDefinition>> ListAsync(ActivityDefinitionFilter filter, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
}

internal sealed class ThrowingActivityDefinitionVersionStore : IActivityDefinitionVersionStore
{
    private const string Message = "Test routed through ThrowingActivityDefinitionVersionStore; this test should only exercise the IDbContextFactory path.";

    public Task<ActivityDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
    public Task<ActivityDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
    public Task<ActivityDefinitionVersion?> FindByDefinitionAndSortKeyAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
    public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
    public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionIdsAsync(IEnumerable<string> definitionIds, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
    public Task<IReadOnlyList<ActivityDefinitionVersion>> ListAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
}
