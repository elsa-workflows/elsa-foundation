using Elsa.Activities.Design.Core.Contracts;
using Elsa.Workflows.Design.Validations.Validators;
using Xunit;
using static Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests.ValidatorTestHelpers;

namespace Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests;

/// <summary>
/// FR-033 2026-07-05 amendment. Branch coverage — resolvable version clean, unresolvable version
/// flagged, per-node granularity in a mixed graph, recursion into ChildActivities, empty draft.
/// An unresolvable <c>ActivityVersionId</c> is a compile error the author must see at the node:
/// the store's Get contract throws <c>EntityNotFoundException</c> on a missing id (the stub
/// mirrors that), and without this validator the gate faults with that opaque exception instead
/// of reporting the offending node (see PromotionGateTests for the FR-024 consequence).
/// </summary>
public sealed class UnknownActivityVersionValidatorTests
{
    [Fact]
    public async Task Resolvable_activity_version_emits_no_error()
    {
        var state = StateWithRoot(Node("n1", "av-1"));
        var errors = await Validate(Validator(new StubActivityCatalog().Add("av-1")), state);

        Assert.Empty(errors);
    }

    [Fact]
    public async Task Unresolvable_activity_version_emits_error()
    {
        var state = StateWithRoot(Node("n1", "av-missing"));
        var errors = await Validate(Validator(new StubActivityCatalog()), state);

        var error = Assert.Single(errors);
        Assert.Equal("n1", error.Path);
        Assert.Equal("Graph/UnknownActivityVersion", error.Type);
        Assert.Contains("av-missing", error.Message);
    }

    [Fact]
    public async Task Mixed_graph_flags_only_the_unresolvable_nodes()
    {
        var state = State(activities:
        [
            Node("start", "av-1"),
            Node("n2", "av-gone"),
        ]);
        var errors = await Validate(Validator(new StubActivityCatalog().Add("av-1")), state);

        var error = Assert.Single(errors);
        Assert.Equal("n2", error.Path);
    }

    [Fact]
    public async Task Unresolvable_version_on_nested_child_activity_emits_error()
    {
        var child = Node("child", "av-gone");
        var root = Node("container", "av-container", childActivities: [child]);
        var state = StateWithRoot(root);
        var errors = await Validate(Validator(new StubActivityCatalog().Add("av-container")), state);

        var error = Assert.Single(errors);
        Assert.Equal("child", error.Path);
    }

    [Fact]
    public async Task Draft_without_root_activity_emits_no_error()
    {
        var errors = await Validate(Validator(new StubActivityCatalog()), State());

        Assert.Empty(errors);
    }

    private static UnknownActivityVersionValidator Validator(IActivityDefinitionLookup catalog) =>
        new(Resolver(catalog), Options(), Walker());
}
