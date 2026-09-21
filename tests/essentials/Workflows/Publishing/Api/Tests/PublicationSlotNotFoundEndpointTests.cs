using System.Net;
using System.Text;
using Elsa.Workflows.Publishing.Api.Handlers;
using Elsa.Workflows.Publishing.Api.Tests.Support;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Exceptions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// The unpublish and restore routes answer 404 exactly when the slot lifecycle handler raises
/// <see cref="PublicationSlotNotFoundException"/>, through the real handlers over HTTP.
/// </summary>
/// <remarks>
/// The routes used to decide 404 by searching the exception message for "does not exist", "unavailable" and two other
/// phrases. That failed in both directions, and the dangerous one looks like success: a server fault whose diagnostic
/// merely mentions something being unavailable was reported as a 404, which a client reads as "nothing to do here".
/// Each former 404 path is pinned below, and so is that fault, which must stay a 500.
/// </remarks>
public sealed class PublicationSlotNotFoundEndpointTests : IAsyncLifetime
{
    // The scenario host activates this slot under the publishing source before any test runs.
    private const string ActiveDefinitionId = "definition-route";
    private const string ActivePublicationId = "publication-capture";
    private const string SlotName = "default";
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

    private PublishingMinimalApiScenarioHost _host = null!;

    public enum Route
    {
        Unpublish,
        Restore
    }

    public async Task InitializeAsync() => _host = await StartAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Theory]
    [InlineData(Route.Unpublish)]
    [InlineData(Route.Restore)]
    public async Task A_slot_that_does_not_exist_is_a_404(Route route)
    {
        using var response = await SendAsync(_host, route, "definition-without-slots");

        await AssertStatusAsync(HttpStatusCode.NotFound, response);
    }

    [Fact]
    public async Task Unpublishing_a_publication_whose_executable_is_gone_is_a_404()
    {
        await SavePublicationAsync(_host, ActiveDefinitionId, ActivePublicationId, "artifact-gone", PublicationStatus.Active);

        using var response = await SendAsync(_host, Route.Unpublish, ActiveDefinitionId);

        await AssertStatusAsync(HttpStatusCode.NotFound, response);
    }

    [Fact]
    public async Task Restoring_a_slot_with_no_retired_publication_is_a_404()
    {
        const string definitionId = "definition-never-retired";
        await VacateSlotAsync(definitionId);

        using var response = await SendAsync(_host, Route.Restore, definitionId);

        await AssertStatusAsync(HttpStatusCode.NotFound, response);
    }

    [Fact]
    public async Task Restoring_a_publication_whose_executable_is_gone_is_a_404()
    {
        const string definitionId = "definition-executable-gone";
        await VacateSlotAsync(definitionId);
        await SavePublicationAsync(_host, definitionId, "publication-retired", "artifact-gone", PublicationStatus.Retired);

        using var response = await SendAsync(_host, Route.Restore, definitionId);

        await AssertStatusAsync(HttpStatusCode.NotFound, response);
    }

    [Fact]
    public async Task Restoring_a_publication_with_no_source_reference_is_a_404()
    {
        const string definitionId = "definition-reference-gone";
        await VacateSlotAsync(definitionId);
        var artifactId = await SaveExecutableAsync(_host, definitionId);
        await SavePublicationAsync(_host, definitionId, "publication-retired", artifactId, PublicationStatus.Retired);

        using var response = await SendAsync(_host, Route.Restore, definitionId);

        await AssertStatusAsync(HttpStatusCode.NotFound, response);
    }

    [Fact]
    public async Task A_server_fault_whose_diagnostic_mentions_something_unavailable_stays_a_500()
    {
        await using var host = await StartAsync(services =>
        {
            services.RemoveAll<IWorkflowActivationAuthority>();
            services.AddSingleton<IWorkflowActivationAuthority>(new UnavailableDeactivationAuthority(new InMemoryWorkflowActivationAuthority()));
        });
        var artifactId = await SaveExecutableAsync(host, ActiveDefinitionId);
        await SavePublicationAsync(host, ActiveDefinitionId, ActivePublicationId, artifactId, PublicationStatus.Active);

        using var response = await SendAsync(host, Route.Unpublish, ActiveDefinitionId);

        await AssertStatusAsync(HttpStatusCode.InternalServerError, response);
    }

    [Theory]
    [InlineData(Route.Unpublish)]
    [InlineData(Route.Restore)]
    public async Task The_status_follows_the_exception_type_not_its_wording(Route route)
    {
        await using var host = await StartAsync(services =>
        {
            services.RemoveAll<IPublicationSlotUnpublisher>();
            services.RemoveAll<IPublicationSlotRestorer>();
            services.AddSingleton<ThrowingSlotLifecycle>();
            services.AddSingleton<IPublicationSlotUnpublisher>(sp => sp.GetRequiredService<ThrowingSlotLifecycle>());
            services.AddSingleton<IPublicationSlotRestorer>(sp => sp.GetRequiredService<ThrowingSlotLifecycle>());
        });

        using var response = await SendAsync(host, route, ActiveDefinitionId);

        await AssertStatusAsync(HttpStatusCode.NotFound, response);
    }

    /// <summary>
    /// The scenario host with the real slot lifecycle handlers in place of the capture stubs, and the trigger indexer
    /// the activation coordinator refuses to run without.
    /// </summary>
    private static Task<PublishingMinimalApiScenarioHost> StartAsync(Action<IServiceCollection>? configureServices = null) =>
        PublishingMinimalApiScenarioHost.StartAsync(configureServices: services =>
        {
            services.RemoveAll<IPublicationSlotUnpublisher>();
            services.RemoveAll<IPublicationSlotRestorer>();
            services.AddScoped<IPublicationSlotUnpublisher, UnpublishPublicationSlotRequestHandler>();
            services.AddScoped<IPublicationSlotRestorer, RestorePublicationSlotRequestHandler>();
            services.TryAddScoped<IWorkflowTriggerIndexer, WorkflowTriggerIndexer>();
            configureServices?.Invoke(services);
        });

    /// <summary>Leaves a slot that exists but serves nothing, with no publication record of its own.</summary>
    private async Task VacateSlotAsync(string definitionId)
    {
        var authority = _host.Services.GetRequiredService<IWorkflowActivationAuthority>();
        var activated = await authority.TryActivateAsync(new WorkflowActivationSlotRequest(
            definitionId, SlotName, "activation-vacated", WorkflowActivationSource.Publishing, 0, Now));
        Assert.True(activated.Succeeded, activated.Diagnostic);
        var vacated = await authority.TryDeactivateAsync(definitionId, SlotName, WorkflowActivationSource.Publishing, activated.Slot.Revision, Now);
        Assert.True(vacated.Succeeded, vacated.Diagnostic);
    }

    private static async Task<string> SaveExecutableAsync(PublishingMinimalApiScenarioHost host, string definitionId)
    {
        var executable = TestExecutable.Create(TestExecutable.Identity($"artifact-{definitionId}", $"hash-{definitionId}", definitionId, $"version-{definitionId}"));
        await host.Services.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable);
        return executable.Identity.ArtifactId;
    }

    private static async Task SavePublicationAsync(
        PublishingMinimalApiScenarioHost host,
        string definitionId,
        string publicationId,
        string artifactId,
        PublicationStatus status) =>
        await host.Services.GetRequiredService<IPublicationRecordStore>().SaveAsync(new PublicationRecord(
            publicationId,
            WorkflowActivationSlotIdentity.Create(definitionId, SlotName),
            definitionId,
            $"version-{definitionId}",
            artifactId,
            SourceReferenceId: null,
            ExpectedSlotRevision: 0,
            status,
            Now,
            ActivatedAt: Now,
            RetiredAt: status == PublicationStatus.Retired ? Now : null,
            Failure: null,
            SlotName));

    private static Task<HttpResponseMessage> SendAsync(PublishingMinimalApiScenarioHost host, Route route, string definitionId)
    {
        var request = route == Route.Unpublish
            ? new HttpRequestMessage(HttpMethod.Delete, $"/publishing/workflows/{definitionId}/slots/{SlotName}")
            : new HttpRequestMessage(HttpMethod.Post, $"/publishing/workflows/{definitionId}/slots/{SlotName}/restore")
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        request.Headers.TryAddWithoutValidation(PublishingCompatibilityCases.IdentityHeader, "trusted-success");
        return host.Client.SendAsync(request);
    }

    private static async Task AssertStatusAsync(HttpStatusCode expected, HttpResponseMessage response) =>
        Assert.True(expected == response.StatusCode, $"Expected {(int)expected}, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

    /// <summary>
    /// Fails every deactivation the way an unreachable authority would. The coordinator folds the message into its
    /// diagnostic, and the unpublish handler folds that into its own failure message.
    /// </summary>
    private sealed class UnavailableDeactivationAuthority(IWorkflowActivationAuthority inner) : IWorkflowActivationAuthority
    {
        public ValueTask<WorkflowActivationSlot?> FindAsync(string workflowDefinitionId, string slotName, CancellationToken cancellationToken = default) =>
            inner.FindAsync(workflowDefinitionId, slotName, cancellationToken);

        public ValueTask<IReadOnlyCollection<WorkflowActivationSlot>> ListByDefinitionAsync(string workflowDefinitionId, CancellationToken cancellationToken = default) =>
            inner.ListByDefinitionAsync(workflowDefinitionId, cancellationToken);

        public ValueTask<WorkflowActivationTransition> TryActivateAsync(WorkflowActivationSlotRequest request, CancellationToken cancellationToken = default) =>
            inner.TryActivateAsync(request, cancellationToken);

        public ValueTask<WorkflowActivationTransition> TryDeactivateAsync(
            string workflowDefinitionId,
            string slotName,
            WorkflowActivationSource source,
            long expectedRevision,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The activation authority is unavailable, so the slot does not exist to it right now.");
    }

    /// <summary>Raises the not-found type with wording that shares nothing with the handlers' own messages.</summary>
    private sealed class ThrowingSlotLifecycle : IPublicationSlotUnpublisher, IPublicationSlotRestorer
    {
        public Task<WorkflowActivationSlot> UnpublishAsync(string workflowDefinitionId, string slotName, CancellationToken cancellationToken) =>
            throw new PublicationSlotNotFoundException("Nothing to act on here.");

        public Task<WorkflowActivationSlot> RestoreAsync(string workflowDefinitionId, string slotName, CancellationToken cancellationToken) =>
            throw new PublicationSlotNotFoundException("Nothing to act on here.");
    }
}
