using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Validations.Validators;
using Xunit;
using static Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests.ValidatorTestHelpers;

namespace Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests;

/// <summary>
/// SC-022(d) + SC-022(e) activity-level. Branch coverage — required satisfied, required missing,
/// required present-but-empty, recursion into ChildActivities, unknown version graceful skip.
/// </summary>
public sealed class RequiredInputOutputValidatorTests
{
    [Fact]
    public async Task Required_input_satisfied_emits_no_error()
    {
        var lookup = StubLookup.WithVersion("av-1",
            inputs: [RequiredInput("body")],
            outputs: []);

        var state = State(activities: [Node("n1", "av-1",
            inputs: [LiteralInput("body", "hello")])]);
        var errors = await Validate(new RequiredInputOutputValidator(lookup, Options()), state);

        Assert.Empty(errors);
    }

    [Fact]
    public async Task Required_input_missing_emits_error()
    {
        var lookup = StubLookup.WithVersion("av-1",
            inputs: [RequiredInput("body")],
            outputs: []);

        var state = State(activities: [Node("n1", "av-1")]);
        var errors = await Validate(new RequiredInputOutputValidator(lookup, Options()), state);

        var error = Assert.Single(errors);
        Assert.Equal("n1/inputs/body", error.Path);
        Assert.Equal("InputOutput/MissingRequired", error.Type);
    }

    [Fact]
    public async Task Required_input_present_but_empty_value_emits_error()
    {
        var lookup = StubLookup.WithVersion("av-1",
            inputs: [RequiredInput("body")],
            outputs: []);

        var state = State(activities: [Node("n1", "av-1",
            inputs: [LiteralInput("body", "")])]);
        var errors = await Validate(new RequiredInputOutputValidator(lookup, Options()), state);

        Assert.Single(errors);
    }

    [Fact]
    public async Task Required_output_missing_emits_error()
    {
        var lookup = StubLookup.WithVersion("av-1",
            inputs: [],
            outputs: [RequiredOutput("result")]);

        var state = State(activities: [Node("n1", "av-1")]);
        var errors = await Validate(new RequiredInputOutputValidator(lookup, Options()), state);

        var error = Assert.Single(errors);
        Assert.Equal("n1/outputs/result", error.Path);
    }

    [Fact]
    public async Task Unknown_activity_version_is_skipped_gracefully()
    {
        var lookup = StubLookup.Empty();

        var state = State(activities: [Node("n1", "av-missing")]);
        var errors = await Validate(new RequiredInputOutputValidator(lookup, Options()), state);

        Assert.Empty(errors);
    }

    [Fact]
    public async Task Required_input_missing_on_nested_child_activity_emits_error()
    {
        var lookup = StubLookup.WithVersion("av-1",
            inputs: [RequiredInput("body")],
            outputs: []);

        var child = Node("child", "av-1");
        var root = Node("container", "av-1", childActivities: [child]);
        var state = State(activities: [root]);
        var errors = await Validate(new RequiredInputOutputValidator(lookup, Options()), state);

        // One error per activity (root + child) — both have unbound required "body".
        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, e => e.Path == "container/inputs/body");
        Assert.Contains(errors, e => e.Path == "child/inputs/body");
    }

    private static InputDefinition RequiredInput(string referenceKey) => new(
        ReferenceKey: referenceKey,
        Name: referenceKey,
        Type: TypeInformation.String,
        StorageDriverType: null,
        DisplayName: referenceKey,
        Category: null,
        IsRequired: true);

    private static OutputDefinition RequiredOutput(string referenceKey) => new(
        ReferenceKey: referenceKey,
        Name: referenceKey,
        Type: TypeInformation.String,
        StorageDriverType: null,
        DisplayName: referenceKey,
        Category: null,
        IsRequired: true);

    private sealed class StubLookup : IActivityDefinitionLookup
    {
        private readonly Dictionary<string, IActivityDefinitionVersion> _versions;

        private StubLookup(Dictionary<string, IActivityDefinitionVersion> versions)
            => _versions = versions;

        public static StubLookup Empty() => new(new());

        public static StubLookup WithVersion(string versionId, IEnumerable<InputDefinition> inputs, IEnumerable<OutputDefinition> outputs)
            => new(new() { [versionId] = new StubVersion(versionId, inputs, outputs) });

        public Task<IActivityDefinitionVersion> GetVersion(string versionId, CancellationToken cancellationToken = default)
            => Task.FromResult(_versions.TryGetValue(versionId, out var version) ? version : null!);

        public Task<IActivityDefinition> GetDefinition(string idOrActivityTypeKey, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<IEnumerable<IActivityDefinition>> ListDefinitions(string? id = null, string? category = null, string? searchTerm = null, string? displayName = null, string? description = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<IEnumerable<ActivityDefinitionVersionInfo>> ListVersions(string definitionId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    private sealed class StubVersion(string id, IEnumerable<InputDefinition> inputs, IEnumerable<OutputDefinition> outputs) : IActivityDefinitionVersion
    {
        public string Id { get; } = id;
        public string Version => "1.0.0";
        public string DefinitionId => "def-1";
        public string ActivityTypeKey => "TestActivity";
        public string DescriptorType => "Test";
        public System.Text.Json.JsonElement DescriptorPayload => default;
        public IActivityDefinition Definition => null!;
        public IEnumerable<InputDefinition> Inputs { get; } = inputs;
        public IEnumerable<OutputDefinition> Outputs { get; } = outputs;
        public IEnumerable<ActivityPortDefinition> Ports => [];
        public ActivityExecutionType ExecutionType => default;
        public string? ReconcilliationHash => null;
    }
}
