using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Validations.Validators;
using Xunit;
using static Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests.ValidatorTestHelpers;

namespace Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests;

/// <summary>
/// SC-022(d) + SC-022(e) activity-level. Branch coverage — required satisfied, required missing,
/// required present-but-empty, recursion into ChildActivities, unknown version skipped (the
/// unknown-version *error* is <see cref="UnknownActivityVersionValidator"/>'s concern per the
/// FR-033 2026-07-05 amendment; this validator resolves via <c>CatalogVersionResolver</c> and
/// skips the unresolvable node rather than double-reporting or faulting the gate).
/// </summary>
public sealed class RequiredInputOutputValidatorTests
{
    private readonly StubActivityCatalog _catalog = new StubActivityCatalog()
        .Add("av-1", inputs: [RequiredInput("body")]);

    [Fact]
    public async Task Required_input_satisfied_emits_no_error()
    {
        var state = State(activities: [Node("n1", "av-1",
            inputs: [LiteralInput("body", "hello")])]);
        var errors = await Validate(Validator(_catalog), state);

        Assert.Empty(errors);
    }

    [Fact]
    public async Task Required_input_missing_emits_error()
    {
        var state = State(activities: [Node("n1", "av-1")]);
        var errors = await Validate(Validator(_catalog), state);

        var error = Assert.Single(errors);
        Assert.Equal("n1/inputs/body", error.Path);
        Assert.Equal("InputOutput/MissingRequired", error.Type);
    }

    [Fact]
    public async Task Required_input_present_but_empty_value_emits_error()
    {
        var state = State(activities: [Node("n1", "av-1",
            inputs: [LiteralInput("body", "")])]);
        var errors = await Validate(Validator(_catalog), state);

        Assert.Single(errors);
    }

    [Fact]
    public async Task Required_input_present_but_empty_json_string_value_emits_error()
    {
        var state = State(activities: [Node("n1", "av-1",
            inputs: [LiteralInput("body", JsonSerializer.SerializeToElement(""))])]);
        var errors = await Validate(Validator(_catalog), state);

        Assert.Single(errors);
    }

    [Fact]
    public async Task Required_input_present_but_undefined_json_value_emits_error()
    {
        var state = StateWithRoot(Node("n1", "av-1",
            inputs: [LiteralInput("body", default(JsonElement))]));
        var errors = await Validate(Validator(_catalog), state);

        Assert.Single(errors);
    }

    [Fact]
    public async Task Required_output_missing_emits_error()
    {
        var catalog = new StubActivityCatalog().Add("av-1", outputs: [RequiredOutput("result")]);

        var state = State(activities: [Node("n1", "av-1")]);
        var errors = await Validate(Validator(catalog), state);

        var error = Assert.Single(errors);
        Assert.Equal("n1/outputs/result", error.Path);
    }

    [Fact]
    public async Task Unknown_activity_version_is_skipped_gracefully()
    {
        // The store's Get contract throws on a missing id; CatalogVersionResolver folds that to
        // null and this validator skips the node — UnknownActivityVersionValidator owns the report.
        var state = State(activities: [Node("n1", "av-missing")]);
        var errors = await Validate(Validator(new StubActivityCatalog()), state);

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Blank_activity_version_id_is_skipped_without_crashing(string? versionId)
    {
        // A null id would otherwise throw ArgumentNullException from CatalogVersionResolver's
        // dictionary and fault the gate; the resolver folds blank ids to null and this validator
        // skips the node (UnknownActivityVersionValidator owns the report).
        var state = StateWithRoot(Node("n1", versionId!));
        var errors = await Validate(Validator(new StubActivityCatalog()), state);

        Assert.Empty(errors);
    }

    [Fact]
    public async Task Required_input_missing_on_nested_child_activity_emits_error()
    {
        var child = Node("child", "av-1");
        var root = Node("container", "av-1", childActivities: [child]);
        var state = State(activities: [root]);
        var errors = await Validate(Validator(_catalog), state);

        // One error per activity (root + child) — both have unbound required "body".
        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, e => e.Path == "container/inputs/body");
        Assert.Contains(errors, e => e.Path == "child/inputs/body");
    }

    private static RequiredInputOutputValidator Validator(IActivityDefinitionLookup catalog) =>
        new(CatalogResolver(catalog), Options(), Walker());

    private static InputDefinition RequiredInput(string referenceKey) => new(
        ReferenceKey: referenceKey,
        Name: referenceKey,
        Type: new TypeReference("String"),
        StorageDriverType: null,
        DisplayName: referenceKey,
        Category: null,
        IsNullable: false,
        IsRequired: true);

    private static OutputDefinition RequiredOutput(string referenceKey) => new(
        ReferenceKey: referenceKey,
        Name: referenceKey,
        Type: new TypeReference("String"),
        StorageDriverType: null,
        DisplayName: referenceKey,
        Category: null,
        IsNullable: false,
        IsRequired: true);
}
