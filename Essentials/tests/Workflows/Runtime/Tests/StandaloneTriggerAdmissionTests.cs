using Elsa.Events;
using Elsa.Events.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Triggers;
using Elsa.Workflows.Runtime.Tests.Fixtures;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>Application-level preparation checks. Physical acceptance and recovery use the provider suites.</summary>
public sealed class StandaloneTriggerAdmissionTests
{
    [Theory]
    [InlineData("RequireAll", 0)]
    [InlineData("AcceptAvailable", 1)]
    public async Task Host_start_policy_is_evaluated_on_resource_authority_before_acceptance(string policy, int expectedAdmissions)
    {
        using var fixture = new Fixture(policy, policyGate: new DenyOnePolicy());
        await fixture.Publish("a"); await fixture.Publish("blocked");
        await fixture.Save([Subscription("a"), Subscription("blocked")]);
        if (policy == "RequireAll")
        {
            var failure = await Assert.ThrowsAsync<StandaloneTriggerAdmissionRefusedException>(() => fixture.Admit().AsTask());
            Assert.Equal("target-start-policy-denied", Assert.Single(failure.Refusals).ReasonCode);
            Assert.Equal("a", Assert.Single(failure.NotAcceptedSubscriptionIds));
        }
        else
        {
            var result = await fixture.Admit();
            Assert.Equal("a", Assert.Single(result.Item.SubscriptionDelivery!.Recipients).SubscriptionId);
            Assert.Equal("target-start-policy-denied", Assert.Single(result.Item.SubscriptionDelivery.Refusals).ReasonCode);
        }
        Assert.Equal(expectedAdmissions, fixture.Delivery.Admissions);
    }

    [Fact]
    public async Task Require_all_refuses_the_whole_complete_cohort_before_accepting_any_work()
    {
        using var fixture = new Fixture();
        await fixture.Publish("a");
        await fixture.Save([Subscription("a"), Subscription("unpublished")]);
        var error = await Assert.ThrowsAsync<StandaloneTriggerAdmissionRefusedException>(() => fixture.Admit().AsTask());
        Assert.Equal("all-subscribers-required", error.Code);
        Assert.Equal("unpublished", Assert.Single(error.Refusals).SubscriptionId);
        Assert.Equal(0, fixture.Delivery.Admissions);
    }

    [Fact]
    public async Task Accept_available_saves_the_complete_partial_result_and_all_target_observations()
    {
        using var fixture = new Fixture("AcceptAvailable");
        await fixture.Publish("a");
        await fixture.Save([Subscription("a"), Subscription("unpublished")]);
        var result = await fixture.Admit();
        var selected = result.Item.SubscriptionDelivery!;
        Assert.Equal("a", Assert.Single(selected.Recipients).SubscriptionId);
        Assert.Equal("unpublished", Assert.Single(selected.Refusals).SubscriptionId);
        Assert.Equal(2, fixture.Delivery.Request!.Targets.Count);
        Assert.Null(fixture.Delivery.Request.Targets.Single(x => x.SubscriptionId == "unpublished").Slot);
        Assert.Equal(RuntimeIngressAdmissionPolicy.AcceptAvailable, result.Item.StandaloneIngress!.AdmissionPolicy);
        Assert.Equal("account-42", selected.Recipients[0].Inputs["account"].GetString());
        Assert.Equal("standalone-trigger:trigger", selected.Recipients[0].Authority.SystemIdentity);
        Assert.Equal("sender", selected.Recipients[0].Authority.RootInitiator);
    }

    [Theory]
    [InlineData("RequireAll")]
    [InlineData("AcceptAvailable")]
    public async Task Zero_accepted_subscribers_never_creates_an_accepted_source(string policy)
    {
        using var fixture = new Fixture(policy);
        await fixture.Save([Subscription("unpublished")]);
        var error = await Assert.ThrowsAsync<StandaloneTriggerAdmissionRefusedException>(() => fixture.Admit().AsTask());
        Assert.Equal("no-eligible-subscribers", error.Code);
        Assert.Equal(0, fixture.Delivery.Admissions);
    }

    [Fact]
    public async Task A_broken_serving_source_is_unknown_eligibility_and_does_not_become_a_partial_refusal()
    {
        using var fixture = new Fixture("AcceptAvailable");
        await fixture.Publish("a");
        await fixture.Slots.TryActivateAsync(new("broken", "Published", "missing-source", WorkflowActivationSource.Publishing, 0, Now));
        await fixture.Save([Subscription("a"), Subscription("broken")]);
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Admit().AsTask());
        Assert.Equal(0, fixture.Delivery.Admissions);
    }

    [Fact]
    public async Task Identical_retry_returns_original_acceptance_before_pause_unpublication_and_changed_mapping()
    {
        using var fixture = new Fixture();
        await fixture.Publish("a");
        await fixture.Save([Subscription("a")]);
        var first = await fixture.Admit();
        await fixture.Save([], TriggerDesiredState.Paused, revision: 2);
        var replay = await fixture.Admit();
        Assert.True(replay.Replayed);
        Assert.Equal(first.Item.SourceFingerprint, replay.Item.SourceFingerprint);
        Assert.Equal(1, fixture.Delivery.Admissions);
        var conflict = await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.Admit("changed").AsTask());
        Assert.Equal("conflict", conflict.Code);
    }

    [Fact]
    public async Task New_firings_follow_the_serving_version_and_revalidate_mappings()
    {
        using var fixture = new Fixture();
        await fixture.Publish("a");
        await fixture.Save([Subscription("a")]);
        var first = await fixture.Admit();
        await fixture.Publish("a", "new-required-input");
        var error = await Assert.ThrowsAsync<StandaloneTriggerAdmissionRefusedException>(() => fixture.Admit(key: "tomorrow").AsTask());
        Assert.Equal("no-eligible-subscribers", error.Code);
        Assert.Single(error.Refusals);
        Assert.Equal(1, fixture.Delivery.Admissions);
        Assert.Equal("a-v1", Assert.Single(first.Item.SubscriptionDelivery!.Recipients).Material.ArtifactId);
    }

    [Fact]
    public async Task Event_admission_is_independent_even_if_configuration_contains_http_policy_metadata()
    {
        using var fixture = new Fixture(type: "event");
        await fixture.Publish("a");
        await fixture.Save([Subscription("a", "event"), Subscription("unpublished", "event")]);
        var result = await fixture.Admit();
        Assert.Equal(RuntimeIngressAdmissionPolicy.Independent, result.Item.StandaloneIngress!.AdmissionPolicy);
        Assert.Single(result.Item.SubscriptionDelivery!.Refusals);
    }

    [Fact]
    public async Task The_producer_schema_and_resource_type_are_checked_before_new_acceptance()
    {
        using var fixture = new Fixture();
        await fixture.Publish("a");
        await fixture.Save([Subscription("a")]);
        var error = await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.Admission.AdmitAsync(fixture.Producer,
            fixture.Context, new("trigger", "today", new("String"), Json("\"forged schema\"")), default).AsTask());
        Assert.Equal("source-schema-mismatch", error.Code);
        Assert.Equal(0, fixture.Delivery.Admissions);
    }

    [Fact]
    public async Task Removing_a_mapping_language_blocks_enable_even_when_the_evaluator_remains_installed()
    {
        var expression = new MappingExpression();
        var available = InputMapper(expression, [expression]);
        var removed = InputMapper(expression, []);
        using var fixture = new Fixture(mapper: available);
        await fixture.Publish("a");
        var target = await fixture.Targets.ResolveAsync(Scope, "a", "Published", default);
        var resource = new StandaloneTrigger("trigger", "End of day", "http", 1,
            Json("{\"http\":{\"mode\":\"AsyncFanOut\"}}"), 1, 1, 1, TriggerDesiredState.Enabled,
            new(TriggerManagementKind.Studio), Now, Now, "creator", tenantId: "tenant");
        var write = new TriggerStoreWrite("enable", "enable-fingerprint", "operator", null, resource, [ExpressionSubscription("a")])
        { ObservedTargets = [TriggerManagementTargets.Observation("a", "Published", target)] };

        Assert.Single(await new TriggerManagementTargets(fixture.Targets, available).CheckAsync(Scope, null, write, default));
        var error = await Assert.ThrowsAsync<TriggerOperationException>(() =>
            new TriggerManagementTargets(fixture.Targets, removed).CheckAsync(Scope, null, write, default).AsTask());

        Assert.Equal("validation", error.Code);
        Assert.Equal(0, expression.Calls);
        Assert.Equal(0, fixture.Delivery.Admissions);
    }

    [Fact]
    public async Task Removed_mapping_language_refuses_require_all_before_accepting_a_healthy_sibling()
    {
        var expression = new MappingExpression();
        using var fixture = new Fixture(mapper: InputMapper(expression, []));
        await fixture.Publish("a"); await fixture.Publish("blocked");
        // Represents a persisted association enabled by the previous host composition.
        await fixture.Save([Subscription("a"), ExpressionSubscription("blocked")]);

        var error = await Assert.ThrowsAsync<StandaloneTriggerAdmissionRefusedException>(() => fixture.Admit().AsTask());

        Assert.Equal("all-subscribers-required", error.Code);
        Assert.Equal("a", Assert.Single(error.NotAcceptedSubscriptionIds));
        var refusal = Assert.Single(error.Refusals);
        Assert.Equal("blocked", refusal.SubscriptionId);
        Assert.Equal("triggers.mapping.expression-unavailable", refusal.ReasonCode);
        Assert.Equal(0, expression.Calls);
        Assert.Equal(0, fixture.Delivery.Admissions);
        Assert.Null(fixture.Delivery.Request);
    }

    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly TriggerScope Scope = new(new("tenant"), "tenant");
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
    private static TriggerSubscription Subscription(string id, string type = "http") => new(id, "trigger", id, "Published", "old-model-hint",
        1, 1, true, [new("account", new TriggerSourceBinding(["payload", "account"]))], new(TriggerManagementKind.Studio),
        new("grant-" + id, "trigger", id, id, "Published", "old-model-hint", type, 1, TriggerGrantStatus.Granted, "departed-creator", Now));
    private static TriggerSubscription ExpressionSubscription(string id) => new(id, "trigger", id, "Published", "native",
        1, 1, true, [new("account", new TriggerExpressionBinding("JavaScript", "account", new("String"), Json("{}"),
            ExpressionCapabilityProfiles.BindingPureV1, new Dictionary<string, TriggerValueBinding>
            { ["account"] = new TriggerSourceBinding(["payload", "account"]) }))], new(TriggerManagementKind.Studio),
        new("grant-" + id, "trigger", id, id, "Published", "native", "http", 1, TriggerGrantStatus.Granted, "departed-creator", Now));
    private static TriggerInputMapper InputMapper(IPortableExpressionEvaluator evaluator, IEnumerable<IPortableExpressionHandler> handlers) =>
        new(new WorkflowExecutableInputValidator(new StringTypeRegistry()), evaluator, handlers);

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider eventProvider;
        private readonly IServiceScope eventScope;
        private readonly string type;
        private readonly string policy;
        private readonly InMemoryWorkflowExecutableStore material = new();
        private readonly InMemoryWorkflowExecutableSourceReferenceStore sources = new();
        private readonly InMemoryStandaloneTriggerStore resources;
        private readonly FakeTimeProvider clock = new(Now);
        internal InMemoryWorkflowActivationAuthority Slots { get; } = new();
        internal RecordingDelivery Delivery { get; } = new();
        internal TriggerTargetResolver Targets { get; }
        internal StandaloneTriggerAdmission Admission { get; }
        internal StandaloneTriggerSourceDescriptor Producer { get; }
        internal StandaloneTriggerInvocationContext Context { get; } = new(Scope, new("tenant"), new("source-system", "sender"));

        internal Fixture(string policy = "RequireAll", string type = "http", IWorkflowExecutableStartPolicy? policyGate = null,
            ITriggerInputMapper? mapper = null)
        {
            this.type = type; this.policy = policy;
            var state = new InMemoryRuntimeCheckpointStoreState();
            resources = new(state, new Access(), clock);
            var pipeline = StandaloneTriggerPipelineFixture.Create(new InMemoryRuntimeCheckpointCommitStore(state: state,
                persistenceAccessContextAccessor: new Access()), new Access(), clock);
            Producer = new(type, 1, type + ".request", new("Object"));
            var metadata = new ServiceCollection();
            new EventsFeature().ConfigureServices(metadata);
            metadata.AddSingleton<ITriggerTargetModelSource, NativeTriggerTargetModelSource>();
            metadata.AddScoped<IEventHandler<TriggerTargetModelsDiscovering>, CollectTriggerTargetModels>();
            eventProvider = metadata.BuildServiceProvider(); eventScope = eventProvider.CreateScope();
            Targets = new(Slots, sources, material, new Access(),
                eventScope.ServiceProvider.GetRequiredService<IInlineEventPublisher>(), clock);
            Admission = new(resources, Delivery, Targets,
                mapper ?? new TriggerInputMapper(new WorkflowExecutableInputValidator(new StringTypeRegistry())), [new Adapter()], clock,
                policyGate ?? new AllowWorkflowExecutableStartPolicy(), pipeline.Processor, pipeline.Dispatcher);
        }
        public void Dispose() { eventScope.Dispose(); eventProvider.Dispose(); }

        internal async Task Save(IReadOnlyList<TriggerSubscription> subscriptions, TriggerDesiredState state = TriggerDesiredState.Enabled, long revision = 1)
        {
            var resource = new StandaloneTrigger("trigger", "End of day", type, 1,
                JsonSerializer.SerializeToElement(new { http = new { mode = "AsyncFanOut", admissionPolicy = policy } }), revision,
                revision, revision, state, new(TriggerManagementKind.Studio), Now, Now, "departed-creator", hasActivated: true, tenantId: "tenant");
            await resources.WriteAsync(Scope, new("request-" + revision, "fingerprint-" + revision, "operator", revision == 1 ? null : revision - 1, resource, subscriptions));
        }
        internal async Task Publish(string definition, string input = "account")
        {
            var prior = await Slots.FindAsync(definition, "Published");
            var version = (prior?.Revision ?? 0) + 1;
            var artifact = definition + "-v" + version;
            var executable = new WorkflowExecutable(new(artifact, definition, artifact, version.ToString(), "sha256:" + artifact),
                new ExecutableNode("root", "root", "test", "1.0.0", new("test", RuntimeActivityDescriptor.InitialSchemaVersion, Json("{}")),
                    new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>()),
                new Dictionary<string, WorkflowExecutableResumeTarget>(), Now, new Dictionary<string, string>(),
                new WorkflowExecutableInputContract(1, [new(input, new("String"), true)]), [], IncidentStrategyBuiltIns.FaultReference);
            await material.SaveAsync(executable);
            var activation = artifact + "-activation";
            var slot = WorkflowActivationSlotIdentity.Create(definition, "Published");
            await sources.SaveAsync(new(WorkflowActivationReferenceIdentity.Create(activation), artifact, "test", definition, version.ToString(),
                definition, artifact, version.ToString(), Now, Now, WorkflowExecutableReferenceScope.Published, ActivationId: activation, SlotId: slot, TenantId: "tenant"));
            await Slots.TryActivateAsync(new(definition, "Published", activation, WorkflowActivationSource.Publishing, prior?.Revision ?? 0, Now));
        }
        internal ValueTask<RuntimeIngressAdmissionReceipt> Admit(string account = "account-42", string key = "today") =>
            Admission.AdmitAsync(Producer, Context, new("trigger", key, Producer.PayloadSchema, JsonSerializer.SerializeToElement(new { account })), default);
    }

    private sealed class Access : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(Scope.Persistence); }

    private sealed class MappingExpression : IPortableExpressionEvaluator, IPortableExpressionHandler
    {
        public string Language => "JavaScript";
        internal int Calls { get; private set; }
        public ValueTask<JsonElement> EvaluateAsync(ExpressionEvaluationRequest request)
        { Calls++; return ValueTask.FromResult(request.ParameterValues["account"]); }
    }

    private sealed class DenyOnePolicy : IWorkflowExecutableStartPolicy
    {
        public ValueTask<WorkflowExecutableStartDecision> EvaluateAsync(WorkflowExecutableStartPolicyContext context, CancellationToken cancellationToken = default)
        {
            Assert.Equal("standalone-trigger:trigger", context.RequestedBy);
            return ValueTask.FromResult(context.Executable.DefinitionId == "blocked"
                ? WorkflowExecutableStartDecision.Deny("host-private-policy", "Private host policy detail.") : WorkflowExecutableStartDecision.Allow());
        }
    }

    private sealed class Adapter : IStandaloneTriggerRuntimeTarget
    {
        public string ModelKey => "native";
        public bool Supports(WorkflowExecutable? nativeWorkflow, RuntimeModelMaterial? model) => nativeWorkflow is not null;
        public ValueTask<RuntimeDispatchSeal> PrepareDispatchAsync(RuntimePostCommitOutboxItem source, RuntimeSubscriptionRecipient recipient, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RuntimeTargetAcceptanceReceipt?> ReconcileAsync(RuntimePostCommitOutboxItem source, RuntimeSubscriptionRecipient recipient, RuntimeDispatchSeal seal, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask DispatchAsync(RuntimePostCommitOutboxItem source, RuntimeSubscriptionRecipient recipient, RuntimeDispatchSeal seal, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecordingDelivery : IRuntimeIngressDeliveryStore
    {
        public bool SupportsAdmission => true;
        internal RuntimeIngressAdmissionRequest? Request { get; private set; }
        internal int Admissions { get; private set; }
        public ValueTask<RuntimeIngressAdmissionReceipt?> FindAcceptedAsync(TriggerScope scope, string sourceOutboxItemId, string requestFingerprint, CancellationToken cancellationToken = default)
        {
            if (Request is null || Request.Candidate.OutboxItemId != sourceOutboxItemId) return ValueTask.FromResult<RuntimeIngressAdmissionReceipt?>(null);
            if (Request.Candidate.StandaloneIngress!.RequestFingerprint != requestFingerprint) throw new TriggerOperationException("conflict", "Changed submitted intent.");
            return ValueTask.FromResult<RuntimeIngressAdmissionReceipt?>(new(Request.Candidate, true));
        }
        public ValueTask<RuntimeIngressAdmissionReceipt> AdmitAsync(RuntimeIngressAdmissionRequest request, CancellationToken cancellationToken = default)
        { Admissions++; Request = request; return ValueTask.FromResult(new RuntimeIngressAdmissionReceipt(request.Candidate, false)); }
    }
}
