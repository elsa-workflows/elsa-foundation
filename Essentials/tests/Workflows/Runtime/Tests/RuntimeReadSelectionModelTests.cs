using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Resolvers;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// The member-read model of #4078 slice A: three appended member sources, each carrying its role's payload and an
/// immutable read selection, the requirement an artifact derives from them, and refusal everywhere they would
/// otherwise be read. Nothing in this slice interprets a selection.
/// </summary>
public sealed class RuntimeReadSelectionModelTests
{
    private static readonly JsonSerializerOptions ArtifactJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly ValueTypeDescriptor Text = new("String");

    [Fact]
    public void Source_names_and_numbers_are_fixed()
    {
        Assert.Equal(
            ["Literal=0", "Expression=1", "WorkflowRequest=2", "VariableRead=3", "ActivityResult=4", "SecretRead=5",
             "WorkflowRequestMember=6", "VariableMember=7", "ActivityResultMember=8"],
            Enum.GetValues<RuntimeInputBindingSource>().Select(value => $"{value}={(int)value}"));
        Assert.Equal(["Known=0", "DeferredFit=1"],
            Enum.GetValues<RuntimeReadSelectionMode>().Select(value => $"{value}={(int)value}"));
        Assert.Equal("elsa.runtime.input-member-selection", RuntimeReadSelection.RequirementKey);
        Assert.Equal(1, RuntimeReadSelection.CurrentSchemaVersion);
    }

    public static TheoryData<string> MemberKinds => new() { "request", "variable", "result" };

    [Theory]
    [MemberData(nameof(MemberKinds))]
    public void A_member_binding_carries_its_role_payload_and_selection_and_round_trips(string kind)
    {
        var binding = Member(kind, Known(["customer", "id"]));

        var json = JsonSerializer.Serialize(binding, ArtifactJsonOptions);
        var restored = JsonSerializer.Deserialize<RuntimeInputBinding>(json, ArtifactJsonOptions)!;

        Assert.Equal(binding.Source, restored.Source);
        Assert.Equal(["customer", "id"], restored.Selection!.Segments);
        Assert.Equal(RuntimeReadSelectionMode.Known, restored.Selection.Mode);
        Assert.Equal("String", restored.Selection.SelectedType!.Alias);
        Assert.Equal(json, JsonSerializer.Serialize(restored, ArtifactJsonOptions));
        Assert.Contains("\"selection\":{\"schemaVersion\":1,\"segments\":[\"customer\",\"id\"],\"mode\":\"Known\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void A_deferred_selection_writes_only_its_path_and_mode()
    {
        var json = JsonSerializer.Serialize(new RuntimeReadSelection(1, ["a.b", "", "0"], RuntimeReadSelectionMode.DeferredFit), ArtifactJsonOptions);

        Assert.Equal("""{"schemaVersion":1,"segments":["a.b","","0"],"mode":"DeferredFit"}""", json);
    }

    [Fact]
    public void A_whole_value_binding_writes_no_selection()
    {
        var binding = new RuntimeInputBinding("text", Text, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.ActivityResult,
            activityResult: new RuntimeActivityResultReference("producer", "$result", producerScopeId: null));

        Assert.Null(binding.Selection);
        Assert.DoesNotContain("selection", JsonSerializer.Serialize(binding, ArtifactJsonOptions), StringComparison.Ordinal);
    }

    public static TheoryData<string> InvalidBindings => new()
    {
        "selection-on-whole-request", "selection-on-whole-variable", "selection-on-whole-result", "selection-on-literal",
        "request-member-without-selection", "variable-member-without-selection", "result-member-without-selection",
        "result-member-with-scope", "request-member-with-legacy-path", "result-member-with-variable-payload",
        "member-with-two-payloads", "unknown-source"
    };

    [Theory]
    [MemberData(nameof(InvalidBindings))]
    public void An_invalid_member_binding_is_refused(string shape)
    {
        var selection = Known(["id"]);
        var request = new RuntimeWorkflowRequestReference("customer");
        var variable = new RuntimeVariableReference("customer", "workflow");
        var result = new RuntimeActivityResultReference("producer", "$result", producerScopeId: null);
        Action create = shape switch
        {
            "selection-on-whole-request" => () => Binding(RuntimeInputBindingSource.WorkflowRequest, selection, workflowRequest: request),
            "selection-on-whole-variable" => () => Binding(RuntimeInputBindingSource.VariableRead, selection, variable: variable),
            "selection-on-whole-result" => () => Binding(RuntimeInputBindingSource.ActivityResult, selection, activityResult: result),
            "selection-on-literal" => () => Binding(RuntimeInputBindingSource.Literal, selection,
                literal: ValueEnvelope.Inline(Text, JsonSerializer.SerializeToElement("x"), ValueProtectionPolicy.InstanceInline)),
            "request-member-without-selection" => () => Binding(RuntimeInputBindingSource.WorkflowRequestMember, null, workflowRequest: request),
            "variable-member-without-selection" => () => Binding(RuntimeInputBindingSource.VariableMember, null, variable: variable),
            "result-member-without-selection" => () => new RuntimeInputBinding("text", Text, ValueProtectionPolicy.InstanceInline,
                RuntimeInputBindingSource.ActivityResultMember, activityResult: result),
            "result-member-with-scope" => () => Binding(RuntimeInputBindingSource.ActivityResultMember, selection,
                activityResult: new RuntimeActivityResultReference("producer", "$result", "scope:root")),
            "request-member-with-legacy-path" => () => Binding(RuntimeInputBindingSource.WorkflowRequestMember, selection,
                workflowRequest: new RuntimeWorkflowRequestReference("customer", "customer.id")),
            "result-member-with-variable-payload" => () => Binding(RuntimeInputBindingSource.ActivityResultMember, selection, variable: variable),
            "member-with-two-payloads" => () => Binding(RuntimeInputBindingSource.VariableMember, selection, variable: variable, activityResult: result),
            "unknown-source" => () => Binding((RuntimeInputBindingSource)9, selection, activityResult: result),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null)
        };

        Assert.Throws<ArgumentException>(create);
    }

    public static TheoryData<string> InvalidSelections => new()
    {
        "empty-path", "too-many-segments", "too-long-path", "null-segment", "schema-2", "known-without-type",
        "known-without-profile", "blank-profile", "deferred-with-type", "deferred-with-presence", "deferred-with-nullability",
        "unknown-mode"
    };

    [Theory]
    [MemberData(nameof(InvalidSelections))]
    public void An_invalid_selection_is_refused(string shape)
    {
        Action create = shape switch
        {
            "empty-path" => () => _ = Deferred([]),
            "too-many-segments" => () => _ = Deferred(Enumerable.Repeat("a", 65).ToArray()),
            "too-long-path" => () => _ = Deferred([new string('a', 4000), new string('b', 97)]),
            "null-segment" => () => _ = Deferred(["a", null!]),
            "schema-2" => () => _ = new RuntimeReadSelection(2, ["a"], RuntimeReadSelectionMode.DeferredFit),
            "known-without-type" => () => _ = new RuntimeReadSelection(1, ["a"], RuntimeReadSelectionMode.Known, profileRevision: "p"),
            "known-without-profile" => () => _ = new RuntimeReadSelection(1, ["a"], RuntimeReadSelectionMode.Known, Text),
            "blank-profile" => () => _ = new RuntimeReadSelection(1, ["a"], RuntimeReadSelectionMode.Known, Text, profileRevision: " "),
            "deferred-with-type" => () => _ = new RuntimeReadSelection(1, ["a"], RuntimeReadSelectionMode.DeferredFit, Text),
            "deferred-with-presence" => () => _ = new RuntimeReadSelection(1, ["a"], RuntimeReadSelectionMode.DeferredFit, mayBeAbsent: true),
            "deferred-with-nullability" => () => _ = new RuntimeReadSelection(1, ["a"], RuntimeReadSelectionMode.DeferredFit, mayBeNull: false),
            "unknown-mode" => () => _ = new RuntimeReadSelection(1, ["a"], (RuntimeReadSelectionMode)2),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null)
        };

        Assert.ThrowsAny<ArgumentException>(create);
    }

    [Fact]
    public void A_path_at_its_bounds_is_accepted_and_copied()
    {
        var segments = Enumerable.Repeat("x", 63).Append(new string('y', 4096 - 63)).ToArray();

        var selection = Deferred(segments);
        segments[0] = "changed";

        Assert.Equal(64, selection.Segments.Count);
        Assert.Equal("x", selection.Segments[0]);
    }

    [Fact]
    public void An_executable_or_template_with_a_member_read_derives_the_member_selection_requirement_and_one_without_does_not()
    {
        var requirement = new RuntimeRequirement("elsa.runtime.input-member-selection", "1");

        Assert.Contains(requirement, Executable(Member("result", Deferred(["id"]))).RuntimeRequirements);
        Assert.Contains(requirement, Template(Member("variable", Deferred(["id"]))).RuntimeRequirements);
        Assert.DoesNotContain(requirement, Executable(WholeResult()).RuntimeRequirements);
        Assert.DoesNotContain(requirement, Template(WholeResult()).RuntimeRequirements);
    }

    /// <summary>No composition advertises member selection in this slice, so the shared checker reports it missing.</summary>
    [Fact]
    public void No_runtime_composition_advertises_member_selection_so_a_member_executable_is_not_runnable()
    {
        using var provider = new ServiceCollection().AddWorkflowRuntime().BuildServiceProvider();
        var capabilities = provider.GetServices<IRuntimeActivityConsumerCapability>().ToArray();
        Assert.DoesNotContain(capabilities, capability => capability.ConsumerKey == RuntimeReadSelection.RequirementKey);

        var checker = new RuntimeRequirementChecker(capabilities,
            new RuntimeDurableValueStorageDriverRegistry([new JsonRuntimeDurableValueStorageDriver()]),
            // The test node is not a CLR activity, so the checker never resolves a CLR alias or reads a CLR descriptor.
            typeRegistry: null!, payloadSerializer: null!);
        var check = checker.Check(RuntimeRequirementCheckSubject.FromExecutable(Executable(Member("result", Deferred(["id"])))));

        Assert.False(check.IsSatisfied);
        var entry = Assert.Single(check.Requirements, item => item.ConsumerKey == RuntimeReadSelection.RequirementKey);
        Assert.Equal(("1", RuntimeRequirementStatus.Missing), (entry.SchemaVersion, entry.Status));
    }

    [Theory]
    [MemberData(nameof(MemberKinds))]
    public void The_built_in_resolver_refuses_a_member_read_instead_of_reading_the_whole_source(string kind)
    {
        var binding = Member(kind, Deferred(["id"]));

        var exception = Assert.Throws<NotSupportedException>(() =>
            new RuntimeInputBindingResolver().Resolve(binding, new RuntimeInputBindingResolutionContext("workflow", "activity")));

        Assert.Contains("does not support member selection", exception.Message, StringComparison.Ordinal);
        Assert.Contains("elsa.runtime.input-member-selection@1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_member_executable_still_verifies_its_own_hash_and_is_never_matched_as_a_historical_one()
    {
        var hasher = new WorkflowExecutableHasher();
        var node = Executable(Member("result", Deferred(["id"]))).RootActivity;
        var contract = new WorkflowExecutableInputContract(WorkflowExecutableInputContract.CurrentVersion, []);
        var hash = hasher.ComputeHash(node, contract, []);
        var executable = new WorkflowExecutable(new WorkflowExecutableIdentity("artifact-member", "definition", "version", "1", hash),
            node, new Dictionary<string, WorkflowExecutableResumeTarget>(), DateTimeOffset.UnixEpoch, new Dictionary<string, string>(),
            contract, [], IncidentStrategyBuiltIns.FaultReference);

        Assert.True(hasher.MatchesHash(executable));
    }

    internal static RuntimeReadSelection Known(string[] segments) =>
        new(1, segments, RuntimeReadSelectionMode.Known, Text, mayBeAbsent: false, mayBeNull: true, ValueRepresentation.TextValue, "elsa.activity-value-json/1");

    internal static RuntimeReadSelection Deferred(string[] segments) => new(1, segments, RuntimeReadSelectionMode.DeferredFit);

    internal static RuntimeInputBinding Member(string kind, RuntimeReadSelection selection) => kind switch
    {
        "request" => Binding(RuntimeInputBindingSource.WorkflowRequestMember, selection, workflowRequest: new RuntimeWorkflowRequestReference("customer")),
        "variable" => Binding(RuntimeInputBindingSource.VariableMember, selection, variable: new RuntimeVariableReference("customer", "workflow")),
        "result" => Binding(RuntimeInputBindingSource.ActivityResultMember, selection,
            activityResult: new RuntimeActivityResultReference("producer", "$result", producerScopeId: null)),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    private static RuntimeInputBinding WholeResult() => new("text", Text, ValueProtectionPolicy.InstanceInline,
        RuntimeInputBindingSource.ActivityResult, activityResult: new RuntimeActivityResultReference("producer", "$result", producerScopeId: null));

    private static RuntimeInputBinding Binding(
        RuntimeInputBindingSource source,
        RuntimeReadSelection? selection,
        ValueEnvelope? literal = null,
        RuntimeWorkflowRequestReference? workflowRequest = null,
        RuntimeVariableReference? variable = null,
        RuntimeActivityResultReference? activityResult = null) =>
        new("text", Text, ValueProtectionPolicy.InstanceInline, source, selection, literal, workflowRequest, variable, activityResult);

    private static ExecutableNode Node(RuntimeInputBinding binding) => new(
        "node-consumer", "consumer", "Tests.Consumer", "1.0.0", "test", JsonSerializer.SerializeToElement(new { }),
        new Dictionary<string, RuntimeInputBinding> { [binding.InputName] = binding }, new Dictionary<string, string>());

    private static WorkflowExecutable Executable(RuntimeInputBinding binding) => new(
        new WorkflowExecutableIdentity("artifact", "definition", "version", "1", "hash"), Node(binding),
        new Dictionary<string, WorkflowExecutableResumeTarget>(), DateTimeOffset.UnixEpoch, new Dictionary<string, string>(),
        IncidentStrategyBuiltIns.FaultReference);

    private static ExecutableActivityTemplate Template(RuntimeInputBinding binding) => new(
        "template", "template-hash", Node(binding), new Dictionary<string, WorkflowExecutableResumeTarget>(), [], [], [], "provider",
        new Dictionary<string, string>(), DateTimeOffset.UnixEpoch);
}
