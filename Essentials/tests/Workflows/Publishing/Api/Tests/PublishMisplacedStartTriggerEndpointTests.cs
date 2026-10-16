using System.Net;
using System.Text;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Tests.Support;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Requests;
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
