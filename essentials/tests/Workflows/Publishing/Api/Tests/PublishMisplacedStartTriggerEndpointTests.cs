using System.Net;
using System.Text;
using System.Text.Json;
using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Tests.Support;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Requests;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
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
/// Spec 238, FR-022 and FR-023: a start trigger that no run can start from fails the publish endpoint with a 400 and
/// leaves nothing saved, while a host configuration failure in the same preflight does not become a 400. The
/// placement walk itself is covered by <c>WorkflowTriggerPlacementTests</c>; this pins the endpoint contract.
/// </summary>
public sealed class PublishMisplacedStartTriggerEndpointTests : IAsyncLifetime
{
    private const string VersionId = "version-misplaced-trigger";
    // Hardcoded by CaptureWorkflowExecutableCompiler regardless of the compiled version id.
    private const string DefinitionId = "capture-definition";
    private const string PublishRoute = $"/publishing/workflows/{VersionId}/publish";

    private readonly ThrowingTriggerExtractor _triggers = new();
    private readonly InMemoryWorkflowExecutableStore _executables = new();
    private PublishingMinimalApiScenarioHost _host = null!;

    public async Task InitializeAsync() =>
        _host = await PublishingMinimalApiScenarioHost.StartAsync(
            requestSenderFactory: services => new PublishHandlerSender(services.GetRequiredService<IHttpContextAccessor>()),
            configureServices: ConfigureServices);

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IWorkflowDefinitionVersionStore>(new FakeVersionStore(
            new WorkflowDefinitionVersion(DefinitionId, "1.0.0")
            {
                Id = VersionId,
                Definition = new WorkflowDefinition { Id = DefinitionId, Name = "MisplacedTrigger" },
                State = WorkflowDefinitionState.Empty
            }));
        services.AddSingleton<IExpressionDraftSemanticValidator, ValidExpressions>();
        services.TryAddScoped<IWorkflowTriggerIndexer, WorkflowTriggerIndexer>();
        services.RemoveAll<IWorkflowTriggerBindingExtractor>();
        services.AddSingleton<IWorkflowTriggerBindingExtractor>(_triggers);
        services.RemoveAll<IWorkflowExecutableStore>();
        services.AddSingleton<IWorkflowExecutableStore>(_executables);
    }

    [Fact]
    public async Task Placement_failure_is_a_400_and_nothing_is_saved()
    {
        _triggers.Facet = WorkflowTriggerPreflightException.PlacementFacet;

        using var response = await PostAsync("{}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await _executables.ListAllAsync());
        var authority = _host.Services.GetRequiredService<IWorkflowActivationAuthority>();
        Assert.Empty(await authority.ListByDefinitionAsync(DefinitionId));
    }

    [Fact]
    public async Task Other_preflight_facets_are_not_a_400()
    {
        _triggers.Facet = "ProviderIdentity";

        using var response = await PostAsync("{}");

        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(response.IsSuccessStatusCode);
        Assert.Empty(await _executables.ListAllAsync());
    }

    private async Task<HttpResponseMessage> PostAsync(string body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, PublishRoute)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation(PublishingCompatibilityCases.IdentityHeader, "trusted-success");
        return await _host.Client.SendAsync(request);
    }

    /// <summary>Fails every preflight with the configured facet, as the real extractor does for a misplaced start trigger.</summary>
    private sealed class ThrowingTriggerExtractor : IWorkflowTriggerBindingExtractor
    {
        public string Facet { get; set; } = WorkflowTriggerPreflightException.PlacementFacet;

        public IReadOnlyCollection<WorkflowTriggerBinding> Extract(WorkflowExecutable executable) => throw Failure(executable);

        public WorkflowTriggerPreflightOutcome Evaluate(WorkflowExecutable executable) => throw Failure(executable);

        private WorkflowTriggerPreflightException Failure(WorkflowExecutable executable) =>
            Facet == WorkflowTriggerPreflightException.PlacementFacet
                ? new WorkflowTriggerPreflightException(
                    executable.Identity.ArtifactId,
                    "node-trigger",
                    "Elsa.Event",
                    ["test.start"],
                    executable.RootActivity.ExecutableNodeId,
                    "elsa.sequence.structure",
                    "Start trigger 'node-trigger' cannot start this workflow.")
                : new WorkflowTriggerPreflightException(
                    executable.Identity.ArtifactId,
                    "node-trigger",
                    "Elsa.Event",
                    [],
                    Facet,
                    "A trigger provider has a blank provider id.");
    }

    /// <summary>Hands the publish request to the real handler, as the mediator would; the capture sender would answer it.</summary>
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

/// <summary>
/// The same endpoint contract through the <em>real</em> trigger extractor: the compiled workflow has a start trigger
/// below a structure-less composite, which the start-path rule refuses. These go red if the placement walk is turned
/// off, unlike the stubbed cases above.
/// </summary>
public sealed class PublishMisplacedStartTriggerRealExtractorEndpointTests : IAsyncLifetime
{
    private const string VersionId = "version-misplaced-trigger-real";
    private const string DefinitionId = "misplaced-definition";
    private const string TriggerType = "Test.StartTrigger";
    private const string PublishRoute = $"/publishing/workflows/{VersionId}/publish";

    private readonly InMemoryWorkflowExecutableStore _executables = new();
    private PublishingMinimalApiScenarioHost _host = null!;

    public async Task InitializeAsync() =>
        _host = await PublishingMinimalApiScenarioHost.StartAsync(
            requestSenderFactory: services => new PublishHandlerSender(services.GetRequiredService<IHttpContextAccessor>()),
            compiler: new MisplacedTriggerCompiler(),
            configureServices: ConfigureServices);

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IWorkflowDefinitionVersionStore>(new FakeVersionStore(
            new WorkflowDefinitionVersion(DefinitionId, "1.0.0")
            {
                Id = VersionId,
                Definition = new WorkflowDefinition { Id = DefinitionId, Name = "MisplacedTriggerReal" },
                State = WorkflowDefinitionState.Empty
            }));
        services.AddSingleton<IExpressionDraftSemanticValidator, ValidExpressions>();
        services.TryAddScoped<IWorkflowTriggerIndexer, WorkflowTriggerIndexer>();
        services.RemoveAll<IWorkflowTriggerBindingExtractor>();
        services.AddSingleton<IWorkflowTriggerBindingExtractor>(new WorkflowTriggerBindingExtractor([new StartTriggerProvider()], []));
        services.RemoveAll<IWorkflowExecutableStore>();
        services.AddSingleton<IWorkflowExecutableStore>(_executables);
    }

    [Fact]
    public async Task A_start_trigger_below_a_composite_no_strategy_answers_for_is_a_400_and_nothing_is_saved()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, PublishRoute)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation(PublishingCompatibilityCases.IdentityHeader, "trusted-success");

        using var response = await _host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await _executables.ListAllAsync());
        Assert.Empty(await _host.Services.GetRequiredService<IWorkflowActivationAuthority>().ListByDefinitionAsync(DefinitionId));
    }

    /// <summary>Compiles every version into a structure-less composite whose only child is a start trigger.</summary>
    private sealed class MisplacedTriggerCompiler : IWorkflowExecutableCompiler
    {
        public ValueTask<WorkflowExecutable> CompileAsync(WorkflowExecutableCompileRequest request, CancellationToken cancellationToken = default)
        {
            var trigger = Node("node-trigger", TriggerType, new Dictionary<string, string>
            {
                [TriggerNodeMetadata.ExecutionTypeKey] = TriggerNodeMetadata.TriggerExecutionType
            }, []);
            var root = Node("node-root", "Test.Composite", new Dictionary<string, string>(), [new ExecutableChildSlot("Body", [trigger])]);
            return ValueTask.FromResult(new WorkflowExecutable(
                new WorkflowExecutableIdentity("misplaced-artifact", DefinitionId, request.VersionId, "1.0", "misplaced-hash"),
                root,
                new Dictionary<string, WorkflowExecutableResumeTarget>(),
                new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero),
                new Dictionary<string, string>(),
                new IncidentStrategyReference("default", "1")));
        }

        private static ExecutableNode Node(string nodeId, string activityType, Dictionary<string, string> metadata, IReadOnlyCollection<ExecutableChildSlot> childSlots)
        {
            using var document = JsonDocument.Parse("{}");
            return new ExecutableNode(
                executableNodeId: nodeId,
                authoredActivityId: $"authored-{nodeId}",
                activityType: activityType,
                activityTypeVersion: "1.0",
                descriptor: new RuntimeActivityDescriptor("capture.consumer", "1", document.RootElement.Clone()),
                inputBindings: new Dictionary<string, RuntimeInputBinding>(),
                metadata: metadata,
                childSlots: childSlots);
        }
    }

    private sealed class StartTriggerProvider : IActivityTriggerStimulusProvider
    {
        public string ProviderId => "test.start";
        public TriggerCardinality Cardinality => TriggerCardinality.FanOut;

        public ActivityTriggerStimulusResult Describe(ExecutableNode node) =>
            StringComparer.Ordinal.Equals(node.ActivityType, TriggerType)
                ? ActivityTriggerStimulusResult.Recognized([new TriggerStimulusDescriptor("Event", "sha256:misplaced")])
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
