using System.Text.Json;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Resolvers;
using Elsa.Workflows.Runtime.Services.Values;
using Elsa.Workflows.Runtime.Services.WorkHandlers;
using Elsa.Workflows.Runtime.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// The real input materializer reading a member (#4078 slice B) of a workflow request member or a variable, inline and
/// externally stored: the snapshot holds exactly the selected member, failures carry their VF-SEL code before any
/// snapshot exists, protection never falls below the whole source's, and the capability and the front-door guard
/// follow the composed materializer.
/// </summary>
public sealed class MemberReadMaterializationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly ValueTypeDescriptor Text = new("String");
    private static readonly ValueTypeDescriptor Number = new("Int32");
    private static readonly ValueTypeDescriptor Customer = new("Customer");
    private static readonly JsonElement CustomerJson = JsonSerializer.SerializeToElement(new
    {
        customerId = "c-1", count = 3, address = new { street = "Main", zip = (string?)null }, tags = new[] { "x" }
    });
    private static readonly DurableValueExternalReference CustomerReference = new("external", "customers/c-1", new Dictionary<string, string>());

    public static TheoryData<string, bool> Roles => new() { { "request", false }, { "request", true }, { "variable", false }, { "variable", true } };

    [Theory]
    [MemberData(nameof(Roles))]
    public async Task The_snapshot_records_exactly_the_selected_member(string role, bool external)
    {
        var snapshot = await MaterializeAsync(Member(role, ["address", "street"]), Context(role, external), Store());

        var value = snapshot.Values["message"];
        Assert.Equal(ValuePresence.Present, value.Presence);
        Assert.Equal("Main", value.InlineValue!.Value.GetString());
        Assert.Equal("String", value.Type.Alias);
    }

    [Theory]
    [MemberData(nameof(Roles))]
    public async Task A_known_and_a_deferred_selection_select_alike(string role, bool external)
    {
        var known = await MaterializeAsync(Member(role, ["customerId"], known: true), Context(role, external), Store());
        var deferred = await MaterializeAsync(Member(role, ["customerId"]), Context(role, external), Store());

        Assert.Equal("c-1", known.Values["message"].InlineValue!.Value.GetString());
        Assert.Equal("c-1", deferred.Values["message"].InlineValue!.Value.GetString());
        Assert.NotEqual(known.BindingFingerprint, deferred.BindingFingerprint);
    }

    public static TheoryData<string[], string, bool, bool> Failures => new()
    {
        // segments, code, input required, input nullable
        { ["missing"], "VF-SEL-001", true, true },
        { ["missing"], "VF-SEL-001", false, false },
        { ["address", "zip"], "VF-SEL-002", true, false },
        { ["address", "zip", "deeper"], "VF-SEL-002", false, false },
        { ["tags", "0"], "VF-SEL-003", true, false },
        { ["customerId", "length"], "VF-SEL-003", true, false },
        { ["address"], "VF-SEL-004", true, false },
        { ["count"], "VF-SEL-004", true, false }
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task A_failed_member_read_is_refused_with_its_code_before_any_snapshot(string[] segments, string code, bool required, bool nullable)
    {
        var exception = await Assert.ThrowsAsync<RuntimeReadSelectionException>(() =>
            MaterializeAsync(Member("variable", segments), Context("variable", external: false), Store(), required, nullable).AsTask());

        Assert.Equal(code, exception.Code);
        Assert.StartsWith(code + ": ", exception.Message, StringComparison.Ordinal);
        Assert.Equal("message", exception.InputKey);
        Assert.DoesNotContain("c-1", exception.Message, StringComparison.Ordinal);
        if (segments is ["customerId", "length"])
            Assert.Contains("is looked up in a string, which has no members", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_member_whose_json_does_not_fit_the_input_type_is_refused_even_under_an_identity_plan()
    {
        var binding = Member("variable", ["customerId"], known: true, inputType: Number,
            plan: ValueConversionPlan.Identity(Number, ValueRepresentation.TypedValue));

        var exception = await Assert.ThrowsAsync<RuntimeReadSelectionException>(() =>
            MaterializeAsync(binding, Context("variable", external: false), Store(), inputType: Number).AsTask());

        Assert.Equal(RuntimeReadSelectionException.Mismatch, exception.Code);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task An_absent_or_null_member_of_an_optional_nullable_input_reads_as_no_value(bool absent, bool nullable)
    {
        var snapshot = await MaterializeAsync(Member("variable", absent ? ["missing"] : ["address", "zip"]), Context("variable", external: false),
            Store(), required: false, nullable: nullable);

        Assert.Equal(absent ? ValuePresence.Absent : ValuePresence.ExplicitNull, snapshot.Values["message"].Presence);
    }

    [Fact]
    public async Task A_member_keeps_the_protection_of_its_source()
    {
        var sensitive = new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true, redactionMode: "Full");
        var context = new RuntimeInputBindingResolutionContext("workflow", "invocation-1",
            variableEnvelopes: new Dictionary<RuntimeVariableValueAddress, ValueEnvelope>
            {
                [new("workflow", "customer")] = ValueEnvelope.Inline(Customer, CustomerJson, sensitive)
            });

        var snapshot = await MaterializeAsync(Member("variable", ["customerId"]), context, Store());

        Assert.True(snapshot.Values["message"].Policy.IsSensitive);
        Assert.Equal("Full", snapshot.Values["message"].Policy.RedactionMode);
    }

    [Fact]
    public async Task A_member_of_a_withheld_source_is_refused_as_the_whole_read_is()
    {
        var context = new RuntimeInputBindingResolutionContext("workflow", "invocation-1",
            variableEnvelopes: new Dictionary<RuntimeVariableValueAddress, ValueEnvelope>
            {
                [new("workflow", "customer")] = WithheldValues.Secret(Customer)
            });

        var whole = await Assert.ThrowsAsync<WithheldValueException>(() => MaterializeAsync(WholeVariable(), context, Store()).AsTask());
        var member = await Assert.ThrowsAsync<WithheldValueException>(() => MaterializeAsync(Member("variable", ["customerId"]), context, Store()).AsTask());

        Assert.Equal(whole.Message, member.Message);
    }

    [Fact]
    public async Task A_member_of_a_source_that_requires_encryption_is_never_selected()
    {
        var encrypted = new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true, requiresEncryption: true);
        var context = new RuntimeInputBindingResolutionContext("workflow", "invocation-1",
            variableEnvelopes: new Dictionary<RuntimeVariableValueAddress, ValueEnvelope>
            {
                [new("workflow", "customer")] = ValueEnvelope.Inline(Customer, CustomerJson, encrypted)
            });

        var exception = await Assert.ThrowsAsync<RuntimeReadSelectionException>(() =>
            MaterializeAsync(Member("variable", ["customerId"]), context, Store()).AsTask());

        Assert.Equal(RuntimeReadSelectionException.NotSelectable, exception.Code);
        Assert.Contains("requires encryption", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_replaceable_resolver_never_receives_a_member_read()
    {
        var resolver = new RecordingResolver();
        var materializer = new RuntimeActivityInputMaterializer(resolver, new TypeRegistry(), Store());

        _ = materializer.MaterializeSnapshotAsync(Node(Member("request", ["customerId"])), "invocation-1", Context("request", external: false), Now)
            .AsTask().GetAwaiter().GetResult();

        var seen = Assert.Single(resolver.Seen);
        Assert.Equal(RuntimeInputBindingSource.WorkflowRequest, seen.Source);
        Assert.Null(seen.Selection);
        Assert.Null(seen.WorkflowRequest!.Path);
        // The derived whole-value read is of the source, so it carries the neutral Elsa.Any, not the member's type.
        Assert.Equal(new ValueTypeDescriptor("Elsa.Any"), seen.TargetType);
    }

    [Theory]
    [InlineData(new[] { "tags" }, null)]
    [InlineData(new[] { "customerId" }, "VF-SEL-004")]
    [InlineData(new[] { "address" }, "VF-SEL-004")]
    public async Task A_member_read_into_an_Elsa_Any_collection_is_fitted_before_the_snapshot(string[] segments, string? code)
    {
        var anyList = new ValueTypeDescriptor("Elsa.Any", CollectionKind.List);
        var binding = Member("variable", segments, inputType: anyList);

        if (code is null)
        {
            var snapshot = await MaterializeAsync(binding, Context("variable", external: false), Store(), inputType: anyList);
            Assert.Equal(JsonValueKind.Array, snapshot.Values["message"].InlineValue!.Value.ValueKind);
            return;
        }

        var exception = await Assert.ThrowsAsync<RuntimeReadSelectionException>(() =>
            MaterializeAsync(binding, Context("variable", external: false), Store(), inputType: anyList).AsTask());
        Assert.Equal(code, exception.Code);
    }

    [Fact]
    public void One_fit_failure_predicate_classifies_every_conversion_failure_and_nothing_else()
    {
        Assert.All(
            new Exception[] { new JsonException(), new NotSupportedException(), new InvalidOperationException(), new ArgumentException(), new FormatException(), new OverflowException() },
            exception => Assert.True(RuntimeReadSelectionException.IsFitFailure(exception), exception.GetType().Name));
        Assert.False(RuntimeReadSelectionException.IsFitFailure(new OperationCanceledException()));
        Assert.False(RuntimeReadSelectionException.IsFitFailure(new IOException()));
    }

    [Fact]
    public async Task A_member_read_into_a_single_Elsa_Any_accepts_any_member()
    {
        var any = new ValueTypeDescriptor("Elsa.Any");

        var snapshot = await MaterializeAsync(Member("variable", ["address"], inputType: any), Context("variable", external: false), Store(), inputType: any);

        Assert.Equal("Main", snapshot.Values["message"].InlineValue!.Value.GetProperty("street").GetString());
    }

    [Theory]
    [InlineData(true, false, false, null)]
    [InlineData(false, false, true, null)]
    [InlineData(false, true, false, "VF-SEL-001")]
    [InlineData(false, false, false, "VF-SEL-002")]
    public async Task An_absent_or_null_source_that_requires_encryption_reports_as_absent_or_null(bool absent, bool required, bool nullable, string? code)
    {
        // Parity with the whole-value read: an absent or null source has no member, so its protection does not turn
        // it into VF-SEL-003; only a source holding a value is refused for requiring encryption.
        var encrypted = new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true, requiresEncryption: true);
        var source = absent || required ? ValueEnvelope.Absent(Customer, encrypted) : ValueEnvelope.Null(Customer, encrypted);
        var context = new RuntimeInputBindingResolutionContext("workflow", "invocation-1",
            variableEnvelopes: new Dictionary<RuntimeVariableValueAddress, ValueEnvelope> { [new("workflow", "customer")] = source });

        if (code is null)
        {
            var snapshot = await MaterializeAsync(Member("variable", ["customerId"]), context, Store(), required: false, nullable: true);
            Assert.Equal(absent ? ValuePresence.Absent : ValuePresence.ExplicitNull, snapshot.Values["message"].Presence);
            return;
        }

        var exception = await Assert.ThrowsAsync<RuntimeReadSelectionException>(() =>
            MaterializeAsync(Member("variable", ["customerId"]), context, Store(), required, nullable).AsTask());
        Assert.Equal(code, exception.Code);
    }

    /// <summary>The fingerprint renders selections in the hasher's framed format; these bytes are new and pinned.</summary>
    [Fact]
    public async Task The_binding_fingerprint_of_a_member_read_is_pinned()
    {
        var snapshot = await MaterializeAsync(Member("variable", ["address", "street"]), Context("variable", external: false), Store());

        Assert.Equal(ExpectedMemberFingerprint, snapshot.BindingFingerprint);
    }

    // Computed on this slice's first build of the framed rendering; it must never change.
    internal const string ExpectedMemberFingerprint = "sha256:cedd66e95b724715ae7cce2ff05715cbf8842bb1f2aa568587d045b1e3c353f4";

    [Fact]
    public void A_request_or_variable_member_needs_no_activity_history_and_a_result_member_does()
    {
        var materializer = new RuntimeActivityInputMaterializer(new RuntimeInputBindingResolver(), new TypeRegistry(), Store());

        Assert.False(materializer.RequiresActivityHistory(Node(Member("request", ["id"]))));
        Assert.False(materializer.RequiresActivityHistory(Node(Member("variable", ["id"]))));
        Assert.True(materializer.RequiresActivityHistory(Node(new RuntimeInputBinding("message", Text, ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.ActivityResultMember, Deferred(["id"]),
            activityResult: new RuntimeActivityResultReference("producer", "$result", producerScopeId: null)))));
        Assert.False(materializer.UsesExecutionIndependentBindings(Node(Member("request", ["id"]))));
    }

    [Fact]
    public void The_runtime_advertises_member_selection_only_while_its_own_materializer_is_composed()
    {
        Assert.Equal(["1"], Capability(replaceMaterializer: false));
        Assert.Empty(Capability(replaceMaterializer: true));
    }

    public static TheoryData<string> Replacements => new() { "type", "factory", "instance", "decorator" };

    /// <summary>
    /// The decision is read from the registration: a factory, an instance or a decorator counts as replaced, even one
    /// that produces the built-in materializer, because what it produces is unknown without running it.
    /// </summary>
    [Theory]
    [MemberData(nameof(Replacements))]
    public void A_replaced_materializer_registration_advertises_nothing(string replacement)
    {
        var services = Services();
        switch (replacement)
        {
            case "type":
                services.AddScoped<IRuntimeActivityInputMaterializer, HostMaterializer>();
                break;
            case "factory":
                services.AddScoped<IRuntimeActivityInputMaterializer>(provider => ActivatorUtilities.CreateInstance<RuntimeActivityInputMaterializer>(provider));
                break;
            case "instance":
                services.AddSingleton<IRuntimeActivityInputMaterializer>(new HostMaterializer());
                break;
            case "decorator":
                services.AddScoped<IRuntimeActivityInputMaterializer>(provider =>
                    new DecoratingMaterializer(ActivatorUtilities.CreateInstance<RuntimeActivityInputMaterializer>(provider)));
                break;
        }

        using var provider = services.BuildServiceProvider();
        Assert.Empty(MemberSelection(provider).SupportedSchemaVersions);
    }

    [Fact]
    public void Reading_the_capability_resolves_nothing()
    {
        var resolutions = 0;
        var services = Services();
        services.AddScoped<IRuntimeActivityInputMaterializer>(_ =>
        {
            resolutions++;
            throw new InvalidOperationException("The capability must not construct the materializer.");
        });

        using var provider = services.BuildServiceProvider();
        Assert.Empty(MemberSelection(provider).SupportedSchemaVersions);
        Assert.Equal(0, resolutions);

        // And the built-in registration is advertised from the registrations alone, with no provider at all.
        Assert.Equal(["1"], new InputMemberSelectionCapability(Services()).SupportedSchemaVersions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_front_door_guard_refuses_a_member_executable_only_without_the_capability(bool replaceMaterializer)
    {
        using var provider = Provider(replaceMaterializer);
        using var scope = provider.CreateScope();
        var member = Executable(Member("request", ["id"]));
        var whole = Executable(WholeVariable());

        SchedulerWorkHandlerHelpers.EnsureMemberSelectionSupported(whole, scope.ServiceProvider);
        if (!replaceMaterializer)
        {
            SchedulerWorkHandlerHelpers.EnsureMemberSelectionSupported(member, scope.ServiceProvider);
            return;
        }

        var exception = Assert.Throws<InvalidOperationException>(() => SchedulerWorkHandlerHelpers.EnsureMemberSelectionSupported(member, scope.ServiceProvider));
        Assert.Contains("elsa.runtime.input-member-selection@1", exception.Message, StringComparison.Ordinal);
        Assert.Contains("none of it runs here", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_runtime_without_a_requirement_checker_refuses_a_member_executable()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            SchedulerWorkHandlerHelpers.EnsureMemberSelectionSupported(Executable(Member("request", ["id"])), new ServiceCollection().BuildServiceProvider()));

        Assert.Contains("no runtime requirement checker is composed", exception.Message, StringComparison.Ordinal);
    }

    private static IReadOnlyCollection<string> Capability(bool replaceMaterializer)
    {
        using var provider = Provider(replaceMaterializer);
        return MemberSelection(provider).SupportedSchemaVersions;
    }

    private static IRuntimeActivityConsumerCapability MemberSelection(IServiceProvider provider) =>
        provider.GetServices<IRuntimeActivityConsumerCapability>().Single(capability => capability.ConsumerKey == RuntimeReadSelection.RequirementKey);

    private static ServiceProvider Provider(bool replaceMaterializer)
    {
        var services = Services();
        if (replaceMaterializer)
            services.AddScoped<IRuntimeActivityInputMaterializer, HostMaterializer>();
        return services.BuildServiceProvider();
    }

    private static IServiceCollection Services()
    {
        var services = new ServiceCollection().AddWorkflowRuntime();
        services.AddLogging();
        services.AddSingleton<IWellKnownTypeRegistry, TypeRegistry>();
        services.AddSingleton<IPayloadSerializer, NoPayloadSerializer>();
        return services;
    }

    private static ValueTask<ActivityInputSnapshot> MaterializeAsync(
        RuntimeInputBinding binding,
        RuntimeInputBindingResolutionContext context,
        IExternalPayloadStore store,
        bool required = true,
        bool nullable = false,
        ValueTypeDescriptor? inputType = null) =>
        new RuntimeActivityInputMaterializer(new RuntimeInputBindingResolver(), new TypeRegistry(), store)
            .MaterializeSnapshotAsync(Node(binding, required, nullable, inputType), "invocation-1", context, Now);

    private static RuntimeReadSelection Deferred(string[] segments) => new(1, segments, RuntimeReadSelectionMode.DeferredFit);

    private static RuntimeInputBinding Member(string role, string[] segments, bool known = false, ValueTypeDescriptor? inputType = null,
        ValueConversionPlan? plan = null)
    {
        var selection = known
            ? new RuntimeReadSelection(1, segments, RuntimeReadSelectionMode.Known, plan?.SourceType ?? Text, mayBeAbsent: false, mayBeNull: false,
                ValueRepresentation.TextValue, "elsa.activity-value-json/1")
            : Deferred(segments);
        return role == "request"
            ? new RuntimeInputBinding("message", inputType ?? Text, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.WorkflowRequestMember,
                selection, workflowRequest: new RuntimeWorkflowRequestReference("customer"), conversionPlan: plan)
            : new RuntimeInputBinding("message", inputType ?? Text, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.VariableMember,
                selection, variable: new RuntimeVariableReference("customer", "workflow"), conversionPlan: plan);
    }

    private static RuntimeInputBinding WholeVariable() => new("message", Text, ValueProtectionPolicy.InstanceInline,
        RuntimeInputBindingSource.VariableRead, variable: new RuntimeVariableReference("customer", "workflow"));

    private static RuntimeInputBindingResolutionContext Context(string role, bool external)
    {
        var envelope = external
            ? ValueEnvelope.External(Customer, CustomerReference, ValueProtectionPolicy.InstanceInline)
            : ValueEnvelope.Inline(Customer, CustomerJson, ValueProtectionPolicy.InstanceInline);
        return role == "request"
            ? new RuntimeInputBindingResolutionContext("workflow", "invocation-1",
                workflowInputEnvelopes: new Dictionary<string, ValueEnvelope> { ["customer"] = envelope })
            : new RuntimeInputBindingResolutionContext("workflow", "invocation-1",
                variableEnvelopes: new Dictionary<RuntimeVariableValueAddress, ValueEnvelope> { [new("workflow", "customer")] = envelope });
    }

    private static RecordingExternalPayloadStore Store() => new(new Dictionary<string, JsonElement> { [CustomerReference.Locator] = CustomerJson });

    private static ExecutableNode Node(RuntimeInputBinding binding, bool required = true, bool nullable = false, ValueTypeDescriptor? inputType = null)
    {
        using var descriptor = JsonDocument.Parse("""{"type":"test"}""");
        var contract = new ActivityContract("test/activity", "1.0.0", "test", descriptor.RootElement,
            [new ActivityInputContract("message", "Message", inputType ?? binding.TargetType, required, nullable, false, null, ActivityValuePolicy.Default)],
            new ActivityResultContract(new ValueTypeDescriptor("Elsa.Unit"), true, ActivityValuePolicy.Default, []),
            ["Done"], new ActivityActivationRequirement("test", "test/activity"));
        return new ExecutableNode("node-1", "authored-node-1", "test/activity", "1.0.0", "test", descriptor.RootElement,
            new Dictionary<string, RuntimeInputBinding> { ["message"] = binding }, new Dictionary<string, string>(), activityContract: contract);
    }

    private static WorkflowExecutable Executable(RuntimeInputBinding binding) => new(
        new WorkflowExecutableIdentity("artifact", "definition", "version", "1", "hash"), Node(binding),
        new Dictionary<string, WorkflowExecutableResumeTarget>(), Now, new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference);

    private sealed class RecordingResolver : IRuntimeInputBindingResolver
    {
        private readonly RuntimeInputBindingResolver _inner = new();
        public List<RuntimeInputBinding> Seen { get; } = [];

        public RuntimeResolvedInput Resolve(RuntimeInputBinding binding, RuntimeInputBindingResolutionContext context)
        {
            Seen.Add(binding);
            return _inner.Resolve(binding, context);
        }
    }

    private sealed class HostMaterializer : IRuntimeActivityInputMaterializer
    {
        public ValueTask<ActivityInputSnapshot> MaterializeSnapshotAsync(ExecutableNode node, string invocationId,
            RuntimeInputBindingResolutionContext resolutionContext, DateTimeOffset materializedAt, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("A host materializer is never asked to read a member.");
    }

    private sealed class DecoratingMaterializer(IRuntimeActivityInputMaterializer inner) : IRuntimeActivityInputMaterializer
    {
        public ValueTask<ActivityInputSnapshot> MaterializeSnapshotAsync(ExecutableNode node, string invocationId,
            RuntimeInputBindingResolutionContext resolutionContext, DateTimeOffset materializedAt, CancellationToken cancellationToken = default) =>
            inner.MaterializeSnapshotAsync(node, invocationId, resolutionContext, materializedAt, cancellationToken);
    }

    private sealed class RecordingExternalPayloadStore(IReadOnlyDictionary<string, JsonElement> payloads) : IExternalPayloadStore
    {
        public ValueTask<DurableValueExternalReference> WriteAsync(ExternalPayloadWriteRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new DurableValueExternalReference(request.StorageProfile, $"payloads/{request.OwnerKey}", new Dictionary<string, string>()));

        public ValueTask<JsonElement> ReadAsync(DurableValueExternalReference reference, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(payloads[reference.Locator].Clone());
    }

    private sealed class TypeRegistry : IWellKnownTypeRegistry
    {
        private static readonly Dictionary<string, Type> Types = new(StringComparer.Ordinal)
        {
            ["String"] = typeof(string), ["Int32"] = typeof(int), ["Object"] = typeof(object), ["Customer"] = typeof(object)
        };

        public void RegisterType(Type type, string alias) => throw new NotSupportedException();
        public bool TryGetAlias(Type type, out string alias)
        {
            alias = Types.FirstOrDefault(item => item.Value == type).Key ?? type.FullName!;
            return Types.ContainsValue(type);
        }
        public bool TryGetType(string alias, out Type type) => TryGetTypeOrDefault(alias, out type);
        public IEnumerable<Type> ListTypes() => Types.Values;
        public string GetAliasOrDefault(Type type) => TryGetAlias(type, out var alias) ? alias : type.FullName!;
        public Type GetTypeOrDefault(string alias) => Types.GetValueOrDefault(alias, typeof(object));
        public bool TryGetTypeOrDefault(string alias, out Type type)
        {
            var found = Types.TryGetValue(alias, out var known);
            type = known ?? typeof(object);
            return found;
        }
    }

    private sealed class NoPayloadSerializer : IPayloadSerializer
    {
        public string Serialize(object payload) => throw new NotSupportedException();
        public JsonElement SerializeToElement(object payload) => throw new NotSupportedException();
        public object Deserialize(string serializedData) => throw new NotSupportedException();
        public object Deserialize(string serializedData, Type type) => throw new NotSupportedException();
        public object Deserialize(JsonElement serializedData) => throw new NotSupportedException();
        public T Deserialize<T>(string payload) => throw new NotSupportedException();
        public T Deserialize<T>(JsonElement payload) => throw new NotSupportedException();
        public JsonSerializerOptions GetOptions() => throw new NotSupportedException();
    }
}
