using Xunit;

namespace Elsa.Persistence.Groundwork.DesignConformance.Tests;

public class DesignPersistenceFixtureDataTests
{
    [Fact]
    public void Equivalent_fixture_values_have_a_stable_result_hash()
    {
        var first = DesignPersistenceFixtureData.ResultHash(DesignPersistenceFixtureData.WorkflowDefinition());
        var second = DesignPersistenceFixtureData.ResultHash(DesignPersistenceFixtureData.WorkflowDefinition());

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
    }

    [Fact]
    public void Equivalent_dictionary_payloads_with_different_insertion_orders_have_the_same_result_hash()
    {
        var first = new Dictionary<string, object?>
        {
            ["z"] = 1,
            ["nested"] = new Dictionary<string, object?> { ["b"] = true, ["a"] = new[] { "first", "second" } },
            ["a"] = "value"
        };
        var second = new Dictionary<string, object?>
        {
            ["a"] = "value",
            ["nested"] = new Dictionary<string, object?> { ["a"] = new[] { "first", "second" }, ["b"] = true },
            ["z"] = 1
        };

        Assert.Equal(DesignPersistenceFixtureData.ResultHash(first), DesignPersistenceFixtureData.ResultHash(second));
    }

    [Fact]
    public void Equivalent_object_payloads_with_different_property_orders_have_the_same_result_hash()
    {
        var first = new { Zeta = 1, Alpha = "value" };
        var second = new { Alpha = "value", Zeta = 1 };

        Assert.Equal(DesignPersistenceFixtureData.ResultHash(first), DesignPersistenceFixtureData.ResultHash(second));
    }

    [Fact]
    public void Semantically_different_payloads_have_different_result_hashes()
    {
        var first = new Dictionary<string, object?> { ["state"] = "published", ["version"] = 1 };
        var second = new Dictionary<string, object?> { ["state"] = "published", ["version"] = 2 };

        Assert.NotEqual(DesignPersistenceFixtureData.ResultHash(first), DesignPersistenceFixtureData.ResultHash(second));
    }

    [Fact]
    public void Payload_serializer_options_are_returned_as_independent_copies()
    {
        var serializer = new DesignPersistenceFixtureData.DeterministicPayloadSerializer();
        var mutated = serializer.GetOptions();
        mutated.WriteIndented = true;

        Assert.False(serializer.GetOptions().WriteIndented);
        Assert.Equal(
            DesignPersistenceFixtureData.ResultHash(new Dictionary<string, int> { ["b"] = 2, ["a"] = 1 }),
            DesignPersistenceFixtureData.ResultHash(new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 }));
    }

    [Fact]
    public void Fixture_scopes_remain_distinct()
    {
        var first = DesignPersistenceFixtureData.WorkflowDefinition(DesignPersistenceFixtureData.ScopeA);
        var second = DesignPersistenceFixtureData.WorkflowDefinition(DesignPersistenceFixtureData.ScopeB);

        Assert.NotEqual(first.TenantId, second.TenantId);
        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public void Layout_fixture_is_bound_to_the_fixed_workflow_version()
    {
        var layout = DesignPersistenceFixtureData.WorkflowVersionLayout();

        Assert.Equal(DesignPersistenceFixtureData.WorkflowVersionLayoutId, layout.Id);
        Assert.Equal(DesignPersistenceFixtureData.WorkflowVersionId, layout.WorkflowDefinitionVersionId);
    }

    [Fact]
    public void Deterministic_identity_generator_fails_when_a_scenario_exceeds_its_declared_inputs()
    {
        var generator = new DesignPersistenceFixtureData.DeterministicIdentityGenerator(["one"]);

        Assert.Equal("one", generator.Generate());
        Assert.Throws<InvalidOperationException>(generator.Generate);
    }
}
