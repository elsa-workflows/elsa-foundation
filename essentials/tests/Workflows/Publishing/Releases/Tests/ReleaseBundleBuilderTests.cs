using System.Text.Json;
using Elsa.Workflows.Publishing.Releases.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Publishing.Releases.Tests;

public sealed class ReleaseBundleBuilderTests
{
    [Fact]
    public async Task Owner_discovered_edges_resources_and_prerequisites_build_a_bundle_that_verifies_independently()
    {
        var fixture = new ReleaseSemanticFixture(); var reader = new Reader(fixture);
        fixture.Set(fixture.First, resources: [new(fixture.First, ["placement", "input", "Connection"], "payments", "connection", "1")],
            prerequisites: [new("capability-1", fixture.First, "runtime-capability", "controlled", "exact", "1")]);
        var built = await Builder(fixture).BuildAsync(reader, Roots(fixture));
        Assert.Equal(ReleaseSemanticStatus.Verified, built.Verification.Status);
        var bundle = Assert.IsAssignableFrom<IReleaseBundleTransport>(built.Bundle);
        Assert.Single(bundle.Manifest.GetProperty("edges").EnumerateArray());
        Assert.Single(bundle.Manifest.GetProperty("resourceRequirements").EnumerateArray());
        Assert.Single(bundle.Manifest.GetProperty("prerequisites").EnumerateArray());
        Assert.Equal(ReleaseSemanticStatus.Verified, (await fixture.Verifier().VerifyAsync(bundle)).Status);
        Assert.Equal(bundle.Digest, built.Verification.Digest);
        foreach (var member in reader.Members)
            Assert.Equal(fixture.Bundle.Blobs[member.Descriptor.PayloadDigest].ToArray(), bundle.GetBlobBytes(member.Descriptor.PayloadDigest));
    }

    [Fact]
    public async Task Source_changes_during_inspection_cannot_change_the_frozen_bytes_or_reader_copies()
    {
        var fixture = new ReleaseSemanticFixture(); var reader = new Reader(fixture); var original = reader.GetPayload(fixture.First).ToArray();
        var adapter = new ControlledArtifactVerifier(fixture.Adapter.SupportedCodecs, (member, payload, content, _) =>
        {
            fixture.Bundle.Blobs[member.Descriptor.PayloadDigest] = "{}"u8.ToArray();
            var copied = content.GetPayload(member.Key).ToArray(); copied[0] = 0;
            Assert.Equal(payload.ToArray(), content.GetPayload(member.Key).ToArray());
            return ValueTask.FromResult(fixture.Facts[member.Key]);
        });
        var built = await new ReleaseBundleBuilder(new ReleaseBundleCodec(), new([adapter], []), fixture.Verifier()).BuildAsync(reader, Roots(fixture));
        Assert.Equal(original, Assert.IsAssignableFrom<IReleaseBundleTransport>(built.Bundle).GetBlobBytes(reader.GetMember(fixture.First).Descriptor.PayloadDigest));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("wrong-action")]
    [InlineData("wrong-content")]
    [InlineData("unrelated")]
    [InlineData("bad-hash")]
    [InlineData("bad-version")]
    public async Task Invalid_actual_facts_or_root_authority_never_produce_a_bundle(string change)
    {
        var fixture = new ReleaseSemanticFixture(); var roots = Roots(fixture);
        switch (change)
        {
            case "none": fixture.Set(fixture.First, rootCapabilities: []); break;
            case "wrong-action": roots = [new(fixture.First, fixture.First, ReleaseRootAction.MakeAvailable)]; break;
            case "wrong-content": roots = [new(fixture.First, fixture.Last, ReleaseRootAction.ActivateServing)]; break;
            case "unrelated": fixture.Set(fixture.First, edges: []); break;
            case "bad-hash": fixture.Set(fixture.First, hash: false); break;
            case "bad-version": fixture.Set(fixture.Last, ownerVersion: new("opaque", "17", null)); break;
        }
        var built = await Builder(fixture).BuildAsync(new Reader(fixture), roots);
        Assert.Null(built.Bundle); Assert.Equal(ReleaseSemanticStatus.Invalid, built.Verification.Status);
    }

    [Fact]
    public async Task Unclaimed_occurrence_remains_unsupported_and_is_never_replaced_by_source_declared_facts()
    {
        var fixture = new ReleaseSemanticFixture();
        fixture.Set(fixture.First, sites: [new(fixture.First, ["node"], "node", null, new("controlled", "unknown", 1), JsonSerializer.SerializeToElement(new { }))]);
        var built = await Builder(fixture).BuildAsync(new Reader(fixture), Roots(fixture));
        Assert.Null(built.Bundle); Assert.Equal(ReleaseSemanticStatus.Unsupported, built.Verification.Status);
        Assert.Contains(built.Verification.Diagnostics, item => item.Code == "unsupported_occurrence_site");
    }

    [Theory]
    [InlineData("payload")]
    [InlineData("length")]
    [InlineData("member-limit")]
    [InlineData("blob-limit")]
    [InlineData("manifest-limit")]
    public async Task Bounded_capture_refuses_invalid_input_before_invoking_owner_code(string change)
    {
        var fixture = new ReleaseSemanticFixture(); var reader = new Reader(fixture); var calls = 0;
        var limits = new ReleaseBundleLimits();
        if (change == "payload") reader.Payload = key =>
        {
            var bytes = fixture.Bundle.Blobs[reader.GetMember(key).Descriptor.PayloadDigest].ToArray();
            bytes[bytes.Length - 2] ^= 1; return bytes;
        };
        if (change == "length") reader.Payload = _ => new byte[1000];
        if (change == "member-limit") limits = new() { MaxMembers = 1 };
        if (change == "blob-limit") limits = new() { MaxBlobBytes = 4 };
        if (change == "manifest-limit") limits = new() { MaxManifestBytes = 32 };
        var adapter = new ControlledArtifactVerifier(fixture.Adapter.SupportedCodecs, (_, _, _, _) => { calls++; throw new Exception("private"); });
        var builder = new ReleaseBundleBuilder(new ReleaseBundleCodec(), new([adapter], []), fixture.Verifier());
        await Assert.ThrowsAsync<ReleaseBundleFormatException>(async () => await builder.BuildAsync(reader, Roots(fixture), new() { TransportLimits = limits }));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Canonical_digest_is_independent_of_member_iteration_order_and_expected_digest_is_enforced()
    {
        var fixture = new ReleaseSemanticFixture(); var reader = new Reader(fixture);
        var first = await Builder(fixture).BuildAsync(reader, Roots(fixture));
        var reversed = new Reader(fixture, reverse: true);
        var second = await Builder(fixture).BuildAsync(reversed, Roots(fixture), new() { ExpectedDigest = first.Bundle!.Digest });
        Assert.Equal(first.Bundle!.Digest, second.Bundle!.Digest);
        var refused = await Builder(fixture).BuildAsync(reader, Roots(fixture), new() { ExpectedDigest = "sha256:" + new string('0', 64) });
        Assert.Null(refused.Bundle); Assert.Equal("expected_digest_mismatch", Assert.Single(refused.Verification.Diagnostics).Code);
    }

    [Fact]
    public async Task Cancellation_prevents_source_reads_and_owner_invocation()
    {
        var fixture = new ReleaseSemanticFixture(); var reader = new Reader(fixture);
        reader.Payload = _ => throw new Exception("must not read");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await Builder(fixture).BuildAsync(reader, Roots(fixture), cancellationToken: new(true)));
    }

    [Fact]
    public async Task Selected_verifier_replacement_can_refuse_the_discovered_bundle()
    {
        var fixture = new ReleaseSemanticFixture(); var selected = new RefusingVerifier();
        var builder = new ReleaseBundleBuilder(new ReleaseBundleCodec(), new([fixture.Adapter], []), selected);
        var built = await builder.BuildAsync(new Reader(fixture), Roots(fixture));
        Assert.Null(built.Bundle); Assert.Equal(ReleaseSemanticStatus.Incomplete, built.Verification.Status);
        Assert.Equal(1, selected.Calls);
    }

    [Theory]
    [InlineData("resource")]
    [InlineData("prerequisite")]
    public async Task Conflicting_logical_fact_keys_refuse_at_the_owner_location_before_manifest_generation(string kind)
    {
        var fixture = new ReleaseSemanticFixture();
        if (kind == "resource") fixture.Set(fixture.First, resources:
            [new(fixture.First, ["node"], "payments", "connection", "1"), new(fixture.First, ["node"], "payments", "connection", "2")]);
        else fixture.Set(fixture.First, prerequisites:
            [new("same-key", fixture.First, "runtime-capability", "one", "exact", "1"), new("same-key", fixture.First, "runtime-capability", "two", "exact", "1")]);
        var built = await Builder(fixture).BuildAsync(new Reader(fixture), Roots(fixture));
        Assert.Null(built.Bundle); Assert.Equal(ReleaseSemanticStatus.Invalid, built.Verification.Status);
        Assert.Contains(built.Verification.Diagnostics, item => item.Member == fixture.First && item.Code == "duplicate_actual_" + kind);
    }

    [Fact]
    public async Task Repeated_blob_aliases_charge_the_per_member_work_budget_before_another_payload_read()
    {
        var fixture = new ReleaseSemanticFixture(); var reader = new Reader(fixture);
        var original = reader.GetMember(fixture.First); var length = original.Descriptor.ByteLength;
        var repeated = new AliasReader(original, reader.GetPayload(fixture.First));
        var builder = Builder(fixture);
        var exception = await Assert.ThrowsAsync<ReleaseBundleFormatException>(async () =>
            await builder.BuildAsync(repeated, Roots(fixture), new() { MaxInspectionBytes = length }));
        Assert.Equal("capture_payload_work_limit_exceeded", exception.Code); Assert.Equal(1, repeated.Reads);
    }

    [Fact]
    public async Task Verifier_charges_actual_manifest_aliases_even_when_the_supplied_wrapper_hides_descriptors()
    {
        var fixture = new ReleaseSemanticFixture(); var manifestMembers = fixture.Bundle.Manifest["members"]!;
        var firstDigest = manifestMembers[0]!["payloadDigest"]!.GetValue<string>();
        var lastDigest = manifestMembers[1]!["payloadDigest"]!.GetValue<string>();
        manifestMembers[1]!["payloadDigest"] = firstDigest; fixture.Bundle.Blobs.Remove(lastDigest);
        var actual = fixture.Bundle.Validate(); var calls = 0;
        var adapter = new ControlledArtifactVerifier(fixture.Adapter.SupportedCodecs, (member, _, _, _) =>
        { calls++; return ValueTask.FromResult(fixture.Facts[member.Key]); });
        var verifier = new ReleaseSemanticVerifier(new ReleaseBundleCodec(), new([adapter], []));
        var result = await verifier.VerifyAsync(new HiddenDescriptors(actual), new() { MaxInspectionBytes = actual.Members[0].ByteLength });
        Assert.Equal(ReleaseSemanticStatus.Invalid, result.Status);
        Assert.Equal("semantic_payload_work_limit_exceeded", Assert.Single(result.Diagnostics).Code); Assert.Equal(0, calls);
    }

    [Fact]
    public void Optional_feature_registers_scoped_builder_and_rejects_ambiguous_replacement()
    {
        var services = new ServiceCollection(); var transport = new WorkflowReleaseTransportFeature(); transport.ConfigureServices(services);
        var feature = new WorkflowReleaseVerificationFeature(); feature.ConfigureServices(services); feature.PostConfigureServices(services);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var first = provider.CreateScope(); using var second = provider.CreateScope();
        Assert.NotSame(first.ServiceProvider.GetRequiredService<IReleaseBundleBuilder>(), second.ServiceProvider.GetRequiredService<IReleaseBundleBuilder>());
        services.AddScoped<IReleaseBundleBuilder, ReleaseBundleBuilder>();
        Assert.Throws<InvalidOperationException>(() => feature.PostConfigureServices(services));
    }

    private static ReleaseBundleBuilder Builder(ReleaseSemanticFixture fixture) => new(new ReleaseBundleCodec(), new([fixture.Adapter], []), fixture.Verifier());
    private static ReleaseBundleRoot[] Roots(ReleaseSemanticFixture fixture) => [new(fixture.First, fixture.First, ReleaseRootAction.ActivateServing)];
    private sealed class RefusingVerifier : IReleaseSemanticVerifier
    {
        public int Calls { get; private set; }
        public ValueTask<ReleaseSemanticVerificationResult> VerifyAsync(IReleaseBundleTransport bundle, ReleaseSemanticVerificationOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++; return ValueTask.FromResult(new ReleaseSemanticVerificationResult(ReleaseSemanticStatus.Incomplete, bundle.Digest, [], [], [], [], [new("controlled_refusal")]));
        }
    }
    private sealed class AliasReader : IReleaseContentReader
    {
        private readonly ReadOnlyMemory<byte> _payload;
        public IReadOnlyList<ReleaseContentMember> Members { get; }
        public int Reads { get; private set; }
        public AliasReader(ReleaseContentMember first, ReadOnlyMemory<byte> payload)
        {
            _payload = payload;
            Members = [first, new(new(first.Descriptor.Kind, "alias", first.Descriptor.Codec, first.Descriptor.CodecVersion,
                first.Descriptor.PayloadDigest, first.Descriptor.ByteLength), first.OwnerVersion)];
        }
        public ReleaseContentMember GetMember(ReleaseMemberKey key) => Members.Single(item => item.Key == key);
        public ReadOnlyMemory<byte> GetPayload(ReleaseMemberKey key) { Reads++; return _payload.ToArray(); }
    }
    private sealed class HiddenDescriptors(IReleaseBundleTransport inner) : IReleaseBundleTransport
    {
        public string Digest => inner.Digest;
        public JsonElement Manifest => inner.Manifest;
        public IReadOnlyList<ReleaseBundleMember> Members => [inner.Members[0]];
        public IReadOnlyList<string> AttachmentPaths => inner.AttachmentPaths;
        public byte[] GetManifestBytes() => inner.GetManifestBytes();
        public byte[] GetBlobBytes(string payloadDigest) => inner.GetBlobBytes(payloadDigest);
        public byte[] GetAttachmentBytes(string path) => inner.GetAttachmentBytes(path);
    }
    private sealed class Reader : IReleaseContentReader
    {
        public IReadOnlyList<ReleaseContentMember> Members { get; }
        public Func<ReleaseMemberKey, ReadOnlyMemory<byte>> Payload { get; set; }
        public Reader(ReleaseSemanticFixture fixture, bool reverse = false)
        {
            var captured = fixture.Bundle.Validate();
            var versions = captured.Manifest.GetProperty("members").EnumerateArray().ToDictionary(
                item => ReleaseSemanticFixture.Key(item.GetProperty("key")), item => item.GetProperty("ownerVersion"));
            var members = captured.Members.Select(item =>
            {
                var value = versions[new(item.Kind, item.Identity)];
                var version = value.GetProperty("scheme").GetString() == "integer" ? new ReleaseOwnerVersion("integer", null, value.GetProperty("value").GetInt64())
                    : new ReleaseOwnerVersion(value.GetProperty("scheme").GetString()!, value.GetProperty("value").GetString(), null);
                return new ReleaseContentMember(item, version);
            });
            Members = (reverse ? members.Reverse() : members).ToArray();
            Payload = key => fixture.Bundle.Blobs[GetMember(key).Descriptor.PayloadDigest];
        }
        public ReleaseContentMember GetMember(ReleaseMemberKey key) => Members.Single(item => item.Key == key);
        public ReadOnlyMemory<byte> GetPayload(ReleaseMemberKey key) => Payload(key);
    }
}
