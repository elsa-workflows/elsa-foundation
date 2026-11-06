using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Workflows.Publishing.Releases.Core;
using Xunit;

namespace Elsa.Workflows.Publishing.Releases.Tests;

public sealed class ReleaseSemanticVerifierTests
{
    [Fact]
    public async Task Controlled_complete_facts_bind_to_digest_and_capabilities_without_owner_integration_claims()
    {
        var fixture = new ReleaseSemanticFixture();
        var transport = fixture.Bundle.Validate();
        var result = await fixture.Verifier().VerifyAsync(transport, new() { ExpectedDigest = transport.Digest });
        Assert.Equal(ReleaseSemanticStatus.Verified, result.Status);
        Assert.Equal(transport.Digest, result.Digest);
        Assert.Single(result.Edges); Assert.Empty(result.Diagnostics);
        Assert.Equal("controlled-generic-artifact", Assert.Single(result.Capabilities).Identity.Name);
        Assert.Equal(2, result.Capabilities[0].Codecs.Count);
    }

    [Theory]
    [InlineData("kind")]
    [InlineData("codec")]
    [InlineData("version")]
    public async Task Unknown_exact_codec_tuple_is_unsupported_without_fallback(string part)
    {
        var fixture = new ReleaseSemanticFixture();
        var member = fixture.Bundle.Manifest["members"]![0]!;
        if (part == "kind")
        {
            var original = member["key"]!["kind"]!.GetValue<string>();
            // Keep the declared graph consistent while changing the exact dispatch kind.
            var text = fixture.Bundle.Manifest.ToJsonString().Replace(original, "unknown-kind", StringComparison.Ordinal);
            var replacement = JsonNode.Parse(text)!.AsObject();
            fixture.Bundle.Manifest.Clear(); foreach (var pair in replacement.ToArray()) { replacement.Remove(pair.Key); fixture.Bundle.Manifest.Add(pair.Key, pair.Value); }
        }
        else member[part == "codec" ? "codec" : "codecVersion"] = part == "codec" ? JsonValue.Create("unknown-codec") : JsonValue.Create(2);
        var result = await fixture.Verifier().VerifyAsync(fixture.Bundle.Validate());
        Assert.Equal(ReleaseSemanticStatus.Unsupported, result.Status);
        Assert.Contains(result.Diagnostics, x => x.Code == "unsupported_artifact_codec");
    }

    [Fact]
    public async Task Expected_digest_and_forged_transport_refuse_before_adapter_invocation()
    {
        var fixture = new ReleaseSemanticFixture(); var called = 0;
        var adapter = new ControlledArtifactVerifier(fixture.Adapter.SupportedCodecs, (_, _, _, _) => { called++; throw new IOException("private-token"); });
        var verifier = new ReleaseSemanticVerifier(new ReleaseBundleCodec(), new([adapter], []));
        var captured = fixture.Bundle.Validate();
        var mismatch = await verifier.VerifyAsync(captured, new() { ExpectedDigest = "sha256:" + new string('0', 64) });
        Assert.Equal("expected_digest_mismatch", Assert.Single(mismatch.Diagnostics).Code);
        var invalidExpected = await verifier.VerifyAsync(captured, new() { ExpectedDigest = "private-token" });
        Assert.Equal("invalid_expected_digest", Assert.Single(invalidExpected.Diagnostics).Code);
        var forged = await verifier.VerifyAsync(new ForgedTransport(captured));
        Assert.Equal("transport_digest_mismatch", Assert.Single(forged.Diagnostics).Code);
        Assert.Equal(0, called);
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("hash")]
    [InlineData("version-value")]
    [InlineData("version-tag")]
    [InlineData("invalid-status")]
    [InlineData("null-answer")]
    [InlineData("cross-member")]
    [InlineData("bad-diagnostic")]
    [InlineData("invalid-unicode")]
    public async Task Invalid_owner_facts_fail_closed(string change)
    {
        var fixture = new ReleaseSemanticFixture();
        switch (change)
        {
            case "identity": fixture.Set(fixture.First, identity: fixture.Last); break;
            case "hash": fixture.Set(fixture.First, hash: false); break;
            case "version-value": fixture.Set(fixture.Last, ownerVersion: new("integer", null, 18)); break;
            case "version-tag": fixture.Set(fixture.Last, ownerVersion: new("opaque", "17", null)); break;
            case "invalid-status": fixture.Set(fixture.First, status: (ReleaseSemanticStatus)999); break;
            case "null-answer": fixture.Facts[fixture.First] = null!; break;
            case "cross-member": fixture.Set(fixture.First, edges: [new(fixture.Last, fixture.First, ReleaseEdgeRelation.Dependency, "")]); break;
            case "bad-diagnostic": fixture.Set(fixture.First, status: ReleaseSemanticStatus.Invalid, diagnostics: [new("https://private-token.invalid")]); break;
            case "invalid-unicode": fixture.Set(fixture.First, edges: [fixture.Facts[fixture.First].Edges[0] with { Occurrence = "\uD800" }]); break;
        }
        var result = await fixture.Verifier().VerifyAsync(fixture.Bundle.Validate());
        Assert.Equal(ReleaseSemanticStatus.Invalid, result.Status);
        Assert.DoesNotContain("private-token", JsonSerializer.Serialize(result.Diagnostics), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ReleaseSemanticStatus.Incomplete, ReleaseSemanticStatus.Verified, ReleaseSemanticStatus.Incomplete)]
    [InlineData(ReleaseSemanticStatus.Incomplete, ReleaseSemanticStatus.Unsupported, ReleaseSemanticStatus.Unsupported)]
    [InlineData(ReleaseSemanticStatus.Unsupported, ReleaseSemanticStatus.Invalid, ReleaseSemanticStatus.Invalid)]
    [InlineData(ReleaseSemanticStatus.Invalid, ReleaseSemanticStatus.Incomplete, ReleaseSemanticStatus.Invalid)]
    public async Task Status_precedence_is_explicit_and_partial_facts_never_verify(ReleaseSemanticStatus first, ReleaseSemanticStatus last, ReleaseSemanticStatus expected)
    {
        var fixture = new ReleaseSemanticFixture(); fixture.Set(fixture.First, status: first); fixture.Set(fixture.Last, status: last);
        var result = await fixture.Verifier().VerifyAsync(fixture.Bundle.Validate());
        Assert.Equal(expected, result.Status);
        Assert.NotEqual(ReleaseSemanticStatus.Verified, default(ReleaseSemanticStatus));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("invented")]
    [InlineData("changed-occurrence")]
    [InlineData("changed-relation")]
    [InlineData("missing-member")]
    public async Task Actual_edges_and_declared_edges_must_agree_in_both_directions(string change)
    {
        var fixture = new ReleaseSemanticFixture(); var edge = fixture.Facts[fixture.First].Edges[0];
        switch (change)
        {
            case "missing": fixture.Set(fixture.First, edges: []); break;
            case "invented": fixture.Set(fixture.Last, edges: [new(fixture.Last, fixture.First, ReleaseEdgeRelation.Association, "invented")]); break;
            case "changed-occurrence": fixture.Set(fixture.First, edges: [edge with { Occurrence = "different-node" }]); break;
            case "changed-relation": fixture.Set(fixture.First, edges: [edge with { Relation = ReleaseEdgeRelation.Association }]); break;
            case "missing-member": fixture.Set(fixture.First, edges: [edge with { To = new("unlisted", "pin") }]); break;
        }
        var result = await fixture.Verifier().VerifyAsync(fixture.Bundle.Validate());
        Assert.Equal(ReleaseSemanticStatus.Invalid, result.Status);
        Assert.Contains(result.Diagnostics, x => x.Code is "undeclared_edge" or "declared_edge_not_observed" or "actual_missing_member");
        if (change == "missing") Assert.Contains(result.Diagnostics, x => x.Code == "actual_unrelated_member");
    }

    [Theory]
    [InlineData("resource", "missing")]
    [InlineData("resource", "invented")]
    [InlineData("resource", "changed")]
    [InlineData("resource", "scope")]
    [InlineData("prerequisite", "missing")]
    [InlineData("prerequisite", "invented")]
    [InlineData("prerequisite", "changed")]
    public async Task Actual_requirements_are_compared_with_exact_values_and_scope(string kind, string change)
    {
        var fixture = new ReleaseSemanticFixture();
        if (kind == "resource")
        {
            fixture.Bundle.Manifest["resourceRequirements"] = JsonSerializer.SerializeToNode(new[] { new { key = new { member = fixture.First, origin = new[] { "outer", "inner" }, reference = "orders", kind = "connection" }, schemaVersion = "1" } }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var requirement = new ReleaseResourceRequirement(fixture.First, ["outer", "inner"], "orders", "connection", "1");
            fixture.Set(fixture.First, resources: change switch
            {
                "missing" => [],
                "invented" => [requirement, new(fixture.First, [], "other", "connection", "1")],
                "changed" => [new(fixture.First, requirement.Origin, "orders", "connection", "2")],
                _ => [new(fixture.First, ["inner", "outer"], "orders", "connection", "1")]
            });
        }
        else
        {
            fixture.Bundle.Manifest["prerequisites"] = JsonSerializer.SerializeToNode(new[] { new { key = "consumer", member = fixture.First, kind = "consumer", identity = "fixture", scheme = "exact", constraint = "1" } }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var requirement = new ReleaseSemanticPrerequisite("consumer", fixture.First, "consumer", "fixture", "exact", "1");
            fixture.Set(fixture.First, prerequisites: change switch
            {
                "missing" => [],
                "invented" => [requirement, requirement with { Key = "other", Identity = "other" }],
                _ => [requirement with { Constraint = "2" }]
            });
        }
        var result = await fixture.Verifier().VerifyAsync(fixture.Bundle.Validate());
        Assert.Equal(ReleaseSemanticStatus.Invalid, result.Status);
        Assert.Contains(result.Diagnostics, x => x.Code.Contains(kind, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(ReleaseEdgeRelation.Dependency, ReleaseSemanticStatus.Invalid)]
    [InlineData(ReleaseEdgeRelation.Association, ReleaseSemanticStatus.Verified)]
    public async Task Only_actual_dependency_cycles_refuse_declared_metadata_cycles(ReleaseEdgeRelation relation, ReleaseSemanticStatus expected)
    {
        var fixture = new ReleaseSemanticFixture();
        fixture.Bundle.Manifest["edges"]!.AsArray().Add(JsonSerializer.SerializeToNode(new { from = fixture.Last, to = fixture.First, relation = "association", occurrence = "return" }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        fixture.Set(fixture.Last, edges: [new(fixture.Last, fixture.First, relation, "return")]);
        var result = await fixture.Verifier().VerifyAsync(fixture.Bundle.Validate());
        Assert.Equal(expected, result.Status);
        if (relation == ReleaseEdgeRelation.Dependency) Assert.Contains(result.Diagnostics, x => x.Code == "actual_dependency_cycle");
    }

    [Fact]
    public async Task Actual_dependency_depth_uses_owner_edges_even_when_declared_relations_are_associations()
    {
        var fixture = new ReleaseSemanticFixture(3);
        foreach (var edge in fixture.Bundle.Manifest["edges"]!.AsArray()) edge!["relation"] = "association";
        var result = await fixture.Verifier().VerifyAsync(fixture.Bundle.Validate(), new() { TransportLimits = new() { MaxDependencyDepth = 1 } });
        Assert.Equal(ReleaseSemanticStatus.Invalid, result.Status);
        Assert.Contains(result.Diagnostics, x => x.Code == "actual_dependency_depth_exceeded");
    }

    [Fact]
    public async Task Occurrence_inspection_requires_explicit_complete_claim_and_combines_its_actual_facts()
    {
        var fixture = new ReleaseSemanticFixture(); var key = new ReleaseSiteKey("placed-node", "fixture-leaf", 1);
        using var document = JsonDocument.Parse("{}");
        fixture.Set(fixture.First, edges: [], sites: [new(fixture.First, ["outer"], "node", "input", key, document.RootElement)]);
        var unknown = await fixture.Verifier().VerifyAsync(fixture.Bundle.Validate());
        Assert.Equal(ReleaseSemanticStatus.Unsupported, unknown.Status);
        var inspector = new ControlledOccurrenceInspector([key], (_, _, _) => ValueTask.FromResult(new ReleaseOccurrenceFacts(ReleaseSemanticStatus.Verified,
            [new(fixture.First, fixture.Last, ReleaseEdgeRelation.Dependency, fixture.Bundle.Manifest["edges"]![0]!["occurrence"]!.GetValue<string>())])));
        Assert.Equal(ReleaseSemanticStatus.Verified, (await fixture.Verifier(inspector).VerifyAsync(fixture.Bundle.Validate())).Status);
        var incomplete = new ControlledOccurrenceInspector([key], (_, _, _) => ValueTask.FromResult(new ReleaseOccurrenceFacts(ReleaseSemanticStatus.Incomplete)));
        Assert.Equal(ReleaseSemanticStatus.Incomplete, (await fixture.Verifier(incomplete).VerifyAsync(fixture.Bundle.Validate())).Status);
    }

    [Fact]
    public async Task Adapter_failures_are_sanitized_and_cancellation_propagates()
    {
        var fixture = new ReleaseSemanticFixture();
        var broken = new ControlledArtifactVerifier(fixture.Adapter.SupportedCodecs, (_, _, _, _) => throw new IOException("private-token"));
        var verifier = new ReleaseSemanticVerifier(new ReleaseBundleCodec(), new([broken], []));
        var result = await verifier.VerifyAsync(fixture.Bundle.Validate());
        Assert.Equal(ReleaseSemanticStatus.Incomplete, result.Status);
        Assert.DoesNotContain("private-token", JsonSerializer.Serialize(result.Diagnostics), StringComparison.Ordinal);
        using var cancellation = new CancellationTokenSource();
        var canceled = new ControlledArtifactVerifier(fixture.Adapter.SupportedCodecs, (_, _, _, token) => { cancellation.Cancel(); throw new OperationCanceledException(token); });
        verifier = new(new ReleaseBundleCodec(), new([canceled], []));
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => verifier.VerifyAsync(fixture.Bundle.Validate(), cancellationToken: cancellation.Token).AsTask());
        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task Indexed_reader_and_fact_models_snapshot_inputs_and_cannot_mutate_captured_content()
    {
        var fixture = new ReleaseSemanticFixture(1); var original = fixture.Bundle.Validate();
        var adapter = new ControlledArtifactVerifier(fixture.Adapter.SupportedCodecs, (member, payload, content, _) =>
        {
            Assert.Same(member, content.GetMember(member.Key));
            Assert.Equal(member.OwnerVersion, fixture.Facts[member.Key].OwnerVersion);
            Assert.True(MemoryMarshal.TryGetArray(payload, out var array)); array.Array![array.Offset] = 0;
            var again = content.GetPayload(member.Key); Assert.NotEqual((byte)0, again.Span[0]);
            Assert.Throws<KeyNotFoundException>(() => content.GetMember(new("absent", "private-token")));
            return ValueTask.FromResult(fixture.Facts[member.Key]);
        });
        var result = await new ReleaseSemanticVerifier(new ReleaseBundleCodec(), new([adapter], [])).VerifyAsync(original);
        Assert.Equal(ReleaseSemanticStatus.Verified, result.Status);
        Assert.Equal(fixture.Bundle.Bytes, original.GetManifestBytes());
        var origin = new[] { "first" }; var requirement = new ReleaseResourceRequirement(fixture.First, origin, "r", "k", "1"); origin[0] = "changed";
        Assert.Equal("first", requirement.Origin[0]);
        var edges = new ReleaseSemanticEdge[] { new(fixture.First, fixture.First, ReleaseEdgeRelation.Association, "") };
        var facts = new ReleaseSemanticFacts(ReleaseSemanticStatus.Verified, fixture.First, null, true, edges); edges[0] = edges[0] with { Occurrence = "changed" };
        Assert.Equal("", facts.Edges[0].Occurrence);
    }

    [Fact]
    public async Task Actual_fact_bound_fails_closed()
    {
        var fixture = new ReleaseSemanticFixture();
        fixture.Set(fixture.First, edges: [fixture.Facts[fixture.First].Edges[0], fixture.Facts[fixture.First].Edges[0]]);
        var result = await fixture.Verifier().VerifyAsync(fixture.Bundle.Validate(), new() { TransportLimits = new() { MaxEdges = 1 } });
        Assert.Equal(ReleaseSemanticStatus.Invalid, result.Status);
        Assert.Contains(result.Diagnostics, x => x.Code == "semantic_fact_limit_exceeded");
    }

    [Theory]
    [InlineData("null")]
    [InlineData("invalid-status")]
    [InlineData("exception")]
    [InlineData("cancellation")]
    [InlineData("duplicate-site")]
    [InlineData("site-limit")]
    [InlineData("diagnostic-limit")]
    public async Task Occurrence_answers_bounds_and_cancellation_fail_closed(string change)
    {
        var fixture = new ReleaseSemanticFixture(1); var key = new ReleaseSiteKey("fixture-site", "leaf", 1);
        using var document = JsonDocument.Parse("{}");
        var first = new ReleaseSemanticSite(fixture.First, ["root"], "node", null, key, document.RootElement);
        var second = new ReleaseSemanticSite(fixture.First, ["root"], "node-2", null, key, document.RootElement);
        fixture.Set(fixture.First, sites: change is "duplicate-site" or "site-limit" ? [first, change == "duplicate-site" ? first : second] : [first]);
        using var cancellation = new CancellationTokenSource();
        var inspector = new ControlledOccurrenceInspector([key], (_, _, token) => change switch
        {
            "null" => ValueTask.FromResult<ReleaseOccurrenceFacts>(null!),
            "invalid-status" => ValueTask.FromResult(new ReleaseOccurrenceFacts((ReleaseSemanticStatus)99)),
            "exception" => throw new IOException("private-token"),
            "cancellation" => throw new OperationCanceledException(token),
            "diagnostic-limit" => ValueTask.FromResult(new ReleaseOccurrenceFacts(ReleaseSemanticStatus.Incomplete, diagnostics: [new("first"), new("second")])),
            _ => ValueTask.FromResult(new ReleaseOccurrenceFacts(ReleaseSemanticStatus.Verified))
        });
        if (change == "cancellation")
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Verifier(inspector).VerifyAsync(fixture.Bundle.Validate(), cancellationToken: cancellation.Token).AsTask());
            return;
        }
        var result = await fixture.Verifier(inspector).VerifyAsync(fixture.Bundle.Validate(), new() { MaxSites = 1, MaxDiagnostics = 1 });
        Assert.Equal(change == "exception" ? ReleaseSemanticStatus.Incomplete : ReleaseSemanticStatus.Invalid, result.Status);
        Assert.Single(result.Diagnostics);
        Assert.DoesNotContain("private-token", JsonSerializer.Serialize(result.Diagnostics), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Exact_unicode_opaque_occurrence_and_resource_scope_are_preserved()
    {
        var fixture = new ReleaseSemanticFixture();
        var origin = new[] { "注文/先", "内:部" }; const string occurrence = "節:node/α"; const string input = "入力:β/値";
        fixture.Bundle.Manifest["edges"]![0]!["occurrence"] = occurrence;
        fixture.Bundle.Manifest["resourceRequirements"] = JsonSerializer.SerializeToNode(new[] { new { key = new { member = fixture.First, origin, reference = "orders", kind = "connection" }, schemaVersion = "1" } }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var siteKey = new ReleaseSiteKey("fixture-site", "leaf", 1); using var document = JsonDocument.Parse("{}");
        fixture.Set(fixture.First, edges: [], sites: [new(fixture.First, origin, occurrence, input, siteKey, document.RootElement)]);
        var inspector = new ControlledOccurrenceInspector([siteKey], (site, _, _) =>
        {
            Assert.Equal(occurrence, site.Node); Assert.Equal(input, site.Input); Assert.Equal(origin, site.Origin);
            return ValueTask.FromResult(new ReleaseOccurrenceFacts(ReleaseSemanticStatus.Verified,
                [new(fixture.First, fixture.Last, ReleaseEdgeRelation.Dependency, occurrence)], [new(fixture.First, origin, "orders", "connection", "1")]));
        });
        var result = await fixture.Verifier(inspector).VerifyAsync(fixture.Bundle.Validate());
        Assert.Equal(ReleaseSemanticStatus.Verified, result.Status);
        Assert.Equal(origin, Assert.Single(result.Resources).Origin);
        Assert.Equal(occurrence, Assert.Single(result.Edges).Occurrence);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Occurrence_diagnostics_preserve_legal_ids_and_refuse_provably_wrong_scope(bool wrongScope)
    {
        var fixture = new ReleaseSemanticFixture(1); var origin = new[] { "注文/先" }; const string input = "入力:β/値";
        var siteKey = new ReleaseSiteKey("fixture-site", "leaf", 1); using var document = JsonDocument.Parse("{}");
        fixture.Set(fixture.First, sites: [new(fixture.First, origin, "node:/α", input, siteKey, document.RootElement)]);
        var inspector = new ControlledOccurrenceInspector([siteKey], (_, _, _) => ValueTask.FromResult(new ReleaseOccurrenceFacts(ReleaseSemanticStatus.Incomplete,
            diagnostics: [new("bounded_inspection_incomplete", fixture.First, wrongScope ? ["other"] : origin, input)])));
        var result = await fixture.Verifier(inspector).VerifyAsync(fixture.Bundle.Validate());
        Assert.Equal(wrongScope ? ReleaseSemanticStatus.Invalid : ReleaseSemanticStatus.Incomplete, result.Status);
        if (!wrongScope) { Assert.Equal(origin, Assert.Single(result.Diagnostics).Origin); Assert.Equal(input, result.Diagnostics[0].Input); }
    }

    private sealed class ForgedTransport(IReleaseBundleTransport source) : IReleaseBundleTransport
    {
        public string Digest => "sha256:" + new string('0', 64);
        public JsonElement Manifest => source.Manifest;
        public IReadOnlyList<ReleaseBundleMember> Members => source.Members;
        public IReadOnlyList<string> AttachmentPaths => source.AttachmentPaths;
        public byte[] GetManifestBytes() => source.GetManifestBytes();
        public byte[] GetBlobBytes(string payloadDigest) => source.GetBlobBytes(payloadDigest);
        public byte[] GetAttachmentBytes(string path) => source.GetAttachmentBytes(path);
    }
}
