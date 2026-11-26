using System.Text;
using System.Text.Json;
using Elsa.Workflows.Publishing.Releases.Core;
using static Elsa.Workflows.Publishing.Releases.ReleaseManifest;

namespace Elsa.Workflows.Publishing.Releases;

internal sealed class ReleaseBundleBuilder(IReleaseBundleCodec codec, ReleaseSemanticRegistry registry,
    IReleaseSemanticVerifier verifier) : IReleaseBundleBuilder
{
    public async ValueTask<ReleaseBundleBuildResult> BuildAsync(IReleaseContentReader content, IReadOnlyList<ReleaseBundleRoot> roots,
        ReleaseSemanticVerificationOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content); ArgumentNullException.ThrowIfNull(roots);
        var settings = options ?? new();
        if (settings.MaxSites <= 0 || settings.MaxDiagnostics <= 0 || settings.MaxInspectionBytes <= 0) throw new ArgumentOutOfRangeException(nameof(options));
        var bounds = ReleaseBundleCodec.Limits(settings.TransportLimits);
        cancellationToken.ThrowIfCancellationRequested();
        if (settings.ExpectedDigest is not null)
        {
            try { CheckDigest(settings.ExpectedDigest); }
            catch (ReleaseBundleFormatException)
            { return new(null, new(ReleaseSemanticStatus.Invalid, null, [], [], [], [], [new("invalid_expected_digest")])); }
        }
        var frozen = new FrozenContent(content, bounds, settings.MaxInspectionBytes, cancellationToken);
        if (roots.Count is 0 || roots.Count > bounds.MaxMembers) throw Refuse("invalid_capture_roots");
        var selections = new List<ReleaseBundleRoot>(); var selected = new HashSet<ReleaseMemberKey>();
        foreach (var root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (selections.Count >= bounds.MaxMembers || root is null || root.Member is null || root.Content is null ||
                root.Action is not (ReleaseRootAction.ActivateServing or ReleaseRootAction.MakeAvailable) ||
                !frozen.Contains(root.Member) || !frozen.Contains(root.Content) || !selected.Add(root.Member))
                throw Refuse("invalid_capture_roots");
            selections.Add(root);
        }
        // The skeleton is internal input to actual-fact inspection, never exposed as validated transport.
        using var skeleton = JsonDocument.Parse(WriteManifest(frozen, selections, null, bounds));
        var inspection = await new ReleaseSemanticVerifier(codec, registry).InspectAsync(frozen, skeleton.RootElement,
            null, settings, false, cancellationToken);
        if (inspection.Status != ReleaseSemanticStatus.Verified) return new(null, inspection);
        var manifest = WriteManifest(frozen, selections, inspection, bounds);
        var bundle = codec.Validate(manifest, frozen.Blobs, bounds);
        if (settings.ExpectedDigest is not null)
        {
            if (settings.ExpectedDigest != bundle.Digest)
                return new(null, new(ReleaseSemanticStatus.Invalid, bundle.Digest, inspection.Edges, inspection.Resources,
                    inspection.Prerequisites, inspection.Capabilities, [new("expected_digest_mismatch")]));
        }
        cancellationToken.ThrowIfCancellationRequested();
        var verified = await verifier.VerifyAsync(bundle, settings, cancellationToken);
        if (verified.Status != ReleaseSemanticStatus.Verified) return new(null, verified);
        if (verified.Digest != bundle.Digest || verified.Diagnostics.Count != 0)
            return new(null, new(ReleaseSemanticStatus.Invalid, bundle.Digest, [], [], [], [], [new("invalid_selected_verifier_result")]));
        return new(bundle, verified);
    }

    private static byte[] WriteManifest(FrozenContent content, IReadOnlyList<ReleaseBundleRoot> roots,
        ReleaseSemanticVerificationResult? facts, ReleaseBundleLimits bounds)
    {
        using var output = new ManifestOutput(bounds.MaxManifestBytes);
        using (var writer = new Utf8JsonWriter(output, new() { MaxDepth = bounds.MaxJsonDepth }))
        {
            writer.WriteStartObject(); writer.WriteString("format", "elsa.workflow-release"); writer.WriteNumber("formatVersion", 1);
            writer.WriteString("profile", Profile); writer.WriteStartArray("roots");
            foreach (var root in roots)
            {
                writer.WriteStartObject(); Key("member", root.Member); Key("content", root.Content);
                writer.WriteString("action", root.Action == ReleaseRootAction.ActivateServing ? "activate-serving" : "make-available"); writer.WriteEndObject();
            }
            writer.WriteEndArray(); writer.WriteStartArray("members");
            foreach (var member in content.Members)
            {
                writer.WriteStartObject(); Key("key", member.Key); writer.WriteString("codec", member.Descriptor.Codec);
                writer.WriteNumber("codecVersion", member.Codec.CodecVersion); writer.WritePropertyName("ownerVersion");
                if (member.OwnerVersion is not { } version) writer.WriteNullValue();
                else
                {
                    writer.WriteStartObject(); writer.WriteString("scheme", version.Scheme);
                    if (version.Scheme == "integer") writer.WriteNumber("value", version.IntegerValue!.Value);
                    else writer.WriteString("value", version.TextValue);
                    writer.WriteEndObject();
                }
                writer.WriteString("payloadDigest", member.Descriptor.PayloadDigest); writer.WriteNumber("byteLength", member.Descriptor.ByteLength); writer.WriteEndObject();
            }
            writer.WriteEndArray(); writer.WriteStartArray("edges");
            foreach (var edge in facts?.Edges ?? [])
            {
                writer.WriteStartObject(); Key("from", edge.From); Key("to", edge.To);
                writer.WriteString("relation", edge.Relation == ReleaseEdgeRelation.Dependency ? "dependency" : "association");
                writer.WriteString("occurrence", edge.Occurrence); writer.WriteEndObject();
            }
            writer.WriteEndArray(); writer.WriteStartArray("prerequisites");
            foreach (var item in facts?.Prerequisites ?? [])
            {
                writer.WriteStartObject(); writer.WriteString("key", item.Key); Key("member", item.Member); writer.WriteString("kind", item.Kind);
                writer.WriteString("identity", item.Identity); writer.WriteString("scheme", item.Scheme); writer.WriteString("constraint", item.Constraint); writer.WriteEndObject();
            }
            writer.WriteEndArray(); writer.WriteStartArray("resourceRequirements");
            foreach (var item in facts?.Resources ?? [])
            {
                writer.WriteStartObject(); writer.WriteStartObject("key"); Key("member", item.Member); writer.WriteStartArray("origin");
                foreach (var segment in item.Origin) writer.WriteStringValue(segment);
                writer.WriteEndArray(); writer.WriteString("reference", item.Reference); writer.WriteString("kind", item.Kind); writer.WriteEndObject();
                writer.WriteString("schemaVersion", item.SchemaVersion); writer.WriteEndObject();
            }
            writer.WriteEndArray(); writer.WriteEndObject(); writer.Flush();

            void Key(string name, ReleaseMemberKey key)
            {
                writer.WriteStartObject(name); writer.WriteString("kind", key.Kind); writer.WriteString("identity", key.Identity); writer.WriteEndObject();
            }
        }
        return output.ToArray();
    }

    private sealed class FrozenContent : IReleaseContentReader
    {
        private static readonly UTF8Encoding StrictUtf8 = new(false, true);
        private readonly Dictionary<ReleaseMemberKey, ReleaseContentMember> _members = [];
        public Dictionary<string, ReadOnlyMemory<byte>> Blobs { get; } = new(StringComparer.Ordinal);
        public IReadOnlyList<ReleaseContentMember> Members { get; }
        public bool Contains(ReleaseMemberKey key) => _members.ContainsKey(key);
        public FrozenContent(IReleaseContentReader source, ReleaseBundleLimits bounds, long maximumInspectionBytes, CancellationToken token)
        {
            if (source.Members.Count is 0 || source.Members.Count > bounds.MaxMembers) throw Refuse("member_limit_exceeded");
            long expanded = 0; long inspected = 0;
            foreach (var member in source.Members)
            {
                token.ThrowIfCancellationRequested();
                if (_members.Count >= bounds.MaxMembers || member is null || member.Descriptor is not { } descriptor ||
                    !Text(descriptor.Kind) || !Text(descriptor.Identity) || !Text(descriptor.Codec) || !Text(descriptor.PayloadDigest) || descriptor.CodecVersion <= 0 ||
                    descriptor.ByteLength <= 0 || descriptor.ByteLength > bounds.MaxBlobBytes)
                    throw Refuse("invalid_capture_member");
                CheckDigest(descriptor.PayloadDigest);
                if (member.OwnerVersion is { } version && (!Text(version.Scheme) || (version.Scheme switch
                    {
                        "integer" => version.IntegerValue is not > 0 || version.TextValue is not null,
                        "semver" or "opaque" => !Text(version.TextValue) || version.IntegerValue is not null,
                        _ => true
                    }))) throw Refuse("invalid_owner_version");
                if (!_members.TryAdd(member.Key, member)) throw Refuse("duplicate_member");
                if (descriptor.ByteLength > maximumInspectionBytes - inspected) throw Refuse("capture_payload_work_limit_exceeded");
                inspected += descriptor.ByteLength;
                var distinct = !Blobs.ContainsKey(descriptor.PayloadDigest);
                if (distinct && (Blobs.Count >= bounds.MaxEntries - 1 || descriptor.ByteLength > bounds.MaxExpandedBytes - expanded))
                    throw Refuse("byte_limit_exceeded");
                var payload = source.GetPayload(member.Key);
                if (payload.Length != descriptor.ByteLength) throw Refuse("blob_length_mismatch");
                var bytes = payload.ToArray();
                if (Hash(bytes) != descriptor.PayloadDigest) throw Refuse("blob_digest_mismatch");
                if (bytes[^1] is 10 or 13) throw Refuse("invalid_blob_encoding");
                CheckJson(bytes, bounds.MaxJsonDepth);
                if (distinct) { Blobs.Add(descriptor.PayloadDigest, bytes); expanded += bytes.Length; }
            }
            Members = Array.AsReadOnly(_members.Values.ToArray());

            bool Text(string? text)
            {
                if (string.IsNullOrEmpty(text) || text.Length > bounds.MaxManifestBytes) return false;
                try { return StrictUtf8.GetByteCount(text) <= bounds.MaxManifestBytes; }
                catch (EncoderFallbackException) { return false; }
            }
        }
        public ReleaseContentMember GetMember(ReleaseMemberKey key) => _members[key];
        public ReadOnlyMemory<byte> GetPayload(ReleaseMemberKey key) => Blobs[GetMember(key).Descriptor.PayloadDigest].ToArray();
    }

    private sealed class ManifestOutput(int maximum) : MemoryStream
    {
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length > maximum - Length) throw Refuse("byte_limit_exceeded");
            base.Write(buffer);
        }
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (count > maximum - Length) throw Refuse("byte_limit_exceeded");
            // MemoryStream's span overload dispatches through Stream for derived types.
            // Calling the span overload here would recurse through this array overload.
            base.Write(buffer, offset, count);
        }
    }
}
