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
