using System.Text.Json;

namespace Elsa.Workflows.Publishing.Releases.Core;

/// <summary>Default and partial answers never imply successful verification.</summary>
public enum ReleaseSemanticStatus { Incomplete = 0, Verified = 1, Invalid = 2, Unsupported = 3 }
public enum ReleaseEdgeRelation { Dependency, Association }
public enum ReleaseRootAction { None = 0, ActivateServing = 1, MakeAvailable = 2 }

public sealed record ReleaseMemberKey(string Kind, string Identity);
public sealed record ReleaseCodecKey(string Kind, string Codec, int CodecVersion);
public sealed record ReleaseSiteKey(string SiteKind, string Discriminator, int SchemaVersion);
public sealed record ReleaseSemanticAdapterIdentity(string Name, string Version);

/// <summary>A tagged exact scalar: integer uses IntegerValue only; semver/opaque use TextValue only.</summary>
public sealed record ReleaseOwnerVersion(string Scheme, string? TextValue, long? IntegerValue);
public sealed record ReleaseSemanticEdge(ReleaseMemberKey From, ReleaseMemberKey To, ReleaseEdgeRelation Relation, string Occurrence);
public sealed record ReleaseSemanticPrerequisite(string Key, ReleaseMemberKey Member, string Kind, string Identity, string Scheme, string Constraint);

/// <summary>Owner-verified eligibility of one exact content/action pair for this selected member; grants no target serving authority.</summary>
public sealed record ReleaseRootCapability(ReleaseMemberKey Content, ReleaseRootAction Action);

/// <summary>Captured manifest descriptor, including the exact tagged owner version. It is not an owner verdict.</summary>
public sealed class ReleaseContentMember(ReleaseBundleMember descriptor, ReleaseOwnerVersion? ownerVersion)
{
    public ReleaseBundleMember Descriptor { get; } = descriptor;
    public ReleaseMemberKey Key { get; } = new(descriptor.Kind, descriptor.Identity);
    public ReleaseCodecKey Codec { get; } = new(descriptor.Kind, descriptor.Codec, descriptor.CodecVersion);
    public ReleaseOwnerVersion? OwnerVersion { get; } = ownerVersion;
}

/// <summary>Indexed captured content only. Implementations never resolve missing content from a source or target store.</summary>
public interface IReleaseContentReader
{
    IReadOnlyList<ReleaseContentMember> Members { get; }
    ReleaseContentMember GetMember(ReleaseMemberKey key);
    /// <summary>Returns a fresh payload copy for exactly this captured member.</summary>
    ReadOnlyMemory<byte> GetPayload(ReleaseMemberKey key);
}

public sealed class ReleaseResourceRequirement(ReleaseMemberKey member, IReadOnlyList<string> origin, string reference, string kind, string schemaVersion)
{
    public ReleaseMemberKey Member { get; } = member;
    public IReadOnlyList<string> Origin { get; } = Array.AsReadOnly(origin.ToArray());
    public string Reference { get; } = reference;
    public string Kind { get; } = kind;
    public string SchemaVersion { get; } = schemaVersion;
}

/// <summary>Stable code and non-secret location only; never raw payload values, paths, URLs or exception text.</summary>
public sealed class ReleaseSemanticDiagnostic(string code, ReleaseMemberKey? member = null, IReadOnlyList<string>? origin = null, string? input = null)
{
    public string Code { get; } = code;
    public ReleaseMemberKey? Member { get; } = member;
    public IReadOnlyList<string> Origin { get; } = Array.AsReadOnly((origin ?? []).ToArray());
    public string? Input { get; } = input;
}

/// <summary>One owner-enumerated occurrence facet. Payload is an inspection copy, never a replacement artifact.</summary>
public sealed class ReleaseSemanticSite(ReleaseMemberKey member, IReadOnlyList<string> origin, string node, string? input, ReleaseSiteKey site, JsonElement payload)
{
    public ReleaseMemberKey Member { get; } = member;
    public IReadOnlyList<string> Origin { get; } = Array.AsReadOnly(origin.ToArray());
    public string Node { get; } = node;
    public string? Input { get; } = input;
    public ReleaseSiteKey Site { get; } = site;
    public JsonElement Payload { get; } = payload.Clone();
}

/// <summary>Complete actual facts for a claimed artifact codec; a successful status also requires exact identity/version and owner hash verification.</summary>
public sealed class ReleaseSemanticFacts(ReleaseSemanticStatus status, ReleaseMemberKey? identity, ReleaseOwnerVersion? ownerVersion,
    bool ownerHashVerified, IReadOnlyList<ReleaseSemanticEdge>? edges = null, IReadOnlyList<ReleaseResourceRequirement>? resources = null,
    IReadOnlyList<ReleaseSemanticPrerequisite>? prerequisites = null, IReadOnlyList<ReleaseSemanticSite>? sites = null,
    IReadOnlyList<ReleaseSemanticDiagnostic>? diagnostics = null, IReadOnlyList<ReleaseRootCapability>? rootCapabilities = null)
{
    public ReleaseSemanticStatus Status { get; } = status;
    public ReleaseMemberKey? Identity { get; } = identity;
    public ReleaseOwnerVersion? OwnerVersion { get; } = ownerVersion;
    public bool OwnerHashVerified { get; } = ownerHashVerified;
    public IReadOnlyList<ReleaseSemanticEdge> Edges { get; } = Array.AsReadOnly((edges ?? []).ToArray());
    public IReadOnlyList<ReleaseResourceRequirement> Resources { get; } = Array.AsReadOnly((resources ?? []).ToArray());
    public IReadOnlyList<ReleaseSemanticPrerequisite> Prerequisites { get; } = Array.AsReadOnly((prerequisites ?? []).ToArray());
    public IReadOnlyList<ReleaseSemanticSite> Sites { get; } = Array.AsReadOnly((sites ?? []).ToArray());
    public IReadOnlyList<ReleaseSemanticDiagnostic> Diagnostics { get; } = Array.AsReadOnly((diagnostics ?? []).ToArray());
    public IReadOnlyList<ReleaseRootCapability> RootCapabilities { get; } = Array.AsReadOnly((rootCapabilities ?? []).ToArray());
}

/// <summary>Complete facts for one occurrence facet. Nested sites must be enumerated by the containing artifact owner.</summary>
public sealed class ReleaseOccurrenceFacts(ReleaseSemanticStatus status, IReadOnlyList<ReleaseSemanticEdge>? edges = null,
    IReadOnlyList<ReleaseResourceRequirement>? resources = null, IReadOnlyList<ReleaseSemanticPrerequisite>? prerequisites = null,
    IReadOnlyList<ReleaseSemanticDiagnostic>? diagnostics = null)
{
    public ReleaseSemanticStatus Status { get; } = status;
    public IReadOnlyList<ReleaseSemanticEdge> Edges { get; } = Array.AsReadOnly((edges ?? []).ToArray());
    public IReadOnlyList<ReleaseResourceRequirement> Resources { get; } = Array.AsReadOnly((resources ?? []).ToArray());
    public IReadOnlyList<ReleaseSemanticPrerequisite> Prerequisites { get; } = Array.AsReadOnly((prerequisites ?? []).ToArray());
    public IReadOnlyList<ReleaseSemanticDiagnostic> Diagnostics { get; } = Array.AsReadOnly((diagnostics ?? []).ToArray());
}

/// <summary>Replacement by exact codec key; ambiguous or invalid claims fail registry composition. Performs no compilation, writes or external content lookup.</summary>
public interface IReleaseArtifactSemanticVerifier
{
    ReleaseSemanticAdapterIdentity Identity { get; }
    IReadOnlyCollection<ReleaseCodecKey> SupportedCodecs { get; }
    ValueTask<ReleaseSemanticFacts> VerifyAsync(ReleaseContentMember member, ReadOnlyMemory<byte> payload,
        IReleaseContentReader content, CancellationToken cancellationToken = default);
}

/// <summary>Replacement by exact occurrence-site key; a complete leaf verdict must be explicit.</summary>
public interface IReleaseOccurrenceInspector
{
    ReleaseSemanticAdapterIdentity Identity { get; }
    IReadOnlyCollection<ReleaseSiteKey> SupportedSites { get; }
    ValueTask<ReleaseOccurrenceFacts> InspectAsync(ReleaseSemanticSite site, IReleaseContentReader content,
        CancellationToken cancellationToken = default);
}

public sealed class ReleaseSemanticVerificationOptions
{
    public string? ExpectedDigest { get; init; }
    public ReleaseBundleLimits? TransportLimits { get; init; }
    public int MaxSites { get; init; } = 100_000;
    public int MaxDiagnostics { get; init; } = 1000;
    /// <summary>Aggregate owner payload bytes inspected per member, including members sharing the same blob.</summary>
    public long MaxInspectionBytes { get; init; } = 256L * 1024 * 1024;
}

public sealed class ReleaseSemanticAdapterCapability(ReleaseSemanticAdapterIdentity identity, IReadOnlyList<ReleaseCodecKey> codecs, IReadOnlyList<ReleaseSiteKey> sites)
{
    public ReleaseSemanticAdapterIdentity Identity { get; } = identity;
    public IReadOnlyList<ReleaseCodecKey> Codecs { get; } = Array.AsReadOnly(codecs.ToArray());
    public IReadOnlyList<ReleaseSiteKey> Sites { get; } = Array.AsReadOnly(sites.ToArray());
}

/// <summary>Immutable semantic subject and actual facts. This establishes no origin authenticity, authored/compiled equivalence, target readiness or serving authority.</summary>
public sealed class ReleaseSemanticVerificationResult(ReleaseSemanticStatus status, string? digest, IReadOnlyList<ReleaseSemanticEdge> edges,
    IReadOnlyList<ReleaseResourceRequirement> resources, IReadOnlyList<ReleaseSemanticPrerequisite> prerequisites,
    IReadOnlyList<ReleaseSemanticAdapterCapability> capabilities, IReadOnlyList<ReleaseSemanticDiagnostic> diagnostics)
{
    public ReleaseSemanticStatus Status { get; } = status;
    public string? Digest { get; } = digest;
    public IReadOnlyList<ReleaseSemanticEdge> Edges { get; } = Array.AsReadOnly(edges.ToArray());
    public IReadOnlyList<ReleaseResourceRequirement> Resources { get; } = Array.AsReadOnly(resources.ToArray());
    public IReadOnlyList<ReleaseSemanticPrerequisite> Prerequisites { get; } = Array.AsReadOnly(prerequisites.ToArray());
    public IReadOnlyList<ReleaseSemanticAdapterCapability> Capabilities { get; } = Array.AsReadOnly(capabilities.ToArray());
    public IReadOnlyList<ReleaseSemanticDiagnostic> Diagnostics { get; } = Array.AsReadOnly(diagnostics.ToArray());
}

/// <summary>Replacement contract: one verifier per shell. Revalidates captured transport before exact owner dispatch; never grants import or execution authority.</summary>
public interface IReleaseSemanticVerifier
{
    ValueTask<ReleaseSemanticVerificationResult> VerifyAsync(IReleaseBundleTransport bundle,
        ReleaseSemanticVerificationOptions? options = null, CancellationToken cancellationToken = default);
}
