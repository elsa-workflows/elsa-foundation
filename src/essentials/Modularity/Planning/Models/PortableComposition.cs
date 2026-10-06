using System.Collections.Immutable;
using System.Text.Json;

namespace Elsa.Modularity.Planning.Models;

public enum PortableInputDisposition
{
    Origin,
    Replacement
}

/// <summary>Private source context carried only by operator-owned receipts.</summary>
public sealed record PortableInputContext(string Shell, string Environment);

public sealed record PortableRequiredInput(string Kind, string Id, string Revision);

/// <summary>An additive public wrapper around an unchanged authored-v1 composition.</summary>
public sealed record PortableComposition(
    string SchemaVersion,
    string Kind,
    JsonElement Composition,
    PortableRequiredInput RequiredInput,
    PortableInputDisposition InputDisposition);

/// <summary>A private integrity receipt for the complete supplied source bundle.</summary>
public sealed record PortableInputReceipt(
    string SchemaVersion,
    string Kind,
    string InputId,
    string InputRevision,
    string PublicEnvelopeSha256,
    PortableInputContext Context,
    ImmutableArray<PortableFileDigest> Files);

/// <summary>A private integrity receipt for the exact generated candidate files.</summary>
public sealed record PortableCandidateReceipt(
    string SchemaVersion,
    string Kind,
    string CandidateId,
    string InputId,
    string InputRevision,
    string PublicEnvelopeSha256,
    PortableInputContext Context,
    PortableInputDisposition InputDisposition,
    ImmutableArray<PortableFileDigest> Files);

public sealed record PortableFileDigest(string Name, string Sha256);
