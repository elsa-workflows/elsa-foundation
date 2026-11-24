using Elsa.Workflows.Runtime.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Elsa.Serialization.Core;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Resolvers;
using Elsa.Workflows.Runtime.Services.Values;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Spec 1222 at input materialization (T007, T012, T016 to T018, T022): a pinned read loads only the variable it names;
/// a marked expression's computed reads are served by the recorded run-time lookup, which joins the input read set,
/// records absent reads with their warning, and faults the step for a transient or withheld match whatever the script
/// made of the error. The fake evaluator below stands in for the engine and reads through the request as a script would.
/// </summary>
public sealed class ScriptVariableReadMaterializationTests
{
    private static readonly ValueTypeDescriptor Any = new("Any");
    private static readonly DurableValueExternalReference LargeReference = new("blob", "payloads/large", new Dictionary<string, string>());

    private static readonly ValueEnvelope Order = Inline(new { total = 3 });
    private static readonly ValueEnvelope InnerOrder = Inline(new { total = 7 });
    private static readonly ValueEnvelope Large = ValueEnvelope.External(Any, LargeReference, ValueProtectionPolicy.InstanceInline);
    private static readonly ValueEnvelope Unset = ValueEnvelope.Absent(Any, ValueProtectionPolicy.InstanceInline);
    private static readonly ValueEnvelope Scratch = ValueEnvelope.Transient(Any, new object());

    // Visible from the consumer, nearest first: the container's order shadows the workflow's.
    private static readonly IReadOnlyList<RuntimeReadableVariable> Visible =
    [
        new("order", "container", "inner-order", InnerOrder),
        new("large", "workflow", "large", Large),
        new("unset", "workflow", "unset", Unset),
        new("scratch", "workflow", "scratch", Scratch)
    ];

    [Theory]
    [InlineData("Int32", "0")]
    [InlineData("Boolean", "false")]
    [InlineData("Acme.Status", "0")]
    [InlineData("Acme.Status", "null", true)]
    public async Task Pinned_and_computed_unassigned_scalar_reads_have_the_same_supported_type_default(string alias, string expected, bool? supportsNull = null)
    {
        var visible = new[] { new RuntimeReadableVariable("unset", "workflow", "unset", ValueEnvelope.Absent(new(alias), ValueProtectionPolicy.InstanceInline)) };
        foreach (var computed in new[] { false, true })
        {
            var snapshot = await MaterializeAsync(new Script(request => computed ? request.VariableReader!.Read("unset").Value : request.ParameterValues["variables.unset"]),
                pinned: computed ? null : ("unset", "workflow", "unset"), computed: computed, visible: visible, registry: new EnumRegistry(), descriptors: supportsNull.HasValue ? new EnumCatalog(supportsNull.Value) : null, useDi: supportsNull == true);
            Assert.Equal(ValuePresence.Absent, visible[0].Envelope.Presence);
            if (expected == "null") Assert.Equal(ValuePresence.ExplicitNull, snapshot.Values["value"].Presence);
            else Assert.Equal(expected, snapshot.Values["value"].InlineValue!.Value.GetRawText());
        }
    }

    [Fact]
    public async Task A_literal_only_expression_loads_only_the_variable_it_names()
    {
        // SC-007, FR-007, FR-013: the large external variable is visible but not named, so it is never read.
        var store = new CountingPayloadStore();

        var value = await EvaluateAsync(new Script(request => request.ParameterValues["variables.order"]), pinned: ("order", "container", "inner-order"), store: store);

        Assert.Equal(7, value.GetProperty("total").GetInt32());
        Assert.Equal(0, store.Reads);
    }

    [Fact]
    public async Task A_marked_expression_makes_the_visible_variables_readable_before_it_runs()
    {
        // Q5: the cost of a computed read, paid only by a marked expression.
        var store = new CountingPayloadStore();

        await EvaluateAsync(new Script(_ => JsonSerializer.SerializeToElement(1)), computed: true, store: store);

        Assert.Equal(1, store.Reads);
    }

    [Fact]
    public async Task An_external_value_that_cannot_be_read_blocks_only_a_script_that_reads_it()
    {
        var store = new CountingPayloadStore { Fails = true };

        var value = await EvaluateAsync(new Script(request => request.VariableReader!.Read("order").Value), computed: true, store: store);
        var exception = await Assert.ThrowsAsync<ExpressionInputFailureException>(() =>
            EvaluateAsync(new Script(request => { _ = request.VariableReader!.Read("large"); return JsonSerializer.SerializeToElement(1); }), computed: true, store: store).AsTask());

        Assert.Equal(7, value.GetProperty("total").GetInt32());
        Assert.Contains("variable 'large'", exception.InnerException!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_pinned_read_of_an_unset_variable_reads_null()
    {
        // FR-006.
        var value = await EvaluateAsync(new Script(request => request.ParameterValues["variables.unset"]), pinned: ("unset", "workflow", "unset"));

        Assert.Equal(JsonValueKind.Null, value.ValueKind);
    }

    [Fact]
    public async Task A_pinned_read_of_a_loop_value_the_pass_does_not_have_faults_naming_the_node_and_the_variable()
    {
        // FR-004a: an ExposeIndex expression resolved to false, so this pass has no currentIndex.
        var exception = await Assert.ThrowsAsync<ExpressionInputFailureException>(() =>
            EvaluateAsync(new Script(_ => JsonSerializer.SerializeToElement(1)), pinned: ("currentIndex", "loop", "currentIndex")).AsTask());

        Assert.Contains("'consumer'", exception.InnerException!.Message, StringComparison.Ordinal);
        Assert.Contains("unavailable variable 'currentIndex' in scope 'loop'", exception.InnerException.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_computed_read_resolves_nearest_scope_first_and_joins_the_input_read_set()
    {
        var recorder = new RuntimeVariableReadRecorder();

        var value = await EvaluateAsync(new Script(request => request.VariableReader!.Read("order").Value), computed: true, recorder: recorder);

        Assert.Equal(7, value.GetProperty("total").GetInt32());
        Assert.Same(InnerOrder, Assert.Single(recorder.Reads));
        var read = Assert.Single(recorder.ComputedReads);
        Assert.Equal(("order", "container", "inner-order"), (read.Name, read.Variable!.ScopeId, read.Variable.VariableKey));
    }

    [Fact]
    public async Task A_pinned_and_a_computed_read_of_one_variable_read_the_same_value_and_record_it_once()
    {
        // US2 scenario 7.
        var recorder = new RuntimeVariableReadRecorder();

        var value = await EvaluateAsync(
            new Script(request => JsonSerializer.SerializeToElement(
                request.ParameterValues["variables.order"].GetRawText() == request.VariableReader!.Read("order").Value.GetRawText())),
            pinned: ("order", "container", "inner-order"),
            computed: true,
            recorder: recorder);

        Assert.True(value.GetBoolean());
        Assert.Same(InnerOrder, Assert.Single(recorder.Reads));
    }

    [Fact]
    public async Task Candidates_the_script_never_reads_are_not_recorded()
    {
        var recorder = new RuntimeVariableReadRecorder();

        await EvaluateAsync(new Script(_ => JsonSerializer.SerializeToElement(1)), computed: true, recorder: recorder);

        Assert.Empty(recorder.Reads);
        Assert.Empty(recorder.ComputedReads);
    }

    [Fact]
    public async Task A_computed_name_that_matches_nothing_is_recorded_absent_with_a_warning()
    {
        // FR-011, spec 1216 FR-025/FR-026: the engine reads undefined; the snapshot keeps the absent read.
        var recorder = new RuntimeVariableReadRecorder();
        ExpressionVariableRead? seen = null;

        await EvaluateAsync(new Script(request => { seen = request.VariableReader!.Read("missing"); return JsonSerializer.SerializeToElement(1); }), computed: true, recorder: recorder);

        Assert.Equal(ExpressionVariableReadOutcome.Missing, seen!.Value.Outcome);
        Assert.Empty(recorder.Reads);
        var recorded = Assert.Single(recorder.ToComputedReads([])!);
        Assert.False(recorded.Present);
        Assert.Equal("missing", recorded.Name);
        Assert.StartsWith(VariableComputedRead.MissingNameWarning, recorded.Warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_computed_read_of_a_transient_variable_faults_naming_the_node_and_the_variable_even_when_caught()
    {
        // FR-017, FR-017a, ADR 0089 R3: the script below ignores the refusal; the host recorded it.
        var exception = await Assert.ThrowsAsync<ExpressionInputFailureException>(() =>
            EvaluateAsync(new Script(request => { _ = request.VariableReader!.Read("scratch"); return JsonSerializer.SerializeToElement("caught"); }), computed: true).AsTask());

        var cause = Assert.IsType<RuntimeVariableReadRefusedException>(exception.InnerException);
        Assert.Contains("'consumer'", cause.Message, StringComparison.Ordinal);
        Assert.Contains("variable 'scratch'", cause.Message, StringComparison.Ordinal);
        Assert.Contains("transient", cause.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_computed_read_taints_the_result_with_the_policy_of_the_variable_read()
    {
        // FR-019: a protected value read by a computed name makes the result protected.
        var secret = ValueEnvelope.Inline(Any, JsonSerializer.SerializeToElement("s3cr3t"), new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true));
        var visible = Visible.Append(new RuntimeReadableVariable("apiKey", "workflow", "apiKey", secret)).ToArray();

        var snapshot = await MaterializeAsync(new Script(request => request.VariableReader!.Read("apiKey").Value), computed: true, visible: visible);

        Assert.True(snapshot.Values["value"].Policy.IsSensitive);
    }

    [Fact]
    public void The_recorded_reads_carry_the_version_read_from_the_frame()
    {
        var recorder = new RuntimeVariableReadRecorder();
        var frame = new VariableFrameState("frame-1", "container", "activation-1", "root-frame", VariableFrameKind.Container,
            new Dictionary<string, ValueEnvelope> { ["inner-order"] = InnerOrder },
            versions: new Dictionary<string, VariableVersion> { ["inner-order"] = new(4, "writer") });
        recorder.RecordComputed(new RuntimeVariableComputedRead("order", Visible[0]));

        var read = Assert.Single(recorder.ToComputedReads([frame])!);

        Assert.Equal(("order", "container", "inner-order", 4L, true), (read.Name, read.ScopeId, read.VariableKey, read.Version, read.Present));
    }

    private static ValueTask<JsonElement> EvaluateAsync(
        Script script,
        (string Name, string ScopeId, string Key)? pinned = null,
        bool computed = false,
        CountingPayloadStore? store = null,
        RuntimeVariableReadRecorder? recorder = null) =>
        MaterializeAsync(script, pinned, computed, store, recorder).ContinueWithValue();

    private static async ValueTask<ActivityInputSnapshot> MaterializeAsync(
        Script script,
        (string Name, string ScopeId, string Key)? pinned = null,
        bool computed = false,
        CountingPayloadStore? store = null,
        RuntimeVariableReadRecorder? recorder = null,
        IReadOnlyList<RuntimeReadableVariable>? visible = null, IWellKnownTypeRegistry? registry = null, IRuntimeTypeNullability? descriptors = null, bool useDi = false)
    {
        visible ??= Visible;
        var parameters = new Dictionary<string, ExpressionParameterBinding>(StringComparer.Ordinal);
        if (pinned is { } read)
            parameters[ExpressionParameterSurfaces.VariablesKey(read.Name)] = new VariableExpressionParameterBinding(read.ScopeId, read.Key, ExpressionParameterSurfaces.Variables);
        var fallback = computed
            ? new VariableReadFallback(visible.Select(variable => new VariableReadCandidate(variable.Name, variable.ScopeId, variable.VariableKey)).ToArray())
            : null;
        var binding = new RuntimeInputBinding(
            "value",
            Any,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Expression,
            expression: new RuntimeExpressionBinding("JavaScript", "script", parameters: parameters, variableReadFallback: fallback));
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        services.AddSingleton<IWellKnownTypeRegistry>(registry ?? new EnumRegistry());
        services.AddSingleton<IPortableExpressionEvaluator>(script);
        if (descriptors is not null) services.AddSingleton<IRuntimeTypeNullability>(descriptors);
        using var runtimeOnly = services.BuildServiceProvider();
        Assert.Null(runtimeOnly.GetService<IVariableTypeDescriptorCatalog>());
        Assert.Null(runtimeOnly.GetService<IExternalPayloadStore>());
        IRuntimeActivityInputMaterializer materializer = useDi ? runtimeOnly.GetRequiredService<IRuntimeActivityInputMaterializer>() : new RuntimeActivityInputMaterializer(
            inputBindingResolver: new RuntimeInputBindingResolver(),
            structurePolicies: [],
            wellKnownTypeRegistry: registry,
            portableExpressionEvaluator: script, valueConversionExecutor: null,
            externalPayloadStore: store ?? new CountingPayloadStore(), typeDescriptors: descriptors);
        return await materializer.MaterializeSnapshotAsync(
            Consumer(binding),
            "consumer",
            new RuntimeInputBindingResolutionContext(
                "workflow-1",
                "consumer",
                variableEnvelopes: visible.ToDictionary(variable => new RuntimeVariableValueAddress(variable.ScopeId, variable.VariableKey), variable => variable.Envelope),
                visibleVariables: visible,
                variableReadRecorder: recorder ?? new RuntimeVariableReadRecorder()),
            DateTimeOffset.UnixEpoch);
    }

    private static ExecutableNode Consumer(RuntimeInputBinding binding)
    {
        var descriptor = JsonSerializer.SerializeToElement(new { type = "consumer" });
        var contract = new ActivityContract(
            "test/consumer",
            "1.0.0",
            "test",
            descriptor,
            [new ActivityInputContract("value", "Value", Any, true, true, false, null, ActivityValuePolicy.Default)],
            new ActivityResultContract(new ValueTypeDescriptor("Elsa.Unit"), true, ActivityValuePolicy.Default, []),
            ["Done"],
            new ActivityActivationRequirement("test", "test/consumer"));
        return new ExecutableNode(
            "consumer",
            "consumer",
            "test/consumer",
            "1.0.0",
            "test",
            descriptor,
            new Dictionary<string, RuntimeInputBinding> { ["value"] = binding },
            new Dictionary<string, string>(),
            activityContract: contract);
    }

    private static ValueEnvelope Inline(object value) =>
        ValueEnvelope.Inline(Any, JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline);

    /// <summary>A stand-in engine: runs the given function against the request, as a script reads its roots.</summary>
    private enum Status { Ready = 1 }
    private sealed class EnumRegistry : IWellKnownTypeRegistry
    {
        public void RegisterType(Type type, string alias) => throw new NotSupportedException();
        public bool TryGetAlias(Type type, out string alias) { alias = "Acme.Status"; return type == typeof(Status); }
        public bool TryGetType(string alias, out Type type) => TryGetTypeOrDefault(alias, out type);
        public IEnumerable<Type> ListTypes() => [typeof(Status)];
        public string GetAliasOrDefault(Type type) => type == typeof(Status) ? "Acme.Status" : type.FullName!;
        public Type GetTypeOrDefault(string alias) => alias == "Acme.Status" ? typeof(Status) : typeof(object);
        public bool TryGetTypeOrDefault(string alias, out Type type) { type = GetTypeOrDefault(alias); return type != typeof(object); }
    }

    private sealed class EnumCatalog(bool supportsNull) : IRuntimeTypeNullability
    {
        public bool TryGetSupportsNull(string alias, out bool value) { value = supportsNull; return StringComparer.OrdinalIgnoreCase.Equals(alias, "Acme.Status"); }
    }

    private sealed class Script(Func<ExpressionEvaluationRequest, JsonElement> run) : IPortableExpressionEvaluator
    {
        public ValueTask<JsonElement> EvaluateAsync(ExpressionEvaluationRequest request) => ValueTask.FromResult(run(request).Clone());
    }

    private sealed class CountingPayloadStore : IExternalPayloadStore
    {
        public int Reads { get; private set; }
        public bool Fails { get; init; }

        public ValueTask<DurableValueExternalReference> WriteAsync(ExternalPayloadWriteRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<JsonElement> ReadAsync(DurableValueExternalReference reference, CancellationToken cancellationToken = default)
        {
            Reads++;
            if (Fails)
                throw new InvalidOperationException("The payload store is unavailable.");
            return ValueTask.FromResult(JsonSerializer.SerializeToElement(new { size = "large" }));
        }
    }
}

internal static class SnapshotValueExtensions
{
    public static async ValueTask<JsonElement> ContinueWithValue(this ValueTask<ActivityInputSnapshot> snapshot) =>
        (await snapshot).Values["value"] is { InlineValue: { } value } ? value : JsonSerializer.SerializeToElement<object?>(null);
}
