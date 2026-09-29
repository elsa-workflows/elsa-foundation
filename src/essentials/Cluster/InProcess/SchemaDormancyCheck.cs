using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Core.Options;
using Elsa.Cluster.Core.Services;
using Elsa.Primitives.Exceptions;
using Microsoft.Extensions.Options;

namespace Elsa.Cluster.InProcess;

/// <summary>
/// The default shared dormancy check (spec 182, FR-003): <see cref="SchemaDormancyRule"/> over what this container's
/// <see cref="IObservedSchemaFinalization"/> has observed. Without a source it has observed nothing, so every requirement
/// is unmet rather than assumed met.
/// </summary>
/// <remarks>
/// Every answer comes from memory. Only an unmet requirement whose observation is older than
/// <see cref="SchemaDormancyOptions.RefreshBound"/> makes the observation be refreshed before the answer is given (FR-014).
/// </remarks>
public sealed class SchemaDormancyCheck(IOptions<SchemaDormancyOptions>? options = null, IObservedSchemaFinalization? observed = null) : ISchemaDormancyCheck
{
    private readonly TimeSpan _refreshBound = options?.Value.RefreshBound ?? new SchemaDormancyOptions().RefreshBound;

    public SchemaAvailability Evaluate(IEnumerable<SchemaVersionRequirement> requirements) =>
        SchemaDormancyRule.Evaluate(requirements, family => observed?.Find(family));

    public async ValueTask<SchemaAvailability> EvaluateAsync(IEnumerable<SchemaVersionRequirement> requirements, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        var asked = requirements.ToArray();
        var availability = Evaluate(asked);
        if (availability.IsAvailable || observed is null)
            return availability;

        // FR-014: a stale view must not refuse what finalization already allows. The source refreshes only what it read
        // longer ago than the bound, so this costs nothing when the view is fresh.
        foreach (var family in availability.Unmet.Select(unmet => unmet.Requirement.Family).Distinct(StringComparer.Ordinal))
            await observed.RefreshAsync(family, _refreshBound, cancellationToken);
        return Evaluate(asked);
    }

    public async ValueTask EnsureAvailableAsync(IEnumerable<SchemaVersionRequirement> requirements, string? featureId = null, CancellationToken cancellationToken = default)
    {
        var availability = await EvaluateAsync(requirements, cancellationToken);
        if (availability.IsAvailable)
            return;

        var first = availability.Unmet[0];
        throw new SchemaDormancyRefusedException(
            first.Requirement.Family,
            first.ObservedVersion ?? "(none)",
            first.Requirement.Version,
            featureId,
            availability.Reason);
    }

    public IReadOnlyList<SchemaFamilyObservation> Observe() => observed?.Observe() ?? [];

    public async ValueTask<IReadOnlyList<SchemaFamilyObservation>> ReadStatusAsync(CancellationToken cancellationToken = default) =>
        observed is null ? [] : await observed.ReadStatusAsync(cancellationToken);
}
