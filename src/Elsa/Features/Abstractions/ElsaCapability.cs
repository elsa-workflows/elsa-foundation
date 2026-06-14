namespace Elsa.Features.Abstractions;

public sealed record ElsaCapability(
    string Id,
    string DisplayName,
    string Version,
    string SourceFeature,
    IReadOnlyCollection<string> Tags);
