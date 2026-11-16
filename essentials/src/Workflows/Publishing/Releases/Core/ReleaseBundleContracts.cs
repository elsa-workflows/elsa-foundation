using System.Text.Json;

namespace Elsa.Workflows.Publishing.Releases.Core;

/// <summary>Bounds parsing and captured transport bytes; does not authorize owner import or execution.</summary>
public sealed class ReleaseBundleLimits
{
    public int MaxManifestBytes { get; init; } = 1024 * 1024;
    public int MaxBlobBytes { get; init; } = 32 * 1024 * 1024;
    public long MaxExpandedBytes { get; init; } = 256 * 1024 * 1024;
    public int MaxArchiveBytes { get; init; } = 256 * 1024 * 1024;
    public int MaxEntries { get; init; } = 10_000;
    public int MaxMembers { get; init; } = 10_000;
    public int MaxEdges { get; init; } = 100_000;
    public int MaxJsonDepth { get; init; } = 128;
    public int MaxDependencyDepth { get; init; } = 128;
}

/// <summary>Immutable transport descriptor. The owner's codec and declared identity remain semantically unverified.</summary>
public sealed class ReleaseBundleMember(string kind, string identity, string codec, int codecVersion, string payloadDigest, long byteLength)
{
    public string Kind { get; } = kind;
    public string Identity { get; } = identity;
    public string Codec { get; } = codec;
    public int CodecVersion { get; } = codecVersion;
    public string PayloadDigest { get; } = payloadDigest;
    public long ByteLength { get; } = byteLength;
}

/// <summary>A captured transport with verified bytes and declared graph, not an owner-verified or activatable release.</summary>
public interface IReleaseBundleTransport
{
    string Digest { get; }
    JsonElement Manifest { get; }
    IReadOnlyList<ReleaseBundleMember> Members { get; }
    IReadOnlyList<string> AttachmentPaths { get; }
    /// <summary>Returns a new copy of the original manifest bytes.</summary>
    byte[] GetManifestBytes();
    /// <summary>Returns a new copy of the original blob bytes named by a prefixed digest.</summary>
    byte[] GetBlobBytes(string payloadDigest);
    /// <summary>Returns a new copy of an attachment, which is outside the release digest.</summary>
    byte[] GetAttachmentBytes(string path);
}

/// <summary>Replacement contract: one transport codec per shell. Owner codec recognition, hashes and actual dependency discovery are separate gates.</summary>
public interface IReleaseBundleCodec
{
    IReleaseBundleTransport Validate(ReadOnlyMemory<byte> manifestUtf8, IReadOnlyDictionary<string, ReadOnlyMemory<byte>> blobs, ReleaseBundleLimits? limits = null);
    /// <summary>Validates captured expanded parts directly; attachments are preserved outside the release digest.</summary>
    IReleaseBundleTransport ValidateParts(ReadOnlyMemory<byte> manifestUtf8, IReadOnlyDictionary<string, ReadOnlyMemory<byte>> blobs,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> attachments, ReleaseBundleLimits? limits);
    ValueTask<IReleaseBundleTransport> ReadZipAsync(Stream input, ReleaseBundleLimits? limits = null, CancellationToken cancellationToken = default);
    ValueTask<IReleaseBundleTransport> ReadDirectoryAsync(string directory, ReleaseBundleLimits? limits = null, CancellationToken cancellationToken = default);
    ValueTask WriteZipAsync(IReleaseBundleTransport bundle, Stream output, ReleaseBundleLimits? limits = null, CancellationToken cancellationToken = default);
}

/// <summary>A stable value-free structural refusal; it never contains blob values or raw paths.</summary>
public sealed class ReleaseBundleFormatException(string code) : Exception($"Release bundle transport was refused ({code}).")
{
    public string Code { get; } = code;
}
