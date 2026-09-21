using Elsa.Api.Capabilities.Contracts;
using Elsa.Api.Capabilities.Events;
using Elsa.Api.Capabilities.Exceptions;
using Elsa.Api.Capabilities.Models;
using Elsa.Events.Core.Contracts;

namespace Elsa.Api.Capabilities.Services;

public sealed class ApiCapabilityCatalog(
    IEnumerable<ApiCapabilityDeclaration> staticDeclarations,
    IEnumerable<IApiCapabilitySource> sources,
    IInlineEventPublisher? eventPublisher = null) : IApiCapabilityCatalog
{
    public async Task<ApiCapabilitiesDocument> GetAsync(CancellationToken cancellationToken = default)
    {
        var staticEntries = staticDeclarations.ToArray();
        var contributions = new List<ApiCapabilityDeclaration>();
        foreach (var source in sources)
        {
            var sourceContributions = await source.GetCapabilitiesAsync(cancellationToken);
            contributions.AddRange(sourceContributions);
        }

        if (eventPublisher is not null)
            await eventPublisher.Publish(new ApiCapabilitiesCollecting(contributions), cancellationToken);

        var staticById = staticEntries
            .GroupBy(declaration => declaration.CapabilityId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, ValidateStaticDeclarations, StringComparer.Ordinal);
        var contributionsById = contributions
            .GroupBy(declaration => declaration.CapabilityId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var capabilityIds = staticById.Keys.Concat(contributionsById.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        var capabilities = capabilityIds
            .Select(id => Merge(id, staticById.GetValueOrDefault(id), contributionsById.GetValueOrDefault(id) ?? []))
            .ToArray();
        return new ApiCapabilitiesDocument(capabilities);
    }

    private static ApiCapabilityDeclaration ValidateStaticDeclarations(IGrouping<string, ApiCapabilityDeclaration> declarations)
    {
        var entries = declarations.ToArray();
        var canonical = entries[0];
        if (entries.Skip(1).Any(candidate => !canonical.IsEquivalentTo(candidate)))
        {
            var sources = string.Join(", ", entries.Select(x => x.SourceFeatureId).OrderBy(x => x, StringComparer.Ordinal));
            throw new ApiCapabilityConflictException(
                $"Capability '{declarations.Key}' has incompatible declarations from features: {sources}.");
        }

        return canonical;
    }

    private static ApiCapabilityView Merge(
        string capabilityId,
        ApiCapabilityDeclaration? staticDeclaration,
        IReadOnlyCollection<ApiCapabilityDeclaration> contributions)
    {
        var entries = staticDeclaration is null ? contributions.ToArray() : [staticDeclaration, .. contributions];
        var canonical = entries[0];
        var links = canonical.Links.ToDictionary(link => link.Rel, StringComparer.Ordinal);
        var conflict = false;
        foreach (var candidate in entries.Skip(1))
        {
            if (candidate.ContractMajorVersion != canonical.ContractMajorVersion)
            {
                conflict = true;
                break;
            }

            foreach (var link in candidate.Links)
            {
                if (links.TryGetValue(link.Rel, out var existing) && existing != link)
                {
                    conflict = true;
                    break;
                }
                links.TryAdd(link.Rel, link);
            }
            if (conflict)
                break;
        }
        if (conflict)
        {
            var sources = string.Join(", ", entries.Select(x => x.SourceFeatureId).OrderBy(x => x, StringComparer.Ordinal));
            throw new ApiCapabilityConflictException(
                $"Capability '{capabilityId}' has incompatible contributions from features: {sources}.");
        }

        return new ApiCapabilityView(
            canonical.CapabilityId,
            canonical.ContractMajorVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            links.Values
                .OrderBy(link => link.Rel, StringComparer.Ordinal)
                .Select(link => new ApiCapabilityLinkView(link.Rel, link.Href, link.Templated))
                .ToArray());
    }

}
