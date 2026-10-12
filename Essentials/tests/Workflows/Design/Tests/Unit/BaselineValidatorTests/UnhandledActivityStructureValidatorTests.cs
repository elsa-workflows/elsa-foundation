using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Validators;
using Xunit;
using static Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests.ValidatorTestHelpers;

namespace Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests;

/// <summary>
/// Baseline coverage for the declared-structure-without-handler guard. Spec 071 FR-008 keeps a
/// structure of UNKNOWN kind opaque, so the validator must flag ONLY the composition mismatch:
/// the activity's catalog design facets declare the authored structure kind, but no handler is
/// registered in the shell (e.g. a BPMN process authored while ActivitiesBpmn is disabled).
/// Undeclared kinds stay opaque and valid; an unresolvable version id is owned by
/// <see cref="UnknownActivityVersionValidator"/>.
/// </summary>
public sealed class UnhandledActivityStructureValidatorTests
{
    private const string UnhandledKind = "test.unhandled.structure";
    private const string UnhandledSchemaVersion = "1.0.0";

    [Fact]
    public async Task Handled_structure_emits_no_error()
    {
        var state = StateWithRoot(Node("n1", "av-1", childActivities: [Node("child", "av-1")]));
        var errors = await Validate(Validator(new StubActivityCatalog().Add("av-1")), state);

        Assert.Empty(errors);
    }

    [Fact]
    public async Task Declared_structure_without_handler_emits_error()
    {
        var state = StateWithRoot(UnhandledNode("n1", "av-1"));
        var catalog = new StubActivityCatalog().Add("av-1", designFacets: [Facet(UnhandledKind)]);
        var errors = await Validate(Validator(catalog), state);

        var error = Assert.Single(errors);
        Assert.Equal("n1", error.Path);
        Assert.Equal("Graph/UnhandledActivityStructure", error.Type);
        Assert.Contains(UnhandledKind, error.Message);
    }

    [Fact]
    public async Task Undeclared_structure_without_handler_stays_opaque_and_valid()
    {
        var state = StateWithRoot(UnhandledNode("n1", "av-1"));
        var errors = await Validate(Validator(new StubActivityCatalog().Add("av-1")), state);

        Assert.Empty(errors);
    }

    [Fact]
    public async Task Unresolvable_activity_version_is_not_reported_here()
    {
        var state = StateWithRoot(UnhandledNode("n1", "av-missing"));
        var errors = await Validate(Validator(new StubActivityCatalog()), state);

        Assert.Empty(errors);
    }

    [Fact]
    public async Task Declared_structure_without_handler_on_nested_child_emits_error()
    {
        var child = UnhandledNode("child", "av-composite");
        var state = StateWithRoot(Node("container", "av-1", childActivities: [child]));
        var catalog = new StubActivityCatalog()
            .Add("av-1")
            .Add("av-composite", designFacets: [Facet(UnhandledKind)]);
        var errors = await Validate(Validator(catalog), state);

        var error = Assert.Single(errors);
        Assert.Equal("child", error.Path);
    }

    private static UnhandledActivityStructureValidator Validator(IActivityDefinitionLookup catalog) =>
        new(CatalogResolver(catalog), StructureService(), Options(), Walker());

    private static ActivityNode UnhandledNode(string nodeId, string activityVersionId) =>
        new(
            NodeId: nodeId,
            ActivityVersionId: activityVersionId,
            Inputs: [],
            Outputs: [],
            Structure: new ActivityNodeStructure(
                UnhandledKind,
                UnhandledSchemaVersion,
                JsonSerializer.SerializeToElement(new { marker = "kept" })));

    private static ActivityDesignFacet Facet(string kind) =>
        new(kind, UnhandledSchemaVersion, JsonSerializer.SerializeToElement(new { mode = "generic" }));
}
