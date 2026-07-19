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
        [new("tag-definitions", "tagging/definitions")],
        SourceFeatureId);
}

/// <summary>Advertises the catalog only when a durable catalog persistence provider is composed.</summary>
public sealed class TaggingOperationalCapabilitySource(
    ITagDefinitionCatalogPersistence? catalogPersistence = null) : IApiCapabilitySource
{
    public ValueTask<IReadOnlyCollection<ApiCapabilityDeclaration>> GetCapabilitiesAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyCollection<ApiCapabilityDeclaration>>(
            catalogPersistence is null ? [] : [TaggingApiCapabilities.StaticDeclaration]);
}
