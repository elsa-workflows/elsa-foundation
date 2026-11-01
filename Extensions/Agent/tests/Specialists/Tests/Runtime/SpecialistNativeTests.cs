using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Activities.Runtime;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Agent.Specialists.Core.Contracts;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Agent.Specialists.Services;
using Elsa.Agent.Specialists.Journal;
using Elsa.Agent.Specialists.Tests.Publication;
using Elsa.Primitives.Models;
using Elsa.Secrets.Features;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Services.Values;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Agent.Specialists.Tests.Runtime;

public sealed class SpecialistNativeTests
{
    [Theory]
    [InlineData("{\"Article\":null}", WorkflowExecutionStatus.Completed)]
    [InlineData("{}", WorkflowExecutionStatus.Faulted)]
    public async Task Required_nullable_result_keeps_presence_separate_from_allowed_null(string response, WorkflowExecutionStatus expected)
    {
        var factory = new FixtureFactory(response);
        await using var provider = await ServicesAsync(factory);
        var (actor, _) = await StartAsync(provider, await ExecutableAsync(provider, outputNullable: true));
        var claim = await provider.GetRequiredService<IBackgroundActivityWorkStore>().ClaimNextAsync("test", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));
        Assert.NotNull(claim);
        await provider.GetRequiredService<BackgroundActivityWorker>().ExecuteClaimAsync(new("default"), claim);
        await DrainAsync(actor);
        Assert.Equal(expected, (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!.Status);
        Assert.Equal(1, factory.Calls);
    }

    [Fact]
    public async Task Optional_nonnullable_input_can_be_absent_without_becoming_explicit_null()
    {
        var factory = new FixtureFactory("{\"Article\":\"no brief supplied\"}");
        await using var provider = await ServicesAsync(factory);
        var (actor, _) = await StartAsync(provider, await ExecutableAsync(provider, optionalBrief: true));
        var claim = await provider.GetRequiredService<IBackgroundActivityWorkStore>().ClaimNextAsync("test", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));
        Assert.NotNull(claim);
        await provider.GetRequiredService<BackgroundActivityWorker>().ExecuteClaimAsync(new("default"), claim);
        await DrainAsync(actor);
        Assert.Equal(WorkflowExecutionStatus.Completed, (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!.Status);
        Assert.False(JsonDocument.Parse(factory.LastUser!).RootElement.TryGetProperty("Brief", out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Null_or_encryption_required_input_is_refused_before_dispatch(bool requiresEncryption)
    {
        var factory = new FixtureFactory("{\"Article\":\"must not dispatch\"}");
        await using var provider = await ServicesAsync(factory);
        var policy = requiresEncryption
            ? new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true, requiresEncryption: true)
            : ValueProtectionPolicy.InstanceInline;
        var value = requiresEncryption ? ValueEnvelope.Inline(new("String"), Json("\"private brief\""), policy) : ValueEnvelope.Null(new("String"), policy);
        var binding = new RuntimeInputBinding("brief-key", new("String"), policy, RuntimeInputBindingSource.Literal, literal: value);
        var (actor, _) = await StartAsync(provider, await ExecutableAsync(provider, optionalBrief: true, briefBinding: binding));
        await DrainAsync(actor);
        Assert.Null(await provider.GetRequiredService<IBackgroundActivityWorkStore>().ClaimNextAsync("test", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2)));
        Assert.Equal(WorkflowExecutionStatus.Faulted, (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!.Status);
        Assert.Equal(0, factory.Calls);
        Assert.Equal(0, factory.Opens);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_capture_admission_respects_the_destination_policy_before_dispatch(bool requiresEncryption)
    {
        var factory = new FixtureFactory("{\"Article\":\"private result\"}");
        await using var provider = await ServicesAsync(factory);
        var policy = new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true, requiresEncryption: requiresEncryption);
        var captures = new Dictionary<string, RuntimeOutputCapture>
        {
            ["article-key"] = new("article-key", "variable:Article", new("String", "elsa.json", null), DurableValueLifecycle.Instance, DurableValueStorage.Custom, true,
                new Dictionary<string, string> { [RuntimeMetadataKeys.TargetVariableReferenceKey] = "article-variable" })
        };
        var executable = await ExecutableAsync(provider, outputCaptures: captures, variables: [new("article-variable", "Article", new("String"), policy)]);
        var (actor, _) = await StartAsync(provider, executable);
        var claim = await provider.GetRequiredService<IBackgroundActivityWorkStore>().ClaimNextAsync("test", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));
        if (requiresEncryption) Assert.Null(claim);
        else
        {
            Assert.NotNull(claim);
            await provider.GetRequiredService<BackgroundActivityWorker>().ExecuteClaimAsync(new("default"), claim);
            await DrainAsync(actor);
        }
        var workflow = (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!;
        Assert.Equal(requiresEncryption ? WorkflowExecutionStatus.Faulted : WorkflowExecutionStatus.Completed, workflow.Status);
        Assert.Equal(requiresEncryption ? 0 : 1, factory.Opens);
        Assert.Equal(requiresEncryption ? 0 : 1, factory.Calls);
        var value = workflow.RootVariableFrame!.Values["article-variable"];
        Assert.True(value.Policy.IsSensitive);
        Assert.True(value.Policy.HidesValue());
        if (!requiresEncryption) Assert.Equal("private result", value.InlineValue!.Value.GetString());
    }

    [Fact]
    public async Task Secret_business_input_is_refused_before_opening_a_client()
    {
        var factory = new FixtureFactory("{\"Article\":\"must not dispatch\"}");
        await using var provider = await ServicesAsync(factory);
        var policy = new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true, requiresEncryption: true);
        var input = new RuntimeInputBinding("brief-key", new("String"), policy, RuntimeInputBindingSource.SecretRead, secret: new("logical-secret"));
        await StartAsync(provider, await ExecutableAsync(provider, briefBinding: input));
        Assert.Null(await provider.GetRequiredService<IBackgroundActivityWorkStore>().ClaimNextAsync("test", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2)));
        Assert.Equal(WorkflowExecutionStatus.Faulted, (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!.Status);
        Assert.Equal(0, factory.Opens);
        Assert.Equal(0, factory.Calls);
    }

    [Theory]
    [InlineData("result")]
    [InlineData("projection")]
    public async Task Encryption_required_result_policy_is_refused_before_opening_a_client(string mode)
    {
        var factory = new FixtureFactory("{\"Article\":\"must not dispatch\"}");
        await using var provider = await ServicesAsync(factory);
        await StartAsync(provider, await ExecutableAsync(provider, resultPolicyMode: mode));
        Assert.Null(await provider.GetRequiredService<IBackgroundActivityWorkStore>().ClaimNextAsync("test", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2)));
        Assert.Equal(WorkflowExecutionStatus.Faulted, (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!.Status);
        Assert.Equal(0, factory.Opens);
        Assert.Equal(0, factory.Calls);
    }

    [Fact]
    public async Task Sensitive_native_values_execute_with_redacted_inspection_evidence()
    {
        var factory = new FixtureFactory("{\"Article\":\"private result\"}");
        await using var provider = await ServicesAsync(factory);
        var policy = new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true);
        var input = new RuntimeInputBinding("brief-key", new("String"), policy, RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(new("String"), Json("\"private brief\""), policy));
        var executable = await ExecutableAsync(provider, briefBinding: input, resultPolicyMode: "sensitive");
        var (actor, _) = await StartAsync(provider, executable);
        var claim = await provider.GetRequiredService<IBackgroundActivityWorkStore>().ClaimNextAsync("test", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));
        Assert.NotNull(claim);
        await provider.GetRequiredService<BackgroundActivityWorker>().ExecuteClaimAsync(new("default"), claim);
        await DrainAsync(actor);
        Assert.Equal(WorkflowExecutionStatus.Completed, (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!.Status);
        Assert.Equal(1, factory.Calls);
        var boundary = (await provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("run"))
            .Single(x => x.Execution.ExecutableNodeId == executable.RootActivity.ExecutableNodeId);
        Assert.True(boundary.Completion!.Result.Policy.IsSensitive);
        Assert.False(boundary.Completion.Result.Policy.RequiresEncryption);
        var inspection = (await provider.GetRequiredService<IActivityExecutionInspectionStore>().FindAsync("run", boundary.InvocationId))!;
        Assert.Contains(inspection.ValueSnapshots, x => x.Subject == ActivityExecutionInspectionValueSubject.ActivityInput && x.IsSensitive);
        Assert.All(inspection.ValueSnapshots.Where(x => x.IsSensitive), x =>
        {
            Assert.Equal(RuntimePayloadCaptureMode.None, x.CaptureMode);
            Assert.Null(x.Payload);
        });
        Assert.DoesNotContain("private brief", JsonSerializer.Serialize(inspection), StringComparison.Ordinal);
        Assert.DoesNotContain("private result", JsonSerializer.Serialize(inspection), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Malformed_model_json_retains_the_parser_cause()
    {
        await using var provider = await ServicesAsync(new FixtureFactory("not JSON"));
        var node = (await ExecutableAsync(provider)).RootActivity;
        var request = new SpecialistTurnRequest(new(null, "default", "run", "activity", "operation", "artifact", "hash", "copywriter-v1", "template"),
            SpecialistNativeBinding.Descriptor(node), node.ActivityContract!, Json("{\"Brief\":\"brief\"}"), "binding");
        var exception = await Assert.ThrowsAsync<SpecialistTurnException>(async () => await provider.GetRequiredService<SpecialistModelRunner>().RunAsync(request, CancellationToken.None));
        Assert.IsAssignableFrom<JsonException>(exception.InnerException);
        Assert.Equal("The model result is not valid bounded JSON.", exception.Message);
    }

    [Fact]
    public void Malformed_descriptor_json_retains_the_parser_cause()
    {
        var node = new ExecutableNode("specialist", "specialist", "type", "1", new RuntimeActivityDescriptor(SpecialistRuntimeIds.ConsumerKey, "1", Json("{\"instructions\":{}}")),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>());
        var exception = Assert.Throws<SpecialistTurnException>(() => SpecialistNativeBinding.Descriptor(node));
        Assert.IsAssignableFrom<JsonException>(exception.InnerException);
    }

    [Theory]
    [InlineData("Article", false)]
    [InlineData("Article", true)]
    [InlineData("Article.body", false)]
    public async Task Native_actor_runs_the_published_shape_on_a_background_worker_and_commits_its_typed_result(string outputName, bool allowContinuity)
    {
        var factory = new FixtureFactory(JsonSerializer.Serialize(new Dictionary<string, string> { [outputName] = "draft about moss gardens" }));
        await using var provider = await ServicesAsync(factory);
        var executable = await ExecutableAsync(provider, outputName, allowContinuity: allowContinuity);
        var (actor, start) = await StartAsync(provider, executable);
        Assert.Equal(0, factory.Calls);
        var states = await provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("run");
        var boundary = Assert.Single(states.Where(x => x.Execution.ExecutableNodeId == executable.RootActivity.ExecutableNodeId));
        Assert.Null(boundary.Completion);
        Assert.True(boundary.PrivateState is not null, boundary.SubStatus + ": " + boundary.Fault?.Message + "\n" + boundary.Fault?.SanitizedStackTrace);
        Assert.Equal(JsonValueKind.String, boundary.PrivateState!.Value.InlineValue!.Value.ValueKind);
        var handle = boundary.PrivateState.Value.InlineValue.Value.GetString();
        Assert.Equal(64, handle!.Length);
        Assert.DoesNotContain("moss", boundary.PrivateState.Value.InlineValue.Value.GetRawText(), StringComparison.Ordinal);

        var workStore = provider.GetRequiredService<IBackgroundActivityWorkStore>();
        var claim = await workStore.ClaimNextAsync("specialist-test", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));
        Assert.NotNull(claim);
        await provider.GetRequiredService<BackgroundActivityWorker>().ExecuteClaimAsync(new("default"), claim);
        Assert.Equal(1, factory.Calls);
        Assert.Equal("Write the supplied brief.", factory.Instructions);
        Assert.Equal("fixture-model", factory.ModelId);
        Assert.Equal("moss gardens", JsonDocument.Parse(factory.LastUser!).RootElement.GetProperty("Brief").GetString());
        Assert.True(JsonElement.DeepEquals(executable.RootActivity.DescriptorPayload.GetProperty("resultSchema"), factory.Schema!.Value));
        Assert.Equal(1, factory.Disposals);
        await DrainAsync(actor);
        var workflow = await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run");
        Assert.Equal(WorkflowExecutionStatus.Completed, workflow!.Status);
        states = await provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("run");
        boundary = states.Single(x => x.Execution.ExecutableNodeId == executable.RootActivity.ExecutableNodeId);
        Assert.Equal("draft about moss gardens", boundary.Completion!.Result.InlineValue!.Value.GetProperty(outputName).GetString());
        var consumer = boundary with
        {
            Execution = new("consumer", "run", "consumer", "consumer", "reader", "1"),
            Status = ActivityExecutionStatus.Running, SchedulingActivityExecutionId = boundary.InvocationId, Completion = null
        };
        var binding = new RuntimeInputBinding("next", new("String"), ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.ActivityResult,
            activityResult: new(executable.RootActivity.ExecutableNodeId, "article-key", boundary.Provenance.ExecutionScopeId ?? "scope:root"));
        var resolved = provider.GetRequiredService<IRuntimeInputBindingResolver>().Resolve(binding,
            new("run", consumer.InvocationId, consumer, states.Append(consumer).ToArray(), executable));
        Assert.Equal("draft about moss gardens", resolved.Envelope!.InlineValue!.Value.GetString());
        var worker = states.Single(x => x.Execution.ExecutableNodeId == executable.RootActivity.ChildSlots.Single().Activities.Single().ExecutableNodeId);
        Assert.Equal(handle, worker.Completion!.Result.InlineValue!.Value.GetString());
        var descriptor = SpecialistNativeBinding.Descriptor(executable.RootActivity);
        var identity = new SpecialistInvocationIdentity(workflow.TenantId, workflow.Partition!.Value, workflow.WorkflowExecutionId,
            boundary.InvocationId, boundary.OperationKey, workflow.PinnedExecutable.ArtifactId, workflow.PinnedExecutable.ArtifactHash,
            descriptor.DefinitionVersionId!, descriptor.TemplateHash!);
        var accepted = await provider.GetRequiredService<SpecialistTurnJournal>().ResultAsync(handle, identity, worker.InvocationId, CancellationToken.None);
        if (allowContinuity)
        {
            Assert.NotNull(accepted.Session);
            Assert.Equal(handle, accepted.Session.ConversationId);
            Assert.Equal(1, accepted.Session.Revision);
            Assert.Equal(2, provider.GetRequiredService<SpecialistSessionCodec>().Restore(accepted.Session, identity).Count);
        }
        else Assert.Null(accepted.Session);
        await actor.EnqueueAsync(start);
        await provider.GetRequiredService<BackgroundActivityWorker>().ExecuteClaimAsync(new("default"), claim);
        Assert.Equal(1, factory.Calls);
    }

    [Theory]
    [InlineData("{\"wrong\":true}", "String")]
    [InlineData("not JSON", "String")]
    [InlineData("{\"Article\":\"not-a-guid\"}", "Guid")]
    public async Task Invalid_model_result_cannot_create_a_native_completion(string response, string outputType)
    {
        var factory = new FixtureFactory(response);
        await using var provider = await ServicesAsync(factory);
        var (actor, _) = await StartAsync(provider, await ExecutableAsync(provider, outputType: outputType));
        var claim = await provider.GetRequiredService<IBackgroundActivityWorkStore>().ClaimNextAsync("test", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));
        Assert.NotNull(claim);
        await provider.GetRequiredService<BackgroundActivityWorker>().ExecuteClaimAsync(new("default"), claim);
        await DrainAsync(actor);
        Assert.Equal(1, factory.Calls);
        var states = await provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("run");
        Assert.All(states, x => Assert.Null(x.Completion));
        Assert.Equal(WorkflowExecutionStatus.Faulted, (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!.Status);
    }

    private static async ValueTask<ServiceProvider> ServicesAsync(FixtureFactory factory)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWorkflowRuntime();
        services.AddSingleton<IRuntimeDurableValueStorageDriverRegistry>(new RuntimeDurableValueStorageDriverRegistry([new JsonRuntimeDurableValueStorageDriver()]));
        new ActivitiesRuntimeFeature().ConfigureServices(services);
        new SerializationFeature().ConfigureServices(services);
        new BackgroundActivitiesFeature { RunWorkers = false }.ConfigureServices(services);
        new SecretsFeature { EncryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }.ConfigureServices(services);
        new SpecialistsRuntimeFeature().ConfigureServices(services);
        new SpecialistsInMemoryJournalFeature().ConfigureServices(services);
        services.AddSingleton<ISpecialistModelClientFactory>(factory);
        services.AddPersistenceModeOptions(new PersistenceModeOptions { Mode = PersistenceMode.Durable });
        services.AddPersistenceModes();
        var provider = services.BuildServiceProvider();
        var types = provider.GetRequiredService<IWellKnownTypeRegistry>();
        types.RegisterType(typeof(string), "String");
        types.RegisterType(typeof(JsonElement), "System.Text.Json.JsonElement");
        await new SpecialistTypesStartupTask(types).ExecuteAsync(CancellationToken.None);
        return provider;
    }

    private static async ValueTask<WorkflowExecutable> ExecutableAsync(ServiceProvider provider, string outputName = "Article", string outputType = "String", bool outputNullable = false, bool optionalBrief = false, RuntimeInputBinding? briefBinding = null, IReadOnlyDictionary<string, RuntimeOutputCapture>? outputCaptures = null, IReadOnlyCollection<RuntimeVariableDeclaration>? variables = null, string? resultPolicyMode = null, bool allowContinuity = false)
    {
        // The authoring fixture is disposed, including its three SQLite stores, before execution starts.
        var published = await SpecialistPublicationTests.PublishAndPlaceAsync(outputName, outputType, outputNullable, optionalBrief, briefBinding, outputCaptures, allowContinuity);
        var root = published.Placement.Root;
        if (resultPolicyMode is not null)
        {
            // A seeded artifact backstop: generic publication currently has no authored policy fields.
            var original = root.ActivityContract!;
            var policy = ActivityValuePolicy.Default with { IsSensitive = true, RequiresEncryption = resultPolicyMode != "sensitive" };
            var result = new ActivityResultContract(original.Result.Type, original.Result.IsRequired,
                resultPolicyMode == "projection" ? original.Result.Policy : policy,
                original.Result.Projections.Values.Select(x => new ActivityResultProjectionContract(x.Key, x.Path, x.Type, x.IsRequired,
                    resultPolicyMode == "result" ? x.Policy : policy, x.SourceRepresentation)), original.Result.SourceRepresentation);
            var contract = new ActivityContract(original.ActivityTypeKey, original.ContractVersion, original.DescriptorKind, original.DescriptorPayload,
                original.Inputs.Values, result, original.Outcomes, original.Activation, original.SideEffectProfile, original.SupportsBackgroundExecution);
            root = new(root.ExecutableNodeId, root.AuthoredActivityId, root.ActivityType, root.ActivityTypeVersion, root.Descriptor,
                root.InputBindings, root.Metadata, root.ChildSlots, root.Structure, contract);
        }
        var executable = new WorkflowExecutable(new("editorial", "workflow", "workflow-v1", "1.0.0", "artifact-hash"), root,
            published.Placement.ResumeTargets, DateTimeOffset.UtcNow, new Dictionary<string, string>(), inputContract: null, dependencies: null,
            published.Placement.RuntimeRequirements, published.Storage, IncidentStrategyBuiltIns.FaultReference, workflowVariables: variables);
        await provider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable);
        return executable;
    }

    private static async ValueTask<(IWorkflowExecutionActor Actor, WorkflowExecutionCommandEnvelope Start)> StartAsync(ServiceProvider provider, WorkflowExecutable executable)
    {
        var now = DateTimeOffset.UtcNow;
        var actor = await provider.GetRequiredService<IWorkflowExecutionActorProvider>().GetAgentAsync(new("run", WorkflowExecutionActorActivationReason.Start, now,
            "specialist-tests", WorkflowExecutionActorCapabilities.InProcessMailbox));
        var metadata = new Dictionary<string, string> { ["runtime.artifactId"] = executable.Identity.ArtifactId };
        var command = new WorkflowExecutionCommand("start", "run", WorkflowExecutionCommandKind.Start, now,
            JsonSerializer.SerializeToElement(new WorkflowExecutionStartCommandPayload(executable.Identity, executable.Identity.ArtifactId,
                variables: null, inputs: null, stimulusInput: null, triggerNodeId: null, runKind: WorkflowRunKind.Unknown, pinnedSource: null,
                parentWorkflowExecutionId: null, correlationId: null, tenantId: null, partition: new("default"), authority: null)), metadata);
        var envelope = new WorkflowExecutionCommandEnvelope("start-envelope", "run", command, "start", WorkflowExecutionCommandDeliveryMode.AtLeastOnce, now, 1, metadata);
        await actor.EnqueueAsync(envelope);
        return (actor, envelope);
    }

    private static async ValueTask DrainAsync(IWorkflowExecutionActor actor)
    {
        var now = DateTimeOffset.UtcNow;
        var command = new WorkflowExecutionCommand("drain", "run", WorkflowExecutionCommandKind.RunSchedulerWork, now, null, new Dictionary<string, string>());
        await actor.EnqueueAsync(new("drain-envelope", "run", command, "drain", WorkflowExecutionCommandDeliveryMode.AtLeastOnce, now));
    }

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
    private sealed class FixtureFactory(string response) : ISpecialistModelClientFactory
    {
        private string Response { get; } = response;
        public string ProviderKey => "fixture";
        public int Opens { get; private set; }
        public int Calls { get; private set; }
        public int Disposals { get; private set; }
        public string? Instructions { get; private set; }
        public string? LastUser { get; private set; }
        public string? ModelId { get; private set; }
        public JsonElement? Schema { get; private set; }
        public ValueTask<SpecialistModelClientLease> OpenAsync(SpecialistModelOpenRequest request, CancellationToken cancellationToken = default)
        {
            Opens++;
            return ValueTask.FromResult(new SpecialistModelClientLease(new Client(this), new ChatOptions(), "fixture/1", 1));
        }
        private sealed class Client(FixtureFactory owner) : IChatClient
        {
            public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            {
                owner.Calls++;
                owner.LastUser = messages.Last(x => x.Role == ChatRole.User).Text;
                owner.Instructions = options?.Instructions;
                owner.ModelId = options?.ModelId;
                owner.Schema = (options?.ResponseFormat as ChatResponseFormatJson)?.Schema;
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, owner.Response)) { Usage = new UsageDetails { InputTokenCount = 5, OutputTokenCount = 3 } });
            }
            public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public object? GetService(Type serviceType, object? serviceKey = null) => null;
            public void Dispose() => owner.Disposals++;
        }
    }
}
