using System.Text;
using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Disclosure.Services;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

public sealed class CatalogDisclosureRequirementsTests
{
    private readonly CatalogDisclosureRequirementCanonicalizer canonicalizer = new();

    [Fact]
    public void Zero_defaults_and_missing_or_unknown_classification_never_become_public()
    {
        Assert.Equal(CatalogDisclosureKind.Unspecified, default);
        Assert.Equal(CatalogDisclosureStatus.Unavailable, default);
        Assert.Equal(CatalogOwnerScopeKind.Unspecified, default);
        Assert.False(canonicalizer.TryCanonicalize(null, out _));
        Assert.False(canonicalizer.TryCanonicalize(new(1, default, []), out _));
        Assert.False(canonicalizer.TryCanonicalize(new(1, (CatalogDisclosureKind)99, []), out _));
        Assert.False(canonicalizer.TryCanonicalize(new(0, CatalogDisclosureKind.Public, []), out _));
        Assert.False(canonicalizer.TryCanonicalize(new(2, CatalogDisclosureKind.Public, []), out _));
        Assert.False(canonicalizer.TryCanonicalize(new(1, CatalogDisclosureKind.Public, null!), out _));
        Assert.False(canonicalizer.AreEquivalent(null, null));
    }

    [Fact]
    public void Public_is_explicit_and_has_no_policy_while_protected_requires_a_policy()
    {
        Assert.Equal(CatalogDisclosureKind.Public, canonicalizer.Canonicalize(Public()).Kind);
        Assert.False(canonicalizer.TryCanonicalize(new(1, CatalogDisclosureKind.Protected, []), out _));
        Assert.False(canonicalizer.TryCanonicalize(new(1, CatalogDisclosureKind.Public, [Policy()]), out _));
        Assert.False(canonicalizer.TryCanonicalize(new(1, CatalogDisclosureKind.Protected, [null!]), out _));
    }

    [Fact]
    public void Models_detach_json_and_collection_aliases_before_the_source_document_is_disposed()
    {
        CatalogDisclosureRequirement requirement;
        using (var document = JsonDocument.Parse("{\"source\":\"original\"}"))
        {
            var policies = new List<CatalogDisclosurePolicyRequirement> { new("source", "1", document.RootElement) };
            requirement = new(1, CatalogDisclosureKind.Protected, policies);
            policies.Clear();
        }

        var canonical = canonicalizer.Canonicalize(requirement);
        Assert.Equal("original", Assert.Single(canonical.Policies).Resource.GetProperty("source").GetString());
        Assert.Throws<NotSupportedException>(() =>
            ((IList<CatalogDisclosurePolicyRequirement>)requirement.Policies).Clear());

        var identities = new List<CatalogDisclosureIdentity> { new("version", "definition", "version", new(CatalogOwnerScopeKind.Global, null)) };
        var snapshot = new CatalogDisclosureSnapshot(2, identities);
        identities.Clear();
        Assert.Equal("version", Assert.Single(snapshot.Identities).VersionId);
        Assert.Throws<NotSupportedException>(() => ((IList<CatalogDisclosureIdentity>)snapshot.Identities).Clear());
    }

    [Fact]
    public void Json_round_trip_retains_explicit_protection_without_loading_its_policy_module()
    {
        var original = Protected(Policy("missing.policy", "2", "{\"source\":\"private\"}"));
        var restored = JsonSerializer.Deserialize<CatalogDisclosureRequirement>(JsonSerializer.Serialize(original));
        Assert.NotNull(restored);
        Assert.Equal(CatalogDisclosureKind.Protected, restored.Kind);
        Assert.True(canonicalizer.AreEquivalent(original, restored));
    }

    [Fact]
    public void Canonicalization_sorts_ordinally_and_deduplicates_exact_conjunctions()
    {
        var left = Protected(Policy("z", "1", "{\"b\":2,\"a\":1}"), Policy("A"), Policy("a"), Policy("z", "1", "{\"a\":1,\"b\":2}"));
        var right = Protected(Policy("a"), Policy("z", "1", "{\"a\":1,\"b\":2}"), Policy("A"));
        var canonical = canonicalizer.Canonicalize(left);

        Assert.Equal(new[] { "A", "a", "z" }, canonical.Policies.Select(policy => policy.PolicyKey));
        Assert.Equal("{\"a\":1,\"b\":2}", canonical.Policies[2].Resource.GetRawText());
        Assert.True(canonicalizer.AreEquivalent(left, right));
        Assert.Equal(canonicalizer.ToCanonicalJson(left), canonicalizer.ToCanonicalJson(right));
        Assert.False(canonicalizer.AreEquivalent(left, Protected(Policy("a"))));
    }

    [Theory]
    [InlineData("{\"same\":1,\"same\":2}")]
    [InlineData("{\"a\":1,\"\\u0061\":2}")]
    [InlineData("{\"nested\":[{\"a\":1,\"a\":2}]}")]
    public void Duplicate_json_names_refuse_instead_of_choosing_a_different_binding(string resource)
    {
        Assert.False(canonicalizer.TryCanonicalize(Protected(Policy(resource: resource)), out _));
    }

    [Fact]
    public void Json_property_identity_is_ordinal_and_array_order_is_retained()
    {
        var canonical = canonicalizer.Canonicalize(Protected(Policy(resource: "{\"a\":1,\"A\":2,\"array\":[2,1,null]}")));
        Assert.Equal("{\"A\":2,\"a\":1,\"array\":[2,1,null]}", Assert.Single(canonical.Policies).Resource.GetRawText());
    }

    [Fact]
    public void Exact_numbers_are_not_converted_through_floating_point_or_normalized_to_another_token()
    {
        const string resource = "{\"amount\":12345678901234567890.12345678,\"huge\":1e400,\"spelling\":1.00}";
        var canonical = canonicalizer.Canonicalize(Protected(Policy(resource: resource)));
        Assert.Equal(resource, Assert.Single(canonical.Policies).Resource.GetRawText());
        Assert.False(canonicalizer.AreEquivalent(Protected(Policy(resource: "1.00")), Protected(Policy(resource: "1"))));
    }

    [Fact]
    public void Missing_root_binding_and_missing_keys_refuse_without_disclosing_the_bad_input()
    {
        foreach (var policy in new[]
                 {
                     new CatalogDisclosurePolicyRequirement("source", "1", default),
                     Policy(resource: "null"), Policy(key: ""), Policy(key: " "), Policy(schema: ""), Policy(schema: null!)
                 })
            Assert.False(canonicalizer.TryCanonicalize(Protected(policy), out _));

        var exception = Assert.Throws<CatalogDisclosureRequirementException>(() =>
            canonicalizer.Canonicalize(Protected(Policy(key: new string('S', 129)))));
        Assert.Equal("activities.catalog.requirement-invalid", CatalogDisclosureRequirementException.Code);
        Assert.DoesNotContain(new string('S', 20), exception.Message);
    }

    [Fact]
    public void Malformed_unicode_binding_refuses_with_a_safe_domain_cause()
    {
        using var document = JsonDocument.Parse("\"\\uDEAD\"");
        var requirement = Protected(new CatalogDisclosurePolicyRequirement("source", "1", document.RootElement));
        Assert.False(canonicalizer.TryCanonicalize(requirement, out _));
        var exception = Assert.Throws<CatalogDisclosureRequirementException>(() => canonicalizer.Canonicalize(requirement));
        Assert.NotNull(exception.InnerException);
        Assert.DoesNotContain("DEAD", exception.Message);
    }

    [Fact]
    public void Policy_and_schema_keys_accept_the_exact_limits_and_refuse_the_next_character()
    {
        Assert.True(canonicalizer.TryCanonicalize(Protected(Policy(new string('k', 128), new string('s', 64))), out _));
        Assert.False(canonicalizer.TryCanonicalize(Protected(Policy(new string('k', 129))), out _));
        Assert.False(canonicalizer.TryCanonicalize(Protected(Policy(schema: new string('s', 65))), out _));
    }

    [Fact]
    public void Malformed_utf16_keys_cannot_alias_a_registered_replacement_character_after_serialization()
    {
        Assert.False(canonicalizer.TryCanonicalize(Protected(Policy(key: "bad\uD800")), out _));
        Assert.False(canonicalizer.TryCanonicalize(Protected(Policy(schema: "bad\uDC00")), out _));
        Assert.True(canonicalizer.TryCanonicalize(Protected(Policy(key: "bad\uFFFD")), out _));
        Assert.True(canonicalizer.TryCanonicalize(Protected(Policy(key: "valid\uD83D\uDE00")), out _));
        Assert.False(canonicalizer.AreEquivalent(Protected(Policy(key: "bad\uD800")), Protected(Policy(key: "bad\uFFFD"))));
    }

    [Fact]
    public void Policy_count_is_bounded_before_deduplication_and_union_never_truncates()
    {
        var sixtyFour = Enumerable.Range(0, 64).Select(index => Policy($"p{index:00}")).ToArray();
        Assert.Equal(64, canonicalizer.Canonicalize(Protected(sixtyFour)).Policies.Count);
        Assert.False(canonicalizer.TryCanonicalize(Protected(sixtyFour.Append(sixtyFour[0]).ToArray()), out _));
        Assert.Equal(64, canonicalizer.Union([Protected(sixtyFour), Protected(sixtyFour)]).Policies.Count);
        Assert.Throws<CatalogDisclosureRequirementException>(() => canonicalizer.Union([Protected(sixtyFour), Protected(Policy("extra"))]));
    }

    [Fact]
    public void Resource_depth_accepts_sixteen_containers_and_refuses_seventeen()
    {
        Assert.True(canonicalizer.TryCanonicalize(Protected(Policy(resource: NestedArray(16))), out _));
        Assert.False(canonicalizer.TryCanonicalize(Protected(Policy(resource: NestedArray(17))), out _));
    }

    [Fact]
    public void Canonical_resource_bytes_are_inclusive_and_escape_expansion_counts()
    {
        Assert.True(canonicalizer.TryCanonicalize(Protected(StringPolicy(new string('x', 16382))), out _));
        Assert.False(canonicalizer.TryCanonicalize(Protected(StringPolicy(new string('x', 16383))), out _));
        Assert.True(canonicalizer.TryCanonicalize(Protected(StringPolicy(new string('<', 2730))), out _));
        Assert.False(canonicalizer.TryCanonicalize(Protected(StringPolicy(new string('<', 2731))), out _));
    }

    [Fact]
    public void Complete_requirement_byte_limit_is_inclusive_and_cannot_hide_behind_per_policy_limits()
    {
        var limit = RequirementWithBytes(256 * 1024);
        Assert.Equal(256 * 1024, Encoding.UTF8.GetByteCount(canonicalizer.ToCanonicalJson(limit)));
        Assert.True(canonicalizer.TryCanonicalize(limit, out _));
        Assert.False(canonicalizer.TryCanonicalize(RequirementWithBytes(256 * 1024 + 1), out _));
    }

    [Fact]
    public void Union_is_a_monotonic_conjunction_and_invalid_or_absent_inheritance_never_becomes_public()
    {
        var first = Protected(Policy("source-a"));
        var second = Protected(Policy("source-b"));
        var union = canonicalizer.Union([Public(), first, second, first]);
        Assert.Equal(CatalogDisclosureKind.Protected, union.Kind);
        Assert.Equal(new[] { "source-a", "source-b" }, union.Policies.Select(policy => policy.PolicyKey));
        Assert.True(canonicalizer.AreEquivalent(union, canonicalizer.Union([second, union, Public()])));
        Assert.Equal(CatalogDisclosureKind.Public, canonicalizer.Union([Public(), Public()]).Kind);
        Assert.Throws<CatalogDisclosureRequirementException>(() => canonicalizer.Union([]));
        Assert.Throws<CatalogDisclosureRequirementException>(() => canonicalizer.Union([first, new(1, default, [])]));
    }

    private CatalogDisclosureRequirement RequirementWithBytes(int targetBytes)
    {
        var policies = Enumerable.Range(0, 16).Select(index => StringPolicy("", $"p{index:00}")).ToArray();
        var remaining = targetBytes - Encoding.UTF8.GetByteCount(canonicalizer.ToCanonicalJson(Protected(policies)));
        for (var index = 0; index < policies.Length; index++)
        {
            var count = Math.Min(16382, remaining);
            policies[index] = StringPolicy(new string('x', count), $"p{index:00}");
            remaining -= count;
        }
        Assert.Equal(0, remaining);
        return Protected(policies);
    }

    private static CatalogDisclosureRequirement Public() => new(1, CatalogDisclosureKind.Public, []);

    private static CatalogDisclosureRequirement Protected(params CatalogDisclosurePolicyRequirement[] policies) =>
        new(1, CatalogDisclosureKind.Protected, policies);

    private static CatalogDisclosurePolicyRequirement Policy(string key = "source", string schema = "1", string resource = "{}")
    {
        using var document = JsonDocument.Parse(resource, new JsonDocumentOptions { MaxDepth = 128 });
        return new(key, schema, document.RootElement);
    }

    private static CatalogDisclosurePolicyRequirement StringPolicy(string value, string key = "source") =>
        Policy(key, resource: JsonSerializer.Serialize(value));

    private static string NestedArray(int depth) => new string('[', depth) + "0" + new string(']', depth);
}
