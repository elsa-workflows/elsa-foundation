namespace Elsa.Api.Capabilities.Models;

public sealed record ApiCapabilityLink
{
    public ApiCapabilityLink(string rel, string href, bool templated = false)
    {
        if (string.IsNullOrWhiteSpace(rel))
            throw new ArgumentException("A capability link relation is required.", nameof(rel));
        if (string.IsNullOrWhiteSpace(href))
            throw new ArgumentException("A capability link href is required.", nameof(href));
        if (href.StartsWith('/') || Uri.TryCreate(href, UriKind.Absolute, out _))
            throw new ArgumentException("Capability links must be relative to the active shell route base.", nameof(href));

        Rel = rel;
        Href = href;
        Templated = templated;
    }

    public string Rel { get; init; }
    public string Href { get; init; }
    public bool Templated { get; init; }
}

public sealed record ApiCapabilityDeclaration
{
    public ApiCapabilityDeclaration(
        string capabilityId,
        int contractMajorVersion,
        IReadOnlyCollection<ApiCapabilityLink> links,
        string sourceFeatureId)
    {
        if (string.IsNullOrWhiteSpace(capabilityId))
            throw new ArgumentException("A capability ID is required.", nameof(capabilityId));
        if (contractMajorVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(contractMajorVersion), "The contract major version must be positive.");
        if (string.IsNullOrWhiteSpace(sourceFeatureId))
            throw new ArgumentException("A source feature ID is required for diagnostics.", nameof(sourceFeatureId));

        var duplicateRelation = links
            .GroupBy(link => link.Rel, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateRelation is not null)
            throw new ArgumentException($"Capability link relation '{duplicateRelation.Key}' is declared more than once.", nameof(links));

        CapabilityId = capabilityId;
        ContractMajorVersion = contractMajorVersion;
        Links = links.OrderBy(link => link.Rel, StringComparer.Ordinal).ToArray();
        SourceFeatureId = sourceFeatureId;
    }

    public string CapabilityId { get; init; }
    public int ContractMajorVersion { get; init; }
    public IReadOnlyCollection<ApiCapabilityLink> Links { get; init; }
    public string SourceFeatureId { get; init; }

    internal bool IsEquivalentTo(ApiCapabilityDeclaration other) =>
        ContractMajorVersion == other.ContractMajorVersion && Links.SequenceEqual(other.Links);
}

public sealed record ApiCapabilitiesDocument(IReadOnlyCollection<ApiCapabilityView> Capabilities);

public sealed record ApiCapabilityView(
    string Id,
    string ContractVersion,
    IReadOnlyCollection<ApiCapabilityLinkView> Links);

public sealed record ApiCapabilityLinkView(string Rel, string Href, bool Templated = false);
