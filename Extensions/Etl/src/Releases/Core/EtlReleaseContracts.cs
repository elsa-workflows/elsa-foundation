using Elsa.Etl.Core;
using Elsa.Workflows.Publishing.Releases.Core;

namespace Elsa.Etl.Releases.Core;

/// <summary>Original source content address, never target placement or authenticated publisher evidence.</summary>
public sealed class EtlReleaseSourceScope(string authority, EtlScope scope)
{
    public string Authority { get; } = authority;
    public EtlScope Scope { get; } = scope;
}

/// <summary>New release wire envelope containing an unchanged owner artifact and its original source scope.</summary>
public sealed class EtlReleaseArtifact(EtlReleaseSourceScope source, EtlArtifact artifact)
{
    public EtlReleaseSourceScope Source { get; } = source;
    public EtlArtifact Artifact { get; } = artifact;
}

/// <summary>Independent copied owner-codec bytes. Integer ETL versions remain distinct from workflow SemVer.</summary>
public sealed class EtlEncodedReleaseArtifact(ReleaseBundleMember descriptor, ReleaseOwnerVersion ownerVersion, ReadOnlyMemory<byte> payload)
{
    private readonly byte[] _payload = payload.ToArray();
    public ReleaseBundleMember Descriptor { get; } = descriptor;
    public ReleaseOwnerVersion OwnerVersion { get; } = ownerVersion;
    public byte[] GetPayloadBytes() => (byte[])_payload.Clone();
}

/// <summary>Replacement contract: one bounded owner codec; no compiler, source/target lookup or runtime admission.</summary>
public interface IEtlReleaseArtifactCodec
{
    EtlEncodedReleaseArtifact Encode(EtlReleaseArtifact artifact, ReleaseBundleLimits? limits = null);
    EtlReleaseArtifact Decode(ReadOnlyMemory<byte> payload, ReleaseBundleLimits? limits = null);
}

public static class EtlReleaseCodecs
{
    public const string MemberKind = "etl-artifact";
    public const string Artifact = "elsa.etl-artifact";
}

/// <summary>A stable value-free owner-codec refusal. Unsupported profiles remain distinguishable from invalid content.</summary>
public sealed class EtlReleaseArtifactException(string code, bool isUnsupported = false)
    : Exception("The ETL release artifact was refused (" + code + ").")
{
    public string Code { get; } = code;
    public bool IsUnsupported { get; } = isUnsupported;
}
