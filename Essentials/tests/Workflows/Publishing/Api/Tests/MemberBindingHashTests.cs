using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// Every behavioural field of a member read (#4078 slice A) moves each identity that covers bindings: the node payload
/// hash, the behavioural executable hash, the template behaviour hash and the input-snapshot binding fingerprint.
/// Whole-value identities are pinned unchanged in <see cref="WholeValueBindingHashTests"/>.
/// </summary>
public sealed class MemberBindingHashTests
{
    private static readonly ValueTypeDescriptor Text = new("String");

    private static RuntimeReadSelection Selection(
        string[]? segments = null,
        RuntimeReadSelectionMode mode = RuntimeReadSelectionMode.Known,
        ValueTypeDescriptor? type = null,
        bool? mayBeAbsent = false,
        bool? mayBeNull = false,
        ValueRepresentation? representation = ValueRepresentation.TextValue,
        string? profile = "elsa.activity-value-json/1") =>
        mode == RuntimeReadSelectionMode.Known
            ? new(1, segments ?? ["customer", "id"], mode, type ?? Text, mayBeAbsent, mayBeNull, representation, profile)
            : new(1, segments ?? ["customer", "id"], mode, representation: representation, profileRevision: profile);

    private static RuntimeInputBinding Result(RuntimeReadSelection? selection = null, string producer = "producer", string projection = "$result", bool isOptional = false) =>
        new("text", Text, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.ActivityResultMember, selection ?? Selection(),
            activityResult: new RuntimeActivityResultReference(producer, projection, producerScopeId: null, isOptional));

    private static RuntimeInputBinding Request(string memberKey = "customer", RuntimeReadSelection? selection = null) =>
        new("text", Text, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.WorkflowRequestMember, selection ?? Selection(),
            workflowRequest: new RuntimeWorkflowRequestReference(memberKey));

    private static RuntimeInputBinding Variable(string scope = "workflow", string key = "customer", RuntimeReadSelection? selection = null) =>
        new("text", Text, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.VariableMember, selection ?? Selection(),
            variable: new RuntimeVariableReference(key, scope));

    public static TheoryData<string> Variants => new()
    {
        "producer", "projection", "optional", "first-segment", "last-segment", "segment-boundary", "extra-segment",
        "mode", "selected-type", "may-be-absent", "may-be-null", "representation", "profile-revision",
        "request", "request-key", "variable", "variable-scope", "variable-key", "scope-key-boundary", "whole-value"
    };

    /// <summary>The baseline read, against which each variant differs in exactly one field.</summary>
    private static RuntimeInputBinding Baseline(string variant) => variant switch
    {
        "request-key" => Request(),
        "variable-scope" or "variable-key" => Variable(),
        "scope-key-boundary" => Variable(scope: "a", key: "bc"),
        _ => Result()
    };

    private static RuntimeInputBinding Variant(string variant) => variant switch
    {
        "producer" => Result(producer: "other-producer"),
        "projection" => Result(projection: "customer"),
        "optional" => Result(isOptional: true),
        "first-segment" => Result(Selection(["client", "id"])),
        "last-segment" => Result(Selection(["customer", "Id"])),
        "segment-boundary" => Result(Selection(["custome", "rid"])),
        "extra-segment" => Result(Selection(["customer", "id", ""])),
        "mode" => Result(Selection(mode: RuntimeReadSelectionMode.DeferredFit)),
        "selected-type" => Result(Selection(type: new ValueTypeDescriptor("Int32"))),
        "may-be-absent" => Result(Selection(mayBeAbsent: true)),
        "may-be-null" => Result(Selection(mayBeNull: null)),
        "representation" => Result(Selection(representation: ValueRepresentation.StructuredValue)),
        "profile-revision" => Result(Selection(profile: "elsa.activity-value-json/2")),
        "request" => Request(),
        "request-key" => Request(memberKey: "client"),
        "variable" => Variable(),
        "variable-scope" => Variable(scope: "sequence"),
        "variable-key" => Variable(key: "client"),
        "scope-key-boundary" => Variable(scope: "ab", key: "c"),
        "whole-value" => new("text", Text, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.ActivityResult,
            activityResult: new RuntimeActivityResultReference("producer", "$result", producerScopeId: null)),
        _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, null)
    };

    [Theory]
    [MemberData(nameof(Variants))]
    public void A_single_field_change_moves_the_node_payload_hash(string variant) =>
        Assert.NotEqual(NodePayload(Baseline(variant)), NodePayload(Variant(variant)));

    [Theory]
    [MemberData(nameof(Variants))]
    public void A_single_field_change_moves_the_behavioural_executable_hash(string variant) =>
        Assert.NotEqual(Behavioural(Baseline(variant)), Behavioural(Variant(variant)));

    [Theory]
    [MemberData(nameof(Variants))]
    public void A_single_field_change_moves_the_template_behaviour_hash(string variant) =>
        Assert.NotEqual(ScopedActivityResultHashTests.TemplateHash(Node(Baseline(variant))),
            ScopedActivityResultHashTests.TemplateHash(Node(Variant(variant))));

    [Theory]
    [MemberData(nameof(Variants))]
    public void A_single_field_change_moves_the_input_snapshot_binding_fingerprint(string variant) =>
        Assert.NotEqual(WholeValueBindingHashTests.BindingFingerprint(Node(Baseline(variant))),
            WholeValueBindingHashTests.BindingFingerprint(Node(Variant(variant))));

    [Fact]
    public void The_same_member_read_hashes_the_same()
    {
        Assert.Equal(NodePayload(Result()), NodePayload(Result()));
        Assert.Equal(Behavioural(Result()), Behavioural(Result()));
    }

    /// <summary>
    /// No constructor or reader admits an unknown source, so the binding is made past its constructor here. The node
    /// payload hash refuses it instead of rendering it as an absent literal that any other such binding would share.
    /// </summary>
    [Fact]
    public void The_node_payload_hash_refuses_an_unknown_source()
    {
        var binding = (RuntimeInputBinding)RuntimeHelpers.GetUninitializedObject(typeof(RuntimeInputBinding));
        Set(binding, nameof(RuntimeInputBinding.InputName), "text");
        Set(binding, nameof(RuntimeInputBinding.TargetType), Text);
        Set(binding, nameof(RuntimeInputBinding.EffectivePolicy), ValueProtectionPolicy.InstanceInline);
        Set(binding, nameof(RuntimeInputBinding.Metadata), new Dictionary<string, string>());
        Set(binding, nameof(RuntimeInputBinding.Source), (RuntimeInputBindingSource)99);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => NodePayload(binding));
        Assert.Contains("does not render", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The conversion linker plans result member reads only; a request member read, which publication does not
    /// compile, is refused rather than passed through unplanned.
    /// </summary>
    [Fact]
    public void The_conversion_linker_refuses_a_request_member_read()
    {
        var exception = Assert.Throws<NotSupportedException>(() =>
            new Elsa.Workflows.Publishing.Services.ActivityResultConversionPlanLinker(new Elsa.Workflows.Publishing.Services.ValueConversionPlanResolver())
                .Link(Node(Request())));

        Assert.Contains("publication does not compile member reads yet", exception.Message, StringComparison.Ordinal);
    }

    private static void Set(object target, string property, object value) =>
        target.GetType().GetField($"<{property}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private static string NodePayload(RuntimeInputBinding binding) => new WorkflowExecutableHasher().ComputeHash(Node(binding));

    private static string Behavioural(RuntimeInputBinding binding) => new WorkflowExecutableHasher().ComputeHash(Node(binding),
        new WorkflowExecutableInputContract(WorkflowExecutableInputContract.CurrentVersion, []), []);

    private static ExecutableNode Node(RuntimeInputBinding binding) =>
        WholeValueBindingHashTests.Node(new Dictionary<string, RuntimeInputBinding> { [binding.InputName] = binding });
}
