using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Elsa.Workflows.Publishing.Releases.Core;
using Elsa.Workflows.Publishing.Releases.Runtime.Core;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Releases.Runtime;

/// <summary>Scoped decode memoization, isolated by the identity of one immutable captured reader, never by digest alone.</summary>
internal sealed class RuntimeCapturedContentCache(IReleaseRuntimeArtifactCodec codec)
{
    private readonly ConditionalWeakTable<IReleaseContentReader, ReaderCache> _readers = new();
    private ReaderCache Reader(IReleaseContentReader reader) => _readers.GetValue(reader, value => new(value, codec));
    public ReleaseContentMember Member(IReleaseContentReader reader, ReleaseMemberKey key) => Reader(reader).Member(key)
        ?? throw ReleaseRuntimeArtifactCodec.Refuse("captured_runtime_member_missing");
    public ReleaseRuntimeWorkflow Workflow(IReleaseContentReader reader, ReleaseMemberKey key) => Reader(reader).Read<ReleaseRuntimeWorkflow>(key);
    public ReleaseRuntimeTemplate Template(IReleaseContentReader reader, ReleaseMemberKey key) => Reader(reader).Read<ReleaseRuntimeTemplate>(key);
    public ReleaseRuntimeModel Model(IReleaseContentReader reader, ReleaseMemberKey key) => Reader(reader).Read<ReleaseRuntimeModel>(key);
    public ReleaseWorkflowSelection Selection(IReleaseContentReader reader, ReleaseMemberKey key) => Reader(reader).Read<ReleaseWorkflowSelection>(key);
    public ReleaseActivityVersion ActivityVersion(IReleaseContentReader reader, ReleaseMemberKey key) => Reader(reader).Read<ReleaseActivityVersion>(key);
    public bool MatchesPayload(IReleaseContentReader reader, ReleaseContentMember member, ReadOnlyMemory<byte> payload)
    {
        var captured = Member(reader, member.Key);
        return captured.Codec == member.Codec && captured.OwnerVersion == member.OwnerVersion &&
            captured.Descriptor.PayloadDigest == member.Descriptor.PayloadDigest && captured.Descriptor.ByteLength == member.Descriptor.ByteLength &&
            MatchesBytes(member.Descriptor, payload);
    }
    private static bool MatchesBytes(ReleaseBundleMember descriptor, ReadOnlyMemory<byte> payload) =>
        descriptor.ByteLength == payload.Length && descriptor.PayloadDigest == "sha256:" + Convert.ToHexStringLower(SHA256.HashData(payload.Span));

    public (ReleaseMemberKey Key, WorkflowExecutableIdentity Pin) RuntimePin(IReleaseContentReader reader, string id, ReleaseRuntimeContentScope scope)
    {
        var cache = Reader(reader);
        var workflowMember = cache.Member(new(ReleaseRuntimeCodecs.WorkflowKind, id));
        var modelMember = cache.Member(new(ReleaseRuntimeCodecs.ModelKind, id));
        if (workflowMember is null && modelMember is null) throw ReleaseRuntimeArtifactCodec.Refuse("captured_runtime_pin_missing");
        if (workflowMember is not null && modelMember is not null) throw ReleaseRuntimeArtifactCodec.Refuse("captured_runtime_identity_conflict");
        var member = workflowMember ?? modelMember!;
        WorkflowExecutableIdentity pin; ReleaseRuntimeContentScope actualScope;
        if (workflowMember is not null)
        { var decoded = Workflow(reader, member.Key); pin = decoded.Executable.Identity; actualScope = decoded.Scope; }
        else
        { var decoded = Model(reader, member.Key); pin = decoded.Model.Identity; actualScope = decoded.Scope; }
        if (pin.ArtifactId != member.Key.Identity || actualScope != scope ||
            member.OwnerVersion != new ReleaseOwnerVersion(workflowMember is null ? "opaque" : "semver", pin.ArtifactVersion, null))
            throw ReleaseRuntimeArtifactCodec.Refuse("captured_runtime_identity_conflict");
        return (member.Key, pin);
    }

    private sealed class ReaderCache(IReleaseContentReader reader, IReleaseRuntimeArtifactCodec codec)
    {
        private readonly ConcurrentDictionary<ReleaseMemberKey, Lazy<ReleaseContentMember?>> _members = new();
        private readonly ConcurrentDictionary<ReleaseMemberKey, Lazy<object>> _decoded = new();
        public ReleaseContentMember? Member(ReleaseMemberKey key) => _members.GetOrAdd(key, value => new(() =>
        {
            try
            {
                var member = reader.GetMember(value);
                if (member.Key != value) throw ReleaseRuntimeArtifactCodec.Refuse("captured_runtime_identity_conflict");
                return member;
            }
            catch (KeyNotFoundException) { return null; }
        }, LazyThreadSafetyMode.ExecutionAndPublication)).Value;

        public T Read<T>(ReleaseMemberKey key) where T : class =>
            _decoded.GetOrAdd(key, value => new(() => Decode(value), LazyThreadSafetyMode.ExecutionAndPublication)).Value as T
            ?? throw ReleaseRuntimeArtifactCodec.Refuse("captured_runtime_codec_unsupported");

        private object Decode(ReleaseMemberKey key)
        {
            var member = Member(key) ?? throw ReleaseRuntimeArtifactCodec.Refuse("captured_runtime_member_missing");
            if (member.Codec.CodecVersion != ReleaseRuntimeCodecs.Version && !(key.Kind == ReleaseRuntimeCodecs.ActivityVersionKind && member.Codec.CodecVersion == 2))
                throw ReleaseRuntimeArtifactCodec.Refuse("captured_runtime_codec_unsupported");
            var payload = reader.GetPayload(key);
            if (!MatchesBytes(member.Descriptor, payload)) throw ReleaseRuntimeArtifactCodec.Refuse("captured_runtime_payload_conflict");
            return (key.Kind, member.Codec.Codec) switch
            {
                (ReleaseRuntimeCodecs.WorkflowKind, ReleaseRuntimeCodecs.Workflow) => codec.DecodeWorkflow(payload),
                (ReleaseRuntimeCodecs.TemplateKind, ReleaseRuntimeCodecs.Template) => codec.DecodeTemplate(payload),
                (ReleaseRuntimeCodecs.ModelKind, ReleaseRuntimeCodecs.Model) => codec.DecodeModel(payload),
                (ReleaseRuntimeCodecs.SelectionKind, ReleaseRuntimeCodecs.Selection) => codec.DecodeWorkflowSelection(payload),
                (ReleaseRuntimeCodecs.ActivityVersionKind, ReleaseRuntimeCodecs.ActivityVersion) => ActivityVersion(),
                _ => throw ReleaseRuntimeArtifactCodec.Refuse("captured_runtime_codec_unsupported")
            };
            ReleaseActivityVersion ActivityVersion()
            {
                var result = codec.DecodeActivityVersion(payload);
                if ((result.Publication is null ? 1 : 2) != member.Codec.CodecVersion) throw ReleaseRuntimeArtifactCodec.Refuse("captured_runtime_codec_profile_conflict");
                return result;
            }
        }
    }
}
