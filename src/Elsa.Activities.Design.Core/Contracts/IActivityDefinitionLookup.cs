using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Core.Contracts;

public interface IActivityDefinitionLookup
{
    Task<IActivityDefinition> GetDefinition(string idOrUniqueName, CancellationToken cancellationToken = default);

    Task<IEnumerable<IActivityDefinition>> ListDefinitions(string? category = null, bool? isBrowsable = null, CancellationToken cancellationToken = default);

    Task<IActivityDefinitionVersion> GetVersion(string versionId, CancellationToken cancellationToken = default);

    Task<IEnumerable<ActivityDefinitionVersionInfo>> ListVersions(string definitionId, CancellationToken cancellationToken = default);
}
