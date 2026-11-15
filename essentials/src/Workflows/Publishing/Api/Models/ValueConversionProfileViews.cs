using Elsa.Primitives.Models;

namespace Elsa.Workflows.Publishing.Api.Models;

/// <summary>Stable identity of a registered conversion profile as seen by visual authoring pickers.</summary>
public sealed record ValueConversionProfileReferenceView(string Id, string Version);

/// <summary>
/// A registered conversion profile projected for visual authoring pickers: its versioned identity and the
/// representation-aware capabilities publication will honor. Runtime never rediscovers these from configuration.
/// </summary>
public sealed record ValueConversionProfileView(
    ValueConversionProfileReferenceView Profile,
    IReadOnlyCollection<ValueRepresentation> SupportedSourceRepresentations,
    IReadOnlyCollection<string> SupportedTargetAliases);

public sealed record ValueConversionProfilesResponse(IReadOnlyCollection<ValueConversionProfileView> Items);
