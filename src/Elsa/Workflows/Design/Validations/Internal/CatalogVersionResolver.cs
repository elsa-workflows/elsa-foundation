using Elsa.Activities.Design.Core.Contracts;

namespace Elsa.Workflows.Design.Validations.Internal;

/// <summary>
/// Scoped, memoizing catalog resolution for the baseline validators.
/// <see cref="IActivityDefinitionLookup.FindVersion"/> is a passthrough to the version store, so
/// repeated ActivityVersionIds across the activity tree — and across the validators sharing one
/// validation pass — would otherwise each round-trip. <c>FindVersion</c> already yields the
/// nullable outcome (null means the version does not exist in the catalog); this type adds the
/// per-pass memoization plus a short-circuit for blank ids.
/// </summary>
public sealed class CatalogVersionResolver(IActivityDefinitionLookup catalog)
{
    private readonly Dictionary<string, IActivityDefinitionVersion?> _cache = new(StringComparer.Ordinal);

    public async Task<IActivityDefinitionVersion?> Find(string activityVersionId, CancellationToken cancellationToken)
    {
        // A node with no version id is unresolvable, not a lookup — treat it as absent so callers
        // report it (Graph/UnknownActivityVersion) rather than crash. A null id would also throw
        // ArgumentNullException from the dictionary below; null/empty is a real transient authoring
        // state (both submit commands guard IsNullOrWhiteSpace on ActivityVersionId).
        if (string.IsNullOrWhiteSpace(activityVersionId))
            return null;

        if (_cache.TryGetValue(activityVersionId, out var cached))
            return cached;

        var version = await catalog.FindVersion(activityVersionId, cancellationToken);
        _cache[activityVersionId] = version;
        return version;
    }
}
