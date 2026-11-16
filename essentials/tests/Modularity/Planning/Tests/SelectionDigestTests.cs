using Elsa.Modularity.Planning.Json;

namespace Elsa.Modularity.Planning.Tests;

public sealed class SelectionDigestTests
{
    [Fact]
    public void Ascii_definition_matches_independent_ecmascript_vector()
    {
        var definition = PlannerFixture.Definition("group", "foundation-core", ["Primitives", "Events"], rationale: "Common core", title: "Foundation core");
        Assert.Equal("a22f7d341423057ec2a3931ab19b38e60d7bc0296ec93618bc391a3d8e799158", definition.Digest);
        Assert.Equal("{\"dependencyExplanations\":[],\"description\":\"Planning fixture only\",\"id\":\"foundation-core\",\"kind\":\"group\",\"members\":[\"Events\",\"Primitives\"],\"rationale\":\"Common core\",\"title\":\"Foundation core\",\"version\":\"1\"}", SelectionDigest.CanonicalDefinition(definition));
    }

    [Fact]
    public void Unicode_definition_matches_independent_ecmascript_vector()
    {
        var definition = PlannerFixture.Definition("group", "unicode-demo", ["B", "A"], rationale: "Café ☕", title: "Grüße", description: "Δ workflow",
            explanations: [new("A", "B", "optional", "Éviter les surprises")]);
        Assert.Equal("44de77def0d72ef7c1d8a4b6ba0e7a04724d26c905cca9482df7ec5491b52e52", definition.Digest);
    }

    [Fact]
    public void Reordering_set_members_does_not_change_definition_or_catalog_digest()
    {
        var first = PlannerFixture.Definition("group", "g", ["B", "A"]);
        var second = PlannerFixture.Definition("group", "g", ["A", "B"]);
        Assert.Equal(first.Digest, second.Digest);
        Assert.Equal(PlannerFixture.Catalog(groups: [first]).Digest, PlannerFixture.Catalog(groups: [second]).Digest);
    }

    [Fact]
    public void Changing_reviewed_text_changes_digest()
    {
        var first = PlannerFixture.Definition("group", "g", ["A"], rationale: "one");
        var second = PlannerFixture.Definition("group", "g", ["A"], rationale: "two");
        Assert.NotEqual(first.Digest, second.Digest);
    }
}
