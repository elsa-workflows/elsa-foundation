using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Core.Services;
using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Core.Models;

namespace Elsa.Modularity.Api.Services;

/// <summary>
/// Carries each enabled feature's availability to the feature catalog (spec 182, FR-009): available, or dormant with a
/// reason per unmet dormancy requirement its class declares with <c>[RequiresSchemaVersion]</c>. A dormant feature stays
/// in the catalog, enabled, and says why, rather than simply being absent (FR-008, SC-002).
/// </summary>
/// <remarks>
/// <para>
/// It asks the shared dormancy check, and computes nothing itself (FR-003). The catalog is an operator surface, so a
/// dormant feature's reason carries the finalization gate's status, including the hosts that cannot read a version yet
/// (FR-011; spec 181, FR-022). That status is read only when something is dormant; a catalog with nothing dormant reads no
/// record. It is computed on every request, so it follows a finalization on the next one (FR-020).
/// </para>
/// <para>
/// A feature with no live descriptor on this host, such as one whose package is not loaded, has no class to read
/// requirements from, so its availability is left unknown rather than claimed.
/// </para>
/// </remarks>
public sealed class FeatureAvailabilityCatalogContributor(ISchemaDormancyCheck check) : IFeatureCatalogContributor
{
    public async Task ContributeAsync(FeatureCatalogContributionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var dormant = new List<(FeatureCatalogItemBuilder Feature, SchemaAvailability Availability)>();
        foreach (var feature in context.Items.Values.Where(feature => feature is { Enabled: true, FeatureType: not null }))
        {
            var requirements = SchemaVersionRequirement.DeclaredBy(feature.FeatureType!);
            var availability = requirements.Count == 0 ? SchemaAvailability.Available : await check.EvaluateAsync(requirements, cancellationToken);
            if (availability.IsAvailable)
                feature.Availability = FeatureAvailability.Available;
            else
                dormant.Add((feature, availability));
        }

        if (dormant.Count == 0)
            return;

        var statuses = await ReadStatusAsync(cancellationToken);
        foreach (var (feature, availability) in dormant)
        {
            feature.Availability = new FeatureAvailability(
                FeatureAvailability.DormantStatus,
                availability.Unmet
                    .Select(unmet => new FeatureAvailabilityReason(
                        unmet.Requirement.Family,
                        unmet.Requirement.Version,
                        unmet.Kind.ToString(),
                        SchemaDormancyReasons.ForOperator(unmet, statuses.GetValueOrDefault(unmet.Requirement.Family))))
                    .ToArray());
        }
    }

    /// <summary>
    /// The gate's status per family, or none when it cannot be read: the catalog still lists the feature as dormant with
    /// its reason, and only loses the fleet's detail.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, SchemaFamilyObservation>> ReadStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            return (await check.ReadStatusAsync(cancellationToken))
                .GroupBy(family => family.Family, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new Dictionary<string, SchemaFamilyObservation>(StringComparer.Ordinal);
        }
    }
}
