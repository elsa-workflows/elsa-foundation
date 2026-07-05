using Elsa.Activities.Design.Core.Contracts;
using Elsa.Primitives.Exceptions;

namespace Elsa.Workflows.Design.Validations.Internal;

/// <summary>
/// Scoped, memoizing catalog resolution for the baseline validators.
/// <see cref="IActivityDefinitionLookup.GetVersion"/> is a passthrough to the version store, so
/// repeated ActivityVersionIds across the activity tree — and across the validators sharing one
/// validation pass — would otherwise each round-trip. Translates the store's throwing Get
/// contract (<see cref="EntityNotFoundException"/> on a missing id; it never returns null) into
/// a nullable result: null means the version does not exist in the catalog.
/// </summary>
public sealed class CatalogVersionResolver(IActivityDefinitionLookup catalog)
{
    private readonly Dictionary<string, IActivityDefinitionVersion?> _cache = new(StringComparer.Ordinal);

    public async Task<IActivityDefinitionVersion?> Find(string activityVersionId, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(activityVersionId, out var cached))
            return cached;

        IActivityDefinitionVersion? version;
        try
        {
            version = await catalog.GetVersion(activityVersionId, cancellationToken);
        }
        catch (EntityNotFoundException)
        {
            version = null;
        }

        _cache[activityVersionId] = version;
        return version;
    }
}
