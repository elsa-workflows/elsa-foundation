using System.Net.Http;
using System.Text;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Handlers;
using Elsa.Workflows.Publishing.Api.Tests.Support;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Requests;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Triggers;
using Elsa.Workflows.Runtime.Tests.Fixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// Spec 238 D12 / US4.6: a retired publication whose start trigger the start-path rule now refuses cannot be restored.
/// The refusal arrives through the existing activation-failure channel, and the saved executable is untouched. The
/// publication is first made under a strategy that accepts the placement, standing in for "published before the
/// rule", then the strategy refuses it.
/// </summary>
public sealed class RestoreMisplacedStartTriggerEndpointTests : IAsyncLifetime
{
    private const string VersionId = "version-restore-misplaced";
    private const string DefinitionId = "restore-misplaced-definition";
    private const string CompositeKind = "test.toggle";
    private const string TriggerType = "Test.StartTrigger";

    private readonly InMemoryWorkflowExecutableStore _executables = new();
    private readonly TogglingStartEntryProvider _startEntries = new();
    private PublishingMinimalApiScenarioHost _host = null!;

    public async Task InitializeAsync() =>
        _host = await PublishingMinimalApiScenarioHost.StartAsync(
            requestSenderFactory: services => new PublishHandlerSender(services.GetRequiredService<IHttpContextAccessor>()),
            compiler: new StructuredTriggerCompiler(),
            configureServices: ConfigureServices);

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IWorkflowDefinitionVersionStore>(new FakeVersionStore(
            new WorkflowDefinitionVersion(DefinitionId, "1.0.0")
            {
                Id = VersionId,
                Definition = new WorkflowDefinition { Id = DefinitionId, Name = "RestoreMisplaced" },
                State = WorkflowDefinitionState.Empty
            }));
        services.AddSingleton<IExpressionDraftSemanticValidator, ValidExpressions>();
        services.TryAddScoped<IWorkflowTriggerIndexer, WorkflowTriggerIndexer>();
        services.RemoveAll<IWorkflowTriggerBindingExtractor>();
        services.AddSingleton<IWorkflowTriggerBindingExtractor>(new WorkflowTriggerBindingExtractor([new StartTriggerProvider()], [_startEntries]));
        services.RemoveAll<IWorkflowExecutableStore>();
        services.AddSingleton<IWorkflowExecutableStore>(_executables);
        // The capture seams register canned slot lifecycle stubs; this test needs the real handlers.
        services.RemoveAll<IPublicationSlotUnpublisher>();
        services.AddScoped<IPublicationSlotUnpublisher, UnpublishPublicationSlotRequestHandler>();
        services.RemoveAll<IPublicationSlotRestorer>();
        services.AddScoped<IPublicationSlotRestorer, RestorePublicationSlotRequestHandler>();
    }

    [Fact]
    public async Task Restoring_a_retired_publication_with_a_now_misplaced_start_trigger_fails_and_keeps_the_executable()
    {
        using var publish = await SendAsync(HttpMethod.Post, $"/publishing/workflows/{VersionId}/publish", "{}");
        Assert.True(publish.IsSuccessStatusCode, $"{(int)publish.StatusCode}: {await publish.Content.ReadAsStringAsync()}");
        using var unpublish = await SendAsync(HttpMethod.Delete, $"/publishing/workflows/{DefinitionId}/slots/default", null);
        Assert.True(unpublish.IsSuccessStatusCode, $"{(int)unpublish.StatusCode}: {await unpublish.Content.ReadAsStringAsync()}");
        var saved = Assert.Single(await _executables.ListAllAsync());

        _startEntries.AcceptsTrigger = false;
        using var restore = await SendAsync(HttpMethod.Post, $"/publishing/workflows/{DefinitionId}/slots/default/restore", "{}");

        Assert.False(restore.IsSuccessStatusCode, $"{(int)restore.StatusCode}: {await restore.Content.ReadAsStringAsync()}");
        Assert.Equal(saved.Identity.ArtifactHash, Assert.Single(await _executables.ListAllAsync()).Identity.ArtifactHash);
        var slot = await _host.Services.GetRequiredService<IWorkflowActivationAuthority>().FindAsync(DefinitionId, "default");
        Assert.Null(slot?.ActiveActivationId);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string route, string? body)
    {
        using var request = new HttpRequestMessage(method, route);
        if (body is not null)
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        request.Headers.TryAddWithoutValidation(PublishingCompatibilityCases.IdentityHeader, "trusted-success");
        return await _host.Client.SendAsync(request);
    }

    /// <summary>Begins at the trigger while <see cref="AcceptsTrigger"/> is true, and refuses it once it is false.</summary>
    private sealed class TogglingStartEntryProvider : IStructuralStartEntryProvider
    {
        public bool AcceptsTrigger { get; set; } = true;
        public string StructureKind => CompositeKind;

        public ExecutableNode? SelectStartChild(ExecutableNode composite, string triggerNodeId) =>
            AcceptsTrigger
                ? composite.ChildSlots.SelectMany(slot => slot.Activities).FirstOrDefault(child => child.ExecutableNodeId == triggerNodeId)
                : null;
    }

    /// <summary>Compiles every version into a composite of <see cref="CompositeKind"/> whose only child is a start trigger.</summary>
    private sealed class StructuredTriggerCompiler : IWorkflowExecutableCompiler
    {
        public ValueTask<WorkflowExecutable> CompileAsync(WorkflowExecutableCompileRequest request, CancellationToken cancellationToken = default)
        {
            using var document = JsonDocument.Parse("{}");
            var descriptor = new RuntimeActivityDescriptor("capture.consumer", "1", document.RootElement.Clone());
            var trigger = new ExecutableNode(
                executableNodeId: "node-trigger",
                authoredActivityId: "authored-node-trigger",
                activityType: TriggerType,
                activityTypeVersion: "1.0",
                descriptor: descriptor,
                inputBindings: new Dictionary<string, RuntimeInputBinding>(),
                metadata: new Dictionary<string, string> { [TriggerNodeMetadata.ExecutionTypeKey] = TriggerNodeMetadata.TriggerExecutionType });
            var root = new ExecutableNode(
                executableNodeId: "node-root",
                authoredActivityId: "authored-node-root",
                activityType: "Test.Composite",
                activityTypeVersion: "1.0",
                descriptor: descriptor,
                inputBindings: new Dictionary<string, RuntimeInputBinding>(),
                metadata: new Dictionary<string, string>(),
                childSlots: [new ExecutableChildSlot("Body", [trigger])],
                structure: new ExecutableActivityStructure(CompositeKind, "1", JsonSerializer.SerializeToElement(new { })));
            return ValueTask.FromResult(new WorkflowExecutable(
                new WorkflowExecutableIdentity("restore-artifact", DefinitionId, request.VersionId, "1.0", "restore-hash"),
                root,
                new Dictionary<string, WorkflowExecutableResumeTarget>(),
                new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero),
                new Dictionary<string, string>(),
                new IncidentStrategyReference("default", "1")));
        }
    }

    private sealed class StartTriggerProvider : IActivityTriggerStimulusProvider
    {
        public string ProviderId => "test.start";
        public TriggerCardinality Cardinality => TriggerCardinality.FanOut;

        public ActivityTriggerStimulusResult Describe(ExecutableNode node) =>
            StringComparer.Ordinal.Equals(node.ActivityType, TriggerType)
                ? ActivityTriggerStimulusResult.Recognized([new TriggerStimulusDescriptor("Event", "sha256:restore")])
                : ActivityTriggerStimulusResult.NotRecognized;
    }

    private sealed class PublishHandlerSender(IHttpContextAccessor accessor) : IRequestSender
    {
        public async Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) where T : notnull =>
            (T)(object)await accessor.HttpContext!.RequestServices
                .GetRequiredService<IRequestHandler<PublishWorkflow, PublishedWorkflowView>>()
                .Handle((PublishWorkflow)(object)request, cancellationToken);
    }

    private sealed class ValidExpressions : IExpressionDraftSemanticValidator
    {
        public ValueTask<ExpressionDraftValidationResult> ValidateAsync(
            WorkflowDefinitionState state,
            string documentScope,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ExpressionDraftValidationResult(ExpressionDraftValidationState.Valid, []));
    }
}
