using System.Net;
using System.Text;
using System.Text.Json;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Handlers;
using Elsa.Workflows.Publishing.Api.Tests.Support;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using static Elsa.Workflows.Publishing.Api.Tests.Support.CodedProblemAssertions;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// #1699 over HTTP: every 400/409 Publishing raises on the preflight, publish, restore, unpublish and policy
/// routes carries its machine-readable failure code as a top-level, additive <c>errorCode</c>, while the rest of
/// the module's established problem shape (title, detail, status, type, errors) stays byte-identical, and a
/// problem with no failure code omits the member entirely.
/// </summary>
public sealed class PublicationProblemCodeEndpointTests : IAsyncLifetime
{
    private const string VersionId = "version-problem-codes";
    // Hardcoded by CaptureWorkflowExecutableCompiler regardless of the compiled version id.
    private const string DefinitionId = "capture-definition";
    private const string PublishRoute = $"/publishing/workflows/{VersionId}/publish";
    private const string PolicyRoute = $"/publishing/workflows/{DefinitionId}/policy";
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    private PublishingMinimalApiScenarioHost _host = null!;

    public async Task InitializeAsync() =>
        _host = await PublishingMinimalApiScenarioHost.StartAsync(
            requestSenderFactory: services => new PublishHandlerSender(services.GetRequiredService<IHttpContextAccessor>()),
            configureServices: ConfigureServices);

    /// <summary>
    /// The version store, expression validator, and trigger indexer every test in this class needs to reach a
    /// real publish or unpublish. <see cref="IWorkflowTriggerIndexer"/> is normally contributed by the runtime
    /// triggers feature, which this host does not mount; without it the activation coordinator refuses to run at
    /// all, so a publish that got past preflight could never reach the authority.
    /// </summary>
    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IWorkflowDefinitionVersionStore>(new FakeVersionStore(
            new WorkflowDefinitionVersion(DefinitionId, "1.0.0")
            {
                Id = VersionId,
                Definition = new WorkflowDefinition { Id = DefinitionId, Name = "ProblemCodes" },
                State = WorkflowDefinitionState.Empty
            }));
        services.AddSingleton<IExpressionDraftSemanticValidator, ValidExpressions>();
        services.TryAddScoped<IWorkflowTriggerIndexer, WorkflowTriggerIndexer>();
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Foreign_slot_owner_conflict_carries_its_errorCode()
    {
        var authority = _host.Services.GetRequiredService<IWorkflowActivationAuthority>();
        await authority.TryActivateAsync(new WorkflowActivationSlotRequest(
            DefinitionId, "default", "import:artifact-1", WorkflowActivationSource.ArtifactReconciliation("mounted-artifacts"), 0, Now));

        using var response = await PostAsync(PublishRoute, "{}");
        var raw = await response.Content.ReadAsStringAsync();

        AssertCodedProblem(response, raw, HttpStatusCode.Conflict, "slot_owner_conflict");
    }

    [Fact]
    public async Task Expected_publication_mismatch_carries_its_errorCode()
    {
        using var first = await PostAsync(PublishRoute, "{}");
        Assert.True(IsSuccess(first.StatusCode), $"{(int)first.StatusCode}: {await first.Content.ReadAsStringAsync()}");

        using var response = await PostAsync(PublishRoute, """{"expectedPublicationId":"wrong-publication"}""");
        var raw = await response.Content.ReadAsStringAsync();

        AssertCodedProblem(response, raw, HttpStatusCode.Conflict, "expected_publication_mismatch");
    }

    [Fact]
    public async Task Named_slot_required_is_a_400_and_carries_its_errorCode()
    {
        using var response = await PostAsync(PublishRoute, """{"action":"sideBySide"}""");
        var raw = await response.Content.ReadAsStringAsync();

        AssertCodedProblem(response, raw, HttpStatusCode.BadRequest, "named_slot_required");
    }

    [Fact]
    public async Task A_real_revision_race_on_unpublish_carries_slot_revision_conflict()
    {
        // A dedicated host: the slot-revision race below must not run against the shared fixture's authority,
        // which the other tests in this class also read.
        await using var host = await PublishingMinimalApiScenarioHost.StartAsync(
            requestSenderFactory: services => new PublishHandlerSender(services.GetRequiredService<IHttpContextAccessor>()),
            configureServices: services =>
            {
                ConfigureServices(services);
                services.RemoveAll<IWorkflowActivationAuthority>();
                services.AddSingleton<IWorkflowActivationAuthority>(sp => new RevisionRacingActivationAuthority(
                    new InMemoryWorkflowActivationAuthority(), DefinitionId, "default", sp.GetRequiredService<TimeProvider>()));
                // PublishingDomainSeams registers a canned IPublicationSlotUnpublisher stub for the historical
                // capture surface; this test needs the real handler so the CAS above can actually race it.
                services.RemoveAll<IPublicationSlotUnpublisher>();
                services.AddScoped<IPublicationSlotUnpublisher, UnpublishPublicationSlotRequestHandler>();
            });
        using var publish = await PostAsync(host, PublishRoute, "{}");
        Assert.True(IsSuccess(publish.StatusCode), $"{(int)publish.StatusCode}: {await publish.Content.ReadAsStringAsync()}");

        // Races a distractor publish in behind the unpublish handler's own read of the slot, so its CAS below
        // loses a real revision race rather than a stubbed one.
        using var unpublish = new HttpRequestMessage(HttpMethod.Delete, $"/publishing/workflows/{DefinitionId}/slots/default");
        unpublish.Headers.TryAddWithoutValidation(PublishingCompatibilityCases.IdentityHeader, "trusted-success");
        using var response = await host.Client.SendAsync(unpublish);
        var raw = await response.Content.ReadAsStringAsync();

        AssertCodedProblem(response, raw, HttpStatusCode.Conflict, "slot_revision_conflict");
    }

    [Fact]
    public async Task An_uncoded_404_problem_has_no_errorCode_member()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/publishing/publications/does-not-exist");
        request.Headers.TryAddWithoutValidation(PublishingCompatibilityCases.IdentityHeader, "trusted-success");
        using var response = await _host.Client.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var problem = JsonDocument.Parse(raw);
        Assert.False(problem.RootElement.TryGetProperty("errorCode", out _), $"Expected no errorCode member; got: {raw}");
    }

    [Fact]
    public async Task Setting_a_stale_workflow_publication_policy_carries_its_errorCode()
    {
        using var stale = new HttpRequestMessage(HttpMethod.Put, PolicyRoute)
        {
            Content = new StringContent("""{"defaultAction":"replace","defaultSlotName":"default","expectedRevision":41}""", Encoding.UTF8, "application/json")
        };
        stale.Headers.TryAddWithoutValidation(PublishingCompatibilityCases.IdentityHeader, "trusted-success");
        using var response = await _host.Client.SendAsync(stale);
        var raw = await response.Content.ReadAsStringAsync();

        AssertCodedProblem(response, raw, HttpStatusCode.Conflict, "policy_revision_conflict");
    }

    private static bool IsSuccess(HttpStatusCode status) =>
        status is HttpStatusCode.OK or HttpStatusCode.Created or HttpStatusCode.Accepted;

    private Task<HttpResponseMessage> PostAsync(string route, string body) => PostAsync(_host, route, body);

    private static async Task<HttpResponseMessage> PostAsync(PublishingMinimalApiScenarioHost host, string route, string body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, route)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation(PublishingCompatibilityCases.IdentityHeader, "trusted-success");
        return await host.Client.SendAsync(request);
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

    /// <summary>
    /// Wraps the real in-memory activation authority so the first <see cref="TryDeactivateAsync"/> call for one
    /// target slot loses a revision race to a distractor publish from the same owner, simulating a concurrent
    /// publish that moved the slot between the unpublish handler's own read and its CAS.
    /// </summary>
    private sealed class RevisionRacingActivationAuthority(
        IWorkflowActivationAuthority inner,
        string definitionId,
        string slotName,
        TimeProvider timeProvider) : IWorkflowActivationAuthority
    {
        private int _fired;

        public ValueTask<WorkflowActivationSlot?> FindAsync(string workflowDefinitionId, string targetSlotName, CancellationToken cancellationToken = default) =>
            inner.FindAsync(workflowDefinitionId, targetSlotName, cancellationToken);

        public ValueTask<IReadOnlyCollection<WorkflowActivationSlot>> ListByDefinitionAsync(string workflowDefinitionId, CancellationToken cancellationToken = default) =>
            inner.ListByDefinitionAsync(workflowDefinitionId, cancellationToken);

        public ValueTask<WorkflowActivationTransition> TryActivateAsync(WorkflowActivationSlotRequest request, CancellationToken cancellationToken = default) =>
            inner.TryActivateAsync(request, cancellationToken);

        public async ValueTask<WorkflowActivationTransition> TryDeactivateAsync(
            string workflowDefinitionId,
            string targetSlotName,
            WorkflowActivationSource source,
            long expectedRevision,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken = default)
        {
            if (StringComparer.Ordinal.Equals(workflowDefinitionId, definitionId) &&
                StringComparer.Ordinal.Equals(targetSlotName, slotName) &&
                Interlocked.Exchange(ref _fired, 1) == 0)
            {
                var raced = await inner.TryActivateAsync(new WorkflowActivationSlotRequest(
                    workflowDefinitionId, targetSlotName, "racing-publication", source, expectedRevision, timeProvider.GetUtcNow()), cancellationToken);
                Assert.True(raced.Succeeded, raced.Diagnostic);
            }

            return await inner.TryDeactivateAsync(workflowDefinitionId, targetSlotName, source, expectedRevision, updatedAt, cancellationToken);
        }
    }
}
