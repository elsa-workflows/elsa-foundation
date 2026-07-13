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
        var declarations = staticDeclarations.ToList();
        foreach (var source in sources)
        {
            var contributions = await source.GetCapabilitiesAsync(cancellationToken);
            declarations.AddRange(contributions);
        }

        if (eventPublisher is not null)
            await eventPublisher.Publish(new CollectingApiCapabilities(declarations), cancellationToken);

        var capabilities = declarations
            .GroupBy(declaration => declaration.CapabilityId, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(Merge)
            .ToArray();
        return new ApiCapabilitiesDocument(capabilities);
    }

    private static ApiCapabilityView Merge(IGrouping<string, ApiCapabilityDeclaration> declarations)
    {
        var entries = declarations.ToArray();
        var canonical = entries[0];
        if (entries.Skip(1).Any(candidate => !canonical.IsCompatibleWith(candidate)))
        {
            var sources = string.Join(", ", entries.Select(x => x.SourceFeatureId).OrderBy(x => x, StringComparer.Ordinal));
            throw new ApiCapabilityConflictException(
                $"Capability '{declarations.Key}' has incompatible declarations from features: {sources}.");
        }

        return new ApiCapabilityView(
            canonical.CapabilityId,
            canonical.ContractMajorVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            canonical.Links
                .OrderBy(link => link.Rel, StringComparer.Ordinal)
                .Select(link => new ApiCapabilityLinkView(link.Rel, link.Href, link.Templated))
                .ToArray());
    }

}
