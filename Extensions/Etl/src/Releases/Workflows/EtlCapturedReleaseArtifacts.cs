using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Releases.Core;
using Elsa.Workflows.Publishing.Releases.Core;
using Elsa.Workflows.Publishing.Releases.Runtime.Core;

namespace Elsa.Etl.Releases.Workflows;

/// <summary>Per-captured-reader memoization; neither an external catalog nor a target/source fallback.</summary>
internal sealed class EtlCapturedReleaseArtifacts(IEtlReleaseArtifactCodec codec)
{
    private readonly ConditionalWeakTable<IReleaseContentReader, ReaderCache> _readers = new();
    private ReaderCache Cache(IReleaseContentReader content) => _readers.GetValue(content, value => new(value, codec));

    public EtlReleaseArtifact Read(IReleaseContentReader content, string artifactId, ReleaseRuntimeContentScope scope)
    {
        var result = Cache(content).Read(artifactId) ?? throw Invalid("run_etl_captured_artifact_missing");
        if (result.Source.Authority != scope.Authority || result.Source.Scope.TenantId != scope.TenantId ||
            result.Source.Scope.PersistencePartition != scope.Partition)
            throw Invalid("run_etl_source_scope_mismatch");
        return result;
    }

    public EtlReleaseArtifact Select(IReleaseContentReader content, EtlPublishedSelection selection, ReleaseRuntimeContentScope scope)
    {
        // The literal commits to the full hash. Only the two supported framed owner IDs are candidates.
        EtlReleaseArtifact? selected = null;
        foreach (var format in new[] { EtlArtifact.CurrentFormatVersion, EtlArtifactV2Json.FormatVersion })
        {
            var id = EtlArtifactJson.CreateArtifactId(format, selection.DefinitionId, selection.DefinitionVersion, selection.ContentHash);
            var candidate = Cache(content).Read(id);
            if (candidate is null) continue;
            if (selected is not null) throw Invalid("run_etl_captured_selection_ambiguous");
            selected = Read(content, id, scope);
        }
        return selected ?? throw Invalid("run_etl_captured_artifact_missing");
    }

    private static EtlReleaseArtifactException Invalid(string code) => new(code);

    private sealed class ReaderCache(IReleaseContentReader content, IEtlReleaseArtifactCodec codec)
    {
        private readonly ConcurrentDictionary<string, Lazy<EtlReleaseArtifact?>> _artifacts = new(StringComparer.Ordinal);
        public EtlReleaseArtifact? Read(string id) => _artifacts.GetOrAdd(id,
            value => new(() => Decode(value), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

        private EtlReleaseArtifact? Decode(string id)
        {
            var key = new ReleaseMemberKey(EtlReleaseCodecs.MemberKind, id);
            ReleaseContentMember member;
            try { member = content.GetMember(key); }
            catch (KeyNotFoundException) { return null; }
            if (member.Key != key) throw Invalid("run_etl_captured_identity_mismatch");
            if (member.Codec.Kind != EtlReleaseCodecs.MemberKind || member.Codec.Codec != EtlReleaseCodecs.Artifact ||
                member.Codec.CodecVersion is not (EtlArtifact.CurrentFormatVersion or EtlArtifactV2Json.FormatVersion))
                throw new EtlReleaseArtifactException("run_etl_captured_codec_unsupported", true);
            if (member.Descriptor.ByteLength is <= 0 or > EtlArtifact.MaximumByteLength + 8192)
                throw Invalid("run_etl_captured_payload_too_large");
            var payload = content.GetPayload(key);
            if (payload.Length != member.Descriptor.ByteLength ||
                member.Descriptor.PayloadDigest != "sha256:" + Convert.ToHexStringLower(SHA256.HashData(payload.Span)))
                throw Invalid("run_etl_captured_payload_mismatch");
            var release = codec.Decode(payload);
            if (release.Artifact.ArtifactId != id || release.Artifact.FormatVersion != member.Codec.CodecVersion ||
                member.OwnerVersion != new ReleaseOwnerVersion("integer", null, release.Artifact.DefinitionVersion))
                throw Invalid("run_etl_captured_identity_mismatch");
            return release;
        }
    }
}
