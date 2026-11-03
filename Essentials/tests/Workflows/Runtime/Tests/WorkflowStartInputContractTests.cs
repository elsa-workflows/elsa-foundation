using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Every workflow start applies the pinned input contract before its start command is enqueued (#3728): an omitted input
/// with a declared default gets that default, a missing required input is refused, and an undeclared input name is refused.
/// The runtime API and trigger starts are driven through the composed <see cref="IWorkflowStartDispatcher"/>.
/// </summary>
public sealed class WorkflowStartInputContractTests : IAsyncLifetime
{
    private const string ArtifactId = "artifact-inputs";
    private const string StimulusType = "Event";
    private const string StimulusHash = "sha256:event:inputs";
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private readonly RecordingAgentProvider _agents = new();
    private ServiceProvider _provider = null!;
    private AsyncServiceScope _scope;

    public Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IWellKnownTypeRegistry>(new TestWellKnownTypeRegistry());
        services.AddSingleton<IWorkflowExecutionActorProvider>(_agents);
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
        services.AddWorkflowRuntime().AddPersistenceModes();
        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateAsyncScope();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _provider.DisposeAsync();
    }

    [Fact]
    public async Task RuntimeApiStart_AppliesTheDeclaredDefaultOfAnOmittedInput()
    {
        await PublishAsync(Optional("greeting", "\"hello\""), Required("count"));

        await ApiStartAsync(("count", 3));

        var inputs = Assert.Single(_agents.StartInputs);
        Assert.Equal("hello", inputs["greeting"].GetString());
        Assert.Equal(3, inputs["count"].GetInt32());
    }

    [Fact]
    public async Task RuntimeApiStart_RefusesAMissingRequiredInputBeforeEnqueue()
    {
        await PublishAsync(Required("count"));

        var exception = await Assert.ThrowsAsync<WorkflowExecutableStartRejectedException>(() => ApiStartAsync());

        Assert.Equal(WorkflowExecutableStartRejectionCategory.Input, exception.Category);
        Assert.Equal(WorkflowExecutableInputValidationReasonCodes.MissingRequired, exception.ReasonCode);
        Assert.Empty(_agents.StartInputs);
    }

    [Fact]
    public async Task RuntimeApiStart_RefusesAnUndeclaredInputNameBeforeEnqueue()
    {
        await PublishAsync(Required("count"));

        var exception = await Assert.ThrowsAsync<WorkflowExecutableStartRejectedException>(() => ApiStartAsync(("count", 3), ("unknown", 1)));

        Assert.Equal(WorkflowExecutableStartRejectionCategory.Input, exception.Category);
        Assert.Equal(WorkflowExecutableInputValidationReasonCodes.UnknownName, exception.ReasonCode);
        Assert.Empty(_agents.StartInputs);
    }

    [Fact]
    public async Task TriggerStart_AppliesTheDeclaredDefaultOfAnOmittedInput()
    {
        await PublishAsync(Optional("greeting", "\"hello\""));
        var router = await RouterWithBindingAsync();

        var result = await router.RouteAsync(StartRequest());

        Assert.Equal(1, result.StartedCount);
        var inputs = Assert.Single(_agents.StartInputs);
        Assert.Equal("hello", inputs["greeting"].GetString());
    }

    [Fact]
    public async Task TriggerStart_RefusesAMissingRequiredInputBeforeEnqueue()
    {
        await PublishAsync(Required("count"));
        var router = await RouterWithBindingAsync();

        var exception = await Assert.ThrowsAsync<WorkflowExecutableStartRejectedException>(() => router.RouteAsync(StartRequest()).AsTask());

        Assert.Equal(WorkflowExecutableStartRejectionCategory.Input, exception.Category);
        Assert.Equal(WorkflowExecutableInputValidationReasonCodes.MissingRequired, exception.ReasonCode);
        Assert.Empty(_agents.StartInputs);
    }

    private async Task PublishAsync(params WorkflowDeclaredInput[] inputs)
    {
        var services = _scope.ServiceProvider;
        await services.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(new WorkflowExecutable(
            identity: new WorkflowExecutableIdentity(ArtifactId, "definition-1", "version-1", "1.0.0", "sha256:inputs"),
            rootActivity: new ExecutableNode(
                executableNodeId: "node-root",
                authoredActivityId: "authored-node-root",
                activityType: "test/activity",
                activityTypeVersion: "1.0.0",
                descriptor: new RuntimeActivityDescriptor("test", RuntimeActivityDescriptor.InitialSchemaVersion, JsonSerializer.SerializeToElement(new { type = "test" })),
                inputBindings: new Dictionary<string, RuntimeInputBinding>(),
                metadata: new Dictionary<string, string>()),
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: Now,
            compatibilityMetadata: new Dictionary<string, string>(),
            inputContract: new WorkflowExecutableInputContract(WorkflowExecutableInputContract.CurrentVersion, inputs),
            dependencies: null,
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference));
        await services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().SaveAsync(new WorkflowExecutableSourceReference(
            "published-ref-inputs", ArtifactId, "WorkflowDefinitionVersion", "version-1", "1.0.0",
            "definition-1", "version-1", "1.0.0", Now, Now, WorkflowExecutableReferenceScope.Published));
    }

    private Task<WorkflowExecutionStartDispatchView> ApiStartAsync(params (string Name, int Value)[] inputs)
    {
        var services = _scope.ServiceProvider;
        var handler = new WorkflowExecutionStartService(
            services.GetRequiredService<IWorkflowStartDispatcher>(),
            services.GetRequiredService<IWorkflowExecutableStore>());
        return handler.ExecuteAsync(
            new ExecuteWorkflow(ArtifactId, Inputs: inputs.ToDictionary(input => input.Name, input => JsonSerializer.SerializeToElement(input.Value), StringComparer.Ordinal)),
            CancellationToken.None);
    }

    private async Task<StimulusRouter> RouterWithBindingAsync()
    {
        var bindings = new InMemoryWorkflowTriggerBindingStore();
        await bindings.SaveAsync(new WorkflowTriggerBinding(
            TriggerBindingId: WorkflowTriggerBinding.BuildId(ArtifactId, "node-root", StimulusHash),
            ArtifactId: ArtifactId,
            DefinitionId: "definition-1",
            ArtifactVersion: "1.0.0",
            ArtifactHash: "sha256:inputs",
            ExecutableNodeId: "node-root",
            StimulusType: StimulusType,
            StimulusHash: StimulusHash,
            CorrelationScope: null,
            Metadata: new Dictionary<string, string>(),
            CreatedAt: Now));
        var services = _scope.ServiceProvider;
        return new StimulusRouter(
            bindings,
            new GlobalBookmarkStimulusLookup(new InMemoryBookmarkStateStore()),
            services.GetRequiredService<IWorkflowStartDispatcher>(),
            services.GetRequiredService<IBookmarkResumeDispatcher>(),
            new FakeTimeProvider(Now));
    }

    private static StimulusDispatchRequest StartRequest() =>
        new(StimulusType, StimulusHash, mode: StimulusRoutingMode.StartOnly);

    private static WorkflowDeclaredInput Required(string name) => new(name, new TypeReference("Int32"), isRequired: true);

    private static WorkflowDeclaredInput Optional(string name, string defaultJson) =>
        new(name, new TypeReference("String"), isRequired: false, JsonDocument.Parse(defaultJson).RootElement.Clone());

    /// <summary>Accepts every start and records the workflow inputs its start command carries.</summary>
    private sealed class RecordingAgentProvider : IWorkflowExecutionActorProvider
    {
        public List<IReadOnlyDictionary<string, JsonElement>> StartInputs { get; } = [];

        public WorkflowExecutionActorCapabilities Capabilities => WorkflowExecutionActorCapabilities.None;

        public ValueTask<IWorkflowExecutionActor> GetAgentAsync(WorkflowExecutionActorActivationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IWorkflowExecutionActor>(new RecordingAgent(request.WorkflowExecutionId, StartInputs));

        public ValueTask PassivateAsync(WorkflowExecutionActorPassivationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class RecordingAgent(string workflowExecutionId, List<IReadOnlyDictionary<string, JsonElement>> startInputs) : IWorkflowExecutionActor
    {
        public WorkflowExecutionActorDescriptor Descriptor { get; } = new(
            workflowExecutionId: workflowExecutionId,
            agentId: $"recording:{workflowExecutionId}",
            providerName: "Recording",
            status: WorkflowExecutionActorStatus.Active,
            capabilities: WorkflowExecutionActorCapabilities.None,
            activatedAt: Now);

        public ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)
        {
            startInputs.Add(envelope.Command.Payload!.Value.Deserialize<WorkflowExecutionStartCommandPayload>()!.Inputs);
            return ValueTask.FromResult(new WorkflowExecutionCommandDispatchResult(
                envelopeId: envelope.EnvelopeId,
                workflowExecutionId: envelope.WorkflowExecutionId,
                status: WorkflowExecutionCommandDispatchStatus.Accepted,
                recordedAt: Now));
        }
    }

    private sealed class TestWellKnownTypeRegistry : IWellKnownTypeRegistry
    {
        private readonly Dictionary<string, Type> _types = new(StringComparer.Ordinal) { ["String"] = typeof(string), ["Int32"] = typeof(int) };

        public void RegisterType(Type type, string alias) => _types.Add(alias, type);
        public bool TryGetAlias(Type type, out string alias)
        {
            alias = _types.SingleOrDefault(x => x.Value == type).Key!;
            return alias is not null;
        }

        public bool TryGetType(string alias, out Type type) => _types.TryGetValue(alias, out type!);
        public IEnumerable<Type> ListTypes() => _types.Values;
        public string GetAliasOrDefault(Type type) => _types.SingleOrDefault(x => x.Value == type).Key ?? type.FullName!;
        public Type GetTypeOrDefault(string alias) => _types.GetValueOrDefault(alias, typeof(object));
        public bool TryGetTypeOrDefault(string alias, out Type type) => TryGetType(alias, out type);
    }
}
