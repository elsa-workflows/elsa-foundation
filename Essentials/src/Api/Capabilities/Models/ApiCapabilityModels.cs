using System.Text.Json.Serialization;

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

    /// <summary>
    /// Set while the feature that owns the capability is dormant (spec 182, FR-007): the capability is still advertised,
    /// marked dormant with this caller-neutral reason, so a client can explain a disabled control rather than hide it. A
    /// typed <c>IApiCapabilitySource</c> sets it from the shared dormancy check's reason, which names no host; it is
    /// <see langword="null"/> while the capability is available.
    /// </summary>
    public string? DormantReason
    {
        get => _dormantReason;
        init => _dormantReason = value is null || !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException("A dormant capability states why it is dormant.", nameof(DormantReason));
    }

    private readonly string? _dormantReason;

    internal bool IsEquivalentTo(ApiCapabilityDeclaration other) =>
        ContractMajorVersion == other.ContractMajorVersion && Links.SequenceEqual(other.Links);
}

public sealed record ApiCapabilitiesDocument(IReadOnlyCollection<ApiCapabilityView> Capabilities);

public sealed record ApiCapabilityView(
    string Id,
    string ContractVersion,
    IReadOnlyCollection<ApiCapabilityLinkView> Links)
{
    /// <summary>The status every capability that is dormant carries; an available one carries none.</summary>
    public const string DormantStatus = "dormant";

    /// <summary>
    /// <see cref="DormantStatus"/> while the feature owning the capability is dormant (spec 182, FR-007 and SC-008), and
    /// omitted while it is available, so the document of a host with nothing dormant is unchanged.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Status { get; init; }

    /// <summary>Why the capability is dormant, caller-neutral; omitted while it is available.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; init; }
}

public sealed record ApiCapabilityLinkView(string Rel, string Href, bool Templated = false);
