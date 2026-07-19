using Elsa.Api.Capabilities.Models;

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
