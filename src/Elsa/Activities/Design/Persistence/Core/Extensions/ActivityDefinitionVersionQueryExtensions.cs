using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Persistence.Core;
using Elsa.Primitives.Persistence;

namespace Elsa.Activities.Design.Persistence.Core.Extensions;

public static class ActivityDefinitionVersionQueryExtensions
{
    public static async Task<ActivityDefinitionVersion> GetVersionInlcudingDefinition(this IQueries<ActivityDefinitionVersion> queries, string versionId, CancellationToken cancellationToken)
    {
        var filter = new ActivityDefinitionVersionFilter { Id = versionId };
        var result = await queries.Find(
            filter,
            include: entity => entity.Definition,
            cancellationToken
        );

        return result ?? throw new ArgumentException($"Activity definition version with id '{versionId}' does not exist");
    }

    /// <summary>
    /// Resolves the single version for <paramref name="definitionId"/> whose semantic version equals
    /// <paramref name="version"/> (FR-013, build-metadata-insensitive), or <c>null</c> if none. The
    /// match is exact on precedence — not nearest — so <c>1.0.0</c> and <c>1.0.0+build</c> resolve the
    /// same logical version while <c>9.9.9</c> resolves nothing.
    /// </summary>
    public static Task<ActivityDefinitionVersion?> FindExactVersion(this IQueries<ActivityDefinitionVersion> queries, string definitionId, string version, CancellationToken cancellationToken = default)
    {
        var filter = new ActivityDefinitionVersionFilter { DefinitionId = definitionId, Version = version };
        return queries.Find(filter, cancellationToken);
    }
}
