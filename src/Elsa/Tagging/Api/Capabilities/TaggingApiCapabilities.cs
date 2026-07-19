using Elsa.Api.Capabilities.Contracts;
using Elsa.Api.Capabilities.Models;
using Elsa.Tagging.Core.Contracts;

namespace Elsa.Tagging.Api.Capabilities;

public static class TaggingApiCapabilities
{
    public const string CapabilityId = "elsa.api.tagging";
    public const string SourceFeatureId = "TaggingApi";

    public static ApiCapabilityDeclaration StaticDeclaration { get; } = new(
        CapabilityId,
        1,
        [
            new("tag-definitions", "tagging/definitions"),
            new(
                "tag-definition-values",
                "tagging/definitions/{tagDefinitionId}/values",
                templated: true)
        ],
        SourceFeatureId);
}

/// <summary>Advertises the catalog only when a durable catalog persistence provider is composed.</summary>
public sealed class TaggingOperationalCapabilitySource(
    ITagDefinitionCatalogPersistence? catalogPersistence = null,
    IControlledTagValueCatalogPersistence? controlledValuePersistence = null) : IApiCapabilitySource
{
    public ValueTask<IReadOnlyCollection<ApiCapabilityDeclaration>> GetCapabilitiesAsync(
        CancellationToken cancellationToken = default)
    {
        if (catalogPersistence is null)
            return ValueTask.FromResult<IReadOnlyCollection<ApiCapabilityDeclaration>>([]);

        if (controlledValuePersistence is not null)
            return ValueTask.FromResult<IReadOnlyCollection<ApiCapabilityDeclaration>>(
                [TaggingApiCapabilities.StaticDeclaration]);

        var links = TaggingApiCapabilities.StaticDeclaration.Links
            .Where(link => link.Rel != "tag-definition-values")
            .ToArray();
        return ValueTask.FromResult<IReadOnlyCollection<ApiCapabilityDeclaration>>(
        [
            new ApiCapabilityDeclaration(
                TaggingApiCapabilities.CapabilityId,
                1,
                links,
                TaggingApiCapabilities.SourceFeatureId)
        ]);
    }
}
