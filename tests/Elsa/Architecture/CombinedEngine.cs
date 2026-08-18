using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Events;
using Elsa.Expressions.Core.Models;
using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Validations;
using Elsa.Workflows.Publishing;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Requests;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Reconciliation;
using Elsa.Workflows.Runtime.Reconciliation.Contracts;
using Elsa.Workflows.Runtime.Reconciliation.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using EventActivity = Elsa.Activities.Primitives.Activities.Event;

namespace Elsa.Architecture.Tests;

/// <summary>
/// The all-in-one engine US5 is about: design stores + the real publish pipeline + execution, with the new
/// artifact features optionally armed on the same container.
/// </summary>
/// <remarks>
/// <para>
/// <b>Publishing happens through <see cref="PublishWorkflowRequestHandler"/>, not through a store write.</b>
/// <see cref="PublishCapableEngine"/> deliberately writes the compiled artifact and its reference straight into
/// the stores so a round trip compares identical bytes — but that path never touches the activation authority, so
/// it cannot say anything about ownership. US5's entire subject is which source owns a definition's activation
/// slot, so this engine drives the production handler: policy resolution, preflight, the publication journal and
/// the shared <c>IWorkflowActivationCoordinator</c> all run, and the slot ends up stamped
/// <see cref="WorkflowActivationSource.Publishing"/> by the production code rather than by the fixture.
/// </para>
/// <para>
/// <b>The workflow is a real start trigger.</b> Its single node is <see cref="EventActivity"/>, a CLR activity
/// carrying <c>[TriggerActivity]</c>, so the compiler stamps <c>executionType = Trigger</c> from the CLR type and
/// the production <c>EventTriggerStimulusProvider</c> derives the binding's stimulus from the authored
/// <c>EventName</c> literal. That is what makes "a stimulus never starts two instances" assertable by actually
/// delivering a stimulus, rather than by counting rows in a projection.
/// </para>
/// <para>
/// <b>Workflow-execution ids are minted per start.</b> The shared harness hands out one fixed id, which would let
/// a genuine double-start collapse onto a single execution state and pass a naive count. This engine replaces the
/// generator so a second start is visible as a second state.
/// </para>
/// </remarks>
internal sealed class CombinedEngine : IAsyncDisposable
{
    /// <summary>The activation lane both the publish pipeline and the importer contend for.</summary>
    internal const string SlotName = "default";

    /// <summary>The source id an armed mount identifies itself by — also its activation ownership key.</summary>
    internal const string MountSourceId = "mounted-artifacts";

    private const string EventActivityDefinitionId = "activity-definition-event";
    private const string EventActivityVersionId = "activity-version-event";

    private readonly WorkflowExecutionHarness _harness;

    private CombinedEngine(WorkflowExecutionHarness harness) => _harness = harness;

    internal IServiceProvider Services => _harness.Services;

    internal WorkflowExecutionHarness Harness => _harness;

    public ValueTask DisposeAsync() => _harness.DisposeAsync();

    /// <summary>
    /// Composes design authoring + publishing + execution, and — when <paramref name="artifactMount"/> is
    /// supplied — the artifact importer on top of the very same container.
    /// </summary>
    internal static CombinedEngine Create(
        IReadOnlyCollection<WorkflowDefinitionVersion> versions,
        string? artifactMount = null)
    {
        var builder = RuntimeEngines.NewBuilder()
            .WithFeature(services =>
            {
                services.AddSingleton<IWorkflowDefinitionVersionStore>(
                    new PublishCapableEngine.StubWorkflowVersionStore(versions.ToArray()));
                services.AddSingleton<IActivityDefinitionVersionStore>(
                    new PublishCapableEngine.StubActivityVersionStore(EventActivityVersion()));
                services.AddSingleton<IActivityDefinitionVersionPublicationStore, PublishCapableEngine.EmptyActivityPublicationStore>();
                services.AddSingleton<IExecutableActivityTemplateStore, InMemoryExecutableActivityTemplateStore>();
                services.AddSingleton<IWorkflowExecutableInputValidator, WorkflowExecutableInputValidator>();
            })
            .WithFeature(services => new EventsFeature().ConfigureServices(services))
            // PublishWorkflowRequestHandler refuses to publish without an expression validator — it treats an
            // absent one as "validation unavailable" rather than as "nothing to validate". Composing the real
            // feature keeps the publish path production-shaped instead of stubbing the refusal away.
            .WithFeature(services => new WorkflowDesignValidationsFeature().ConfigureServices(services))
            .WithFeature(services => new WorkflowsPublishingFeature().ConfigureServices(services));

        if (artifactMount is not null)
            builder = builder.WithFeature(services => new JsonWorkflowArtifactReconciliationFeature
            {
                Options =
                {
                    SourceId = MountSourceId,
                    FolderPath = artifactMount,
                },
            }.ConfigureServices(services));

        var harness = builder
            .ConfigureServices(services =>
                services.AddSingleton<IRuntimeExecutionIdGenerator>(
                    new CountingRuntimeExecutionIdGenerator(RuntimeEngines.ActivityExecutionIds)))
            .Build(RuntimeEngines.ActivityExecutionIds);

        // The compiler resolves each node's CLR activity type through the well-known type registry, and the
        // import gate asks the same registry whether the type is present. Both need the startup scan to have run.
        harness.InitializeActivityTypes();
        return new CombinedEngine(harness);
    }

    /// <summary>Publishes one authored version through the production publish request handler.</summary>
    internal async Task<PublishedWorkflowView> PublishAsync(string definitionVersionId)
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<IRequestHandler<PublishWorkflow, PublishedWorkflowView>>()
            .Handle(new PublishWorkflow(definitionVersionId), CancellationToken.None);
    }

    /// <summary>Produces the portable closure for a published version through the production factory.</summary>
    internal async Task<WorkflowArtifactClosure> ExportAsync(string definitionVersionId)
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IWorkflowArtifactClosureFactory>()
            .CreateAsync(definitionVersionId);
    }

    /// <summary>Exports a published version and writes its wire bytes into <paramref name="mount"/>.</summary>
    internal async Task<WorkflowArtifactClosure> ExportToAsync(string definitionVersionId, string mount, string fileName)
    {
        var closure = await ExportAsync(definitionVersionId);
        var wire = Services.GetRequiredService<IWorkflowArtifactClosureSerializer>().Serialize(closure);
        File.WriteAllText(Path.Combine(mount, fileName), wire);
        return closure;
    }

    /// <summary>Runs one artifact reconciliation pass, in its own scope, as the startup task does.</summary>
    internal async Task<WorkflowArtifactReconciliationResult> ReconcileAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IWorkflowArtifactReconciler>().ReconcileAsync();
    }

    // ---- Serving-state readers ---------------------------------------------------------------------------

    /// <summary>The definition's activation slot — the ledger the ownership rule is decided from.</summary>
    internal ValueTask<WorkflowActivationSlot?> FindSlotAsync(string definitionId) =>
        Services.GetRequiredService<IWorkflowActivationAuthority>().FindAsync(definitionId, SlotName);

    /// <summary>
    /// The <b>serving</b> trigger bindings a stimulus resolves to — the projection the stimulus router routes on.
    /// </summary>
    /// <remarks>
    /// <c>ListAllByStimulusAsync</c> returns active rows only, so this is the runtime's own answer to "what would
    /// this stimulus start", not a re-derivation of it from the slot.
    /// </remarks>
    internal ValueTask<IReadOnlyList<WorkflowTriggerBinding>> ListServingBindingsAsync(string eventName) =>
        Services.GetRequiredService<IWorkflowTriggerBindingStore>()
            .ListAllByStimulusAsync(EventStimulus.StimulusType, EventStimulus.Hash(eventName));

    /// <summary>Every workflow execution state the engine holds, for double-start counting.</summary>
    internal ValueTask<IReadOnlyCollection<WorkflowExecutionState>> ListExecutionsAsync() =>
        Services.GetRequiredService<IWorkflowExecutionStateStore>().ListAsync();

    /// <summary>
    /// Delivers a real named-event stimulus through the production router and drains the engine.
    /// </summary>
    /// <remarks>
    /// <c>StartOnly</c> because the claim under test is about starting: a resume outcome would be a different
    /// (and here impossible) thing. The idempotency key is supplied so a duplicate <em>delivery</em> is ruled out
    /// as an explanation for a single start — what is being measured is duplicate <em>activation</em>.
    /// </remarks>
    internal async Task<StimulusRoutingResult> DeliverEventAsync(string eventName, string idempotencyKey)
    {
        await using var scope = Services.CreateAsyncScope();
        var routing = await scope.ServiceProvider.GetRequiredService<IStimulusRouter>().RouteAsync(
            new StimulusDispatchRequest(
                EventStimulus.StimulusType,
                EventStimulus.Hash(eventName),
                mode: StimulusRoutingMode.StartOnly,
                idempotencyKey: idempotencyKey));
        await _harness.SweepUntilQuietAsync();
        return routing;
    }

    // ---- Authored documents -------------------------------------------------------------------------------

    /// <summary>
    /// A one-node workflow whose root is a named-event start trigger.
    /// </summary>
    /// <remarks>
    /// The event name is authored as a literal because the stimulus identity is fixed at publish time; it is also
    /// what makes two versions of one definition observably different artifacts — different literal, different
    /// canonical hash, different content-addressed id, and a different stimulus to route.
    /// </remarks>
    internal static WorkflowDefinitionVersion EventWorkflow(
        string definitionId,
        string versionId,
        string version,
        string nodeId,
        string eventName) =>
        new(definitionId, version)
        {
            Id = versionId,
            Definition = new WorkflowDefinition { Id = definitionId, Name = definitionId },
            State = new WorkflowDefinitionState(
                [],
                new ActivityNode(
                    nodeId,
                    EventActivityVersionId,
                    [
                        new ArgumentState(
                            nameof(EventActivity.EventName),
                            new ArgumentValue(eventName, "Literal"),
                            null,
                            null,
                            null,
                            null),
                        new ArgumentState(
                            nameof(EventActivity.CanStartWorkflow),
                            new ArgumentValue(true, "Literal"),
                            null,
                            null,
                            null,
                            null)
                    ],
                    []),
                [],
                [],
                null)
        };

    /// <summary>
    /// The activity-catalog row for <see cref="EventActivity"/>, keyed on the CLR-activity consumer.
    /// </summary>
    /// <remarks>
    /// The consumer key is load-bearing twice over: <c>ExecutableNodeCompiler</c> only resolves a CLR type — and
    /// therefore only learns that this activity is a trigger — when the row's consumer key is
    /// <see cref="WellKnownRuntimeActivityConsumers.ClrActivity"/>, and the importer's gate refuses a node whose
    /// descriptor consumer no activation strategy handles.
    /// </remarks>
    private static ActivityDefinitionVersion EventActivityVersion()
    {
        var alias = TypeAliasConvention.CanonicalAlias(typeof(EventActivity));
        return new ActivityDefinitionVersion("1.0.0", EventActivityDefinitionId, executionType: ActivityExecutionType.Trigger)
        {
            Id = EventActivityVersionId,
            Definition = new ActivityDefinition
            {
                Id = EventActivityDefinitionId,
                ActivityTypeKey = EventActivity.ActivityType,
                Category = "Primitives"
            },
            ProviderKey = WellKnownRuntimeActivityConsumers.ClrActivity,
            ProviderSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
            ConsumerKey = WellKnownRuntimeActivityConsumers.ClrActivity,
            ConsumerSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
            DescriptorPayload = JsonSerializer.SerializeToElement(new ClrActivityDescriptor(alias)),
            Inputs =
            [
                new InputDefinition(
                    nameof(EventActivity.EventName),
                    nameof(EventActivity.EventName),
                    new TypeReference("String"),
                    null,
                    "Event name",
                    null,
                    false,
                    IsRequired: true),
                new InputDefinition(
                    nameof(EventActivity.CorrelationId),
                    nameof(EventActivity.CorrelationId),
                    new TypeReference("String"),
                    null,
                    "Correlation id",
                    null,
                    true),
                new InputDefinition(
                    nameof(EventActivity.CanStartWorkflow),
                    nameof(EventActivity.CanStartWorkflow),
                    new TypeReference("Boolean"),
                    null,
                    "Can start workflow",
                    null,
                    false)
            ]
        };
    }

    /// <summary>
    /// Mints a distinct workflow-execution id per start, delegating everything else to the shared deterministic
    /// generator.
    /// </summary>
    /// <remarks>
    /// Not a convenience. The shared generator returns one constant workflow-execution id, so two starts would
    /// write to the same execution state and a "exactly one instance" assertion would hold even while the engine
    /// had double-started. Numbering the ids makes the failure this test exists to catch actually observable.
    /// </remarks>
    private sealed class CountingRuntimeExecutionIdGenerator(IEnumerable<string> activityExecutionIds)
        : IRuntimeExecutionIdGenerator
    {
        private readonly DeterministicRuntimeExecutionIdGenerator _inner = new(activityExecutionIds);
        private int _workflowOrdinal;

        public string NewWorkflowExecutionId() => $"wfexec-{Interlocked.Increment(ref _workflowOrdinal)}";

        public string NewWorkflowExecutionCommandId() => _inner.NewWorkflowExecutionCommandId();

        public string NewWorkflowExecutionCommandEnvelopeId() => _inner.NewWorkflowExecutionCommandEnvelopeId();

        public string NewActivityExecutionId() => _inner.NewActivityExecutionId();
    }
}
