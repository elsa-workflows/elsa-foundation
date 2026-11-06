using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Workflows.Publishing.Releases.Core;
using Xunit;

namespace Elsa.Workflows.Publishing.Releases.Tests;

public sealed class ReleaseManifestTests
{
    [Fact]
    public void Canonical_recipe_matches_pinned_unicode_null_and_array_vector()
    {
        using var document = JsonDocument.Parse("{\"β\":\"é\",\"null\":null,\"items\":[1,true,\"<\"]}");
        const string canonical = "{\"items\":[1,true,\"\\u003C\"],\"null\":null,\"\\u03B2\":\"\\u00E9\"}";
        Assert.Equal(canonical, ReleaseManifest.CanonicalText(document.RootElement));
        Assert.Equal("sha256:ea655576dff3d172bd93d6950f51d723ffd5a4bdebb8d008824d5a1e7e018db4", ReleaseManifest.HashManifest(document.RootElement));
    }

    [Fact]
    public void Manifest_set_order_and_formatting_do_not_change_digest_but_ordered_origin_does()
    {
        var fixture = new ReleaseBundleFixture(3);
        var before = fixture.Validate();
        var members = fixture.Manifest["members"]!.AsArray();
        var first = members[0]!.DeepClone(); members.RemoveAt(0); members.Add(first);
        var edges = fixture.Manifest["edges"]!.AsArray();
        first = edges[0]!.DeepClone(); edges.RemoveAt(0); edges.Add(first);
        var after = fixture.Validate();
        Assert.Equal(before.Digest, after.Digest);
        var formatted = Encoding.UTF8.GetBytes(fixture.Manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Assert.Equal(before.Digest, new ReleaseBundleCodec().Validate(formatted, fixture.Blobs).Digest);
        fixture.Manifest["resourceRequirements"]!.AsArray().Add(new JsonObject
        {
            ["key"] = new JsonObject { ["member"] = ReleaseBundleFixture.Key(1), ["origin"] = new JsonArray("outer", "inner"), ["reference"] = "Source", ["kind"] = "etl-file" },
            ["schemaVersion"] = "1"
        });
        var scoped = fixture.Validate().Digest;
        fixture.Manifest["resourceRequirements"]![0]!["key"]!["origin"] = new JsonArray("inner", "outer");
        Assert.NotEqual(scoped, fixture.Validate().Digest);
    }

    [Fact]
    public void Validated_transport_snapshots_inputs_and_never_exposes_mutable_backing_arrays()
    {
        var fixture = new ReleaseBundleFixture();
        var manifest = fixture.Bytes;
        var digest = fixture.Blobs.Keys.First();
        var blob = fixture.Blobs[digest].ToArray();
        fixture.Blobs[digest] = blob;
        var result = new ReleaseBundleCodec().Validate(manifest, fixture.Blobs);
        var expectedManifest = (byte[])manifest.Clone(); var expectedBlob = (byte[])blob.Clone(); var identity = result.Digest;
        manifest[0] = 0; blob[0] = 0;
        result.GetManifestBytes()[0] = 1; result.GetBlobBytes(digest)[0] = 1;
        Assert.Equal(identity, result.Digest);
        Assert.Equal(expectedManifest, result.GetManifestBytes()); Assert.Equal(expectedBlob, result.GetBlobBytes(digest));
        Assert.Equal("elsa.workflow-release", result.Manifest.GetProperty("format").GetString());
        Assert.False(result.Members is ReleaseBundleMember[]);
    }

    [Theory]
    [InlineData("{\"a\":1,\"a\":2}", "duplicate_json_property")]
    [InlineData("{\"a\":[{\"x\":1,\"\\u0078\":2}]}", "duplicate_json_property")]
    [InlineData("{\"a\":1,}", "invalid_json")]
    [InlineData("{\"a\":/*c*/1}", "invalid_json")]
    [InlineData("{}{}", "invalid_json")]
    [InlineData("{\"value\":\"\\uD800\"}", "invalid_json")]
    public void Invalid_blob_json_is_refused_even_when_its_digest_matches(string json, string code)
    {
        var fixture = new ReleaseBundleFixture(1);
        ReplaceBlob(fixture, Encoding.UTF8.GetBytes(json));
        Assert.Equal(code, Assert.Throws<ReleaseBundleFormatException>(() => fixture.Validate()).Code);
    }

    [Fact]
    public void Invalid_utf8_and_bom_are_refused_before_decoding()
    {
        var fixture = new ReleaseBundleFixture(1);
        ReplaceBlob(fixture, [0x22, 0xc0, 0xaf, 0x22]);
        Assert.Equal("invalid_json", Assert.Throws<ReleaseBundleFormatException>(() => fixture.Validate()).Code);
        ReplaceBlob(fixture, [0xef, 0xbb, 0xbf, 0x7b, 0x7d]);
        Assert.Equal("utf8_bom", Assert.Throws<ReleaseBundleFormatException>(() => fixture.Validate()).Code);
    }

    [Fact]
    public void Unpaired_surrogate_in_scoped_identity_has_a_safe_format_refusal()
    {
        var fixture = new ReleaseBundleFixture();
        var json = Encoding.UTF8.GetString(fixture.Bytes).Replace("member-0", "\\uD800", StringComparison.Ordinal);
        var failure = Assert.Throws<ReleaseBundleFormatException>(() => new ReleaseBundleCodec().Validate(Encoding.UTF8.GetBytes(json), fixture.Blobs));
        Assert.Equal("invalid_json", failure.Code);
        Assert.DoesNotContain("D800", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_fields_profiles_and_noncanonical_integers_refuse()
    {
        var fixture = new ReleaseBundleFixture(); fixture.Manifest["newField"] = true;
        Assert.Equal("unknown_or_missing_field", Assert.Throws<ReleaseBundleFormatException>(() => fixture.Validate()).Code);
        fixture.Manifest.Remove("newField"); fixture.Manifest["profile"] = "future/2";
        Assert.Equal("unsupported_format_or_profile", Assert.Throws<ReleaseBundleFormatException>(() => fixture.Validate()).Code);
        fixture.Manifest["profile"] = ReleaseManifest.Profile;
        var bytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(fixture.Bytes).Replace("\"formatVersion\":1", "\"formatVersion\":1e0", StringComparison.Ordinal));
        Assert.Equal("invalid_integer", Assert.Throws<ReleaseBundleFormatException>(() => new ReleaseBundleCodec().Validate(bytes, fixture.Blobs)).Code);
    }

    [Theory]
    [InlineData("member", "duplicate_member_key")]
    [InlineData("root", "duplicate_root")]
    [InlineData("edge", "duplicate_edge")]
    public void Duplicate_declared_identifiers_refuse(string duplicate, string code)
    {
        var fixture = new ReleaseBundleFixture();
        var array = fixture.Manifest[duplicate == "member" ? "members" : duplicate == "root" ? "roots" : "edges"]!.AsArray();
        array.Add(array[0]!.DeepClone());
        Assert.Equal(code, Assert.Throws<ReleaseBundleFormatException>(() => fixture.Validate()).Code);
    }

    [Fact]
    public void Missing_unrelated_and_cyclic_declared_members_refuse()
    {
        var missing = new ReleaseBundleFixture(); missing.Manifest["edges"]![0]!["to"] = ReleaseBundleFixture.Key(7);
        Assert.Equal("missing_member", Assert.Throws<ReleaseBundleFormatException>(() => missing.Validate()).Code);
        var unrelated = new ReleaseBundleFixture(); unrelated.Manifest["edges"] = new JsonArray();
        Assert.Equal("unrelated_member", Assert.Throws<ReleaseBundleFormatException>(() => unrelated.Validate()).Code);
        var cycle = new ReleaseBundleFixture(); cycle.Manifest["edges"]!.AsArray().Add(ReleaseBundleFixture.Edge(1, 0));
        Assert.Equal("dependency_cycle", Assert.Throws<ReleaseBundleFormatException>(() => cycle.Validate()).Code);
    }

    [Fact]
    public void Association_cycles_do_not_invent_executable_cycles_and_diamond_depth_is_checked()
    {
        var fixture = new ReleaseBundleFixture(); fixture.Manifest["edges"]!.AsArray().Add(ReleaseBundleFixture.Edge(1, 0, "association"));
        Assert.NotNull(fixture.Validate());
        var deep = new ReleaseBundleFixture(4);
        deep.Manifest["edges"]!.AsArray().Insert(0, ReleaseBundleFixture.Edge(0, 3));
        Assert.Equal("dependency_depth_exceeded", Assert.Throws<ReleaseBundleFormatException>(() => deep.Validate(new() { MaxDependencyDepth = 2 })).Code);
    }

    [Fact]
    public void Tampered_missing_unlisted_and_mis_sized_blobs_refuse()
    {
        var tampered = new ReleaseBundleFixture();
        var key = tampered.Blobs.Keys.First();
        var changed = Encoding.UTF8.GetString(tampered.Blobs[key].Span).Replace("\"number\":0", "\"number\":9", StringComparison.Ordinal);
        var changedBytes = Encoding.UTF8.GetBytes(changed);
        Assert.Equal(tampered.Blobs[key].Length, changedBytes.Length);
        tampered.Blobs[key] = changedBytes;
        Assert.Equal("blob_digest_mismatch", Assert.Throws<ReleaseBundleFormatException>(() => tampered.Validate()).Code);
        var missing = new ReleaseBundleFixture(); missing.Blobs.Remove(missing.Blobs.Keys.First());
        Assert.Equal("missing_blob", Assert.Throws<ReleaseBundleFormatException>(() => missing.Validate()).Code);
        var extra = new ReleaseBundleFixture(); extra.Blobs.Add(ReleaseManifest.Hash("{}"u8), "{}"u8.ToArray());
        Assert.Equal("unlisted_blob", Assert.Throws<ReleaseBundleFormatException>(() => extra.Validate()).Code);
        var length = new ReleaseBundleFixture(); length.Manifest["members"]![0]!["byteLength"] = 2;
        Assert.Equal("blob_length_mismatch", Assert.Throws<ReleaseBundleFormatException>(() => length.Validate()).Code);
    }

    [Fact]
    public void Entry_member_edge_json_and_total_byte_limits_are_enforced()
    {
        var fixture = new ReleaseBundleFixture(3);
        Assert.Equal("byte_limit_exceeded", Assert.Throws<ReleaseBundleFormatException>(() => fixture.Validate(new() { MaxManifestBytes = 2 })).Code);
        Assert.Equal("invalid_member_bounds", Assert.Throws<ReleaseBundleFormatException>(() => fixture.Validate(new() { MaxBlobBytes = 2 })).Code);
        Assert.Equal("byte_limit_exceeded", Assert.Throws<ReleaseBundleFormatException>(() => fixture.Validate(new() { MaxExpandedBytes = fixture.Bytes.Length + 1 })).Code);
        Assert.Equal("entry_limit_exceeded", Assert.Throws<ReleaseBundleFormatException>(() => fixture.Validate(new() { MaxEntries = 1 })).Code);
        Assert.Equal("item_limit_exceeded", Assert.Throws<ReleaseBundleFormatException>(() => fixture.Validate(new() { MaxMembers = 2 })).Code);
        Assert.Equal("item_limit_exceeded", Assert.Throws<ReleaseBundleFormatException>(() => fixture.Validate(new() { MaxEdges = 1 })).Code);
        Assert.Equal("invalid_json", Assert.Throws<ReleaseBundleFormatException>(() => fixture.Validate(new() { MaxJsonDepth = 2 })).Code);
    }

    [Fact]
    public void Resource_keys_keep_occurrence_scope_and_duplicate_requirements_refuse()
    {
        var fixture = new ReleaseBundleFixture();
        var requirements = fixture.Manifest["resourceRequirements"]!.AsArray();
        JsonObject Requirement(string occurrence) => new()
        {
            ["key"] = new JsonObject { ["member"] = ReleaseBundleFixture.Key(1), ["origin"] = new JsonArray(occurrence), ["reference"] = "Source", ["kind"] = "file" },
            ["schemaVersion"] = "1"
        };
        requirements.Add(Requirement("first")); requirements.Add(Requirement("second"));
        Assert.NotNull(fixture.Validate());
        requirements.Add(Requirement("first"));
        Assert.Equal("duplicate_resource_requirement", Assert.Throws<ReleaseBundleFormatException>(() => fixture.Validate()).Code);
    }

    private static void ReplaceBlob(ReleaseBundleFixture fixture, byte[] bytes)
    {
        var digest = ReleaseManifest.Hash(bytes); fixture.Blobs.Clear(); fixture.Blobs.Add(digest, bytes);
        fixture.Manifest["members"]![0]!["payloadDigest"] = digest; fixture.Manifest["members"]![0]!["byteLength"] = bytes.Length;
    }
}
