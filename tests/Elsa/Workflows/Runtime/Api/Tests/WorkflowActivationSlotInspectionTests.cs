using System.Reflection;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Runtime.Api.Capabilities;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services;
using FastEndpoints;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

/// <summary>
/// T117: the activation ledger is read here, and only read here.
/// </summary>
/// <remarks>
/// The slot is a runtime concept (§E2.2), so Runtime serves activation slots and Publishing serves publications.
/// These endpoints replace <c>ListPublicationSlotsEndpoint</c> / <c>GetPublicationSlotEndpoint</c>, which injected
/// <see cref="IWorkflowActivationAuthority"/> from inside the publishing API.
/// </remarks>
public sealed class WorkflowActivationSlotInspectionTests
{
    private const string SlotsRoute = "runtime/workflows/activation-slots/{definitionId}";
    private const string SlotRoute = "runtime/workflows/activation-slots/{definitionId}/{slotName}";
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(SlotsRoute, typeof(ListWorkflowActivationSlots), typeof(WorkflowActivationSlotListView))]
    [InlineData(SlotRoute, typeof(GetWorkflowActivationSlot), typeof(WorkflowActivationSlotView))]
    public void Endpoints_expose_authenticated_runtime_read_contracts(string route, Type request, Type response)
    {
        var endpoint = RuntimeApiEndpointTestFactory.FindByRoute(route);

        Assert.Equal((request, response), RuntimeApiEndpointTestFactory.Contract(endpoint));
        RuntimeApiEndpointTestFactory.AssertPermissionPolicy(endpoint, PermissionNames.WorkflowRuntimeRead);
        Assert.Null(endpoint.Definition.AnonymousVerbs);
        Assert.Equal(["GET"], endpoint.Definition.Verbs);
    }

    [Fact]
    public void Capabilities_advertise_the_moved_relations_without_changing_the_major_version()
    {
        var links = RuntimeApiCapabilities.StaticDeclaration.Links.ToDictionary(link => link.Rel, StringComparer.Ordinal);

        Assert.Equal(SlotsRoute, links["workflow-activation-slots"].Href);
        Assert.True(links["workflow-activation-slots"].Templated);
        Assert.Equal(SlotRoute, links["workflow-activation-slot"].Href);
        Assert.True(links["workflow-activation-slot"].Templated);
        Assert.DoesNotContain("publication-slots", links.Keys, StringComparer.Ordinal);
        Assert.Equal(1, RuntimeApiCapabilities.StaticDeclaration.ContractMajorVersion);
    }

    [Fact]
    public async Task List_projects_explicit_ownership_and_answers_an_unknown_definition_with_an_empty_list()
    {
        var authority = new InMemoryWorkflowActivationAuthority();
        await authority.TryActivateAsync(new WorkflowActivationSlotRequest(
            "definition-1",
            "default",
            "import:mounted-artifacts:1",
            WorkflowActivationSource.ArtifactReconciliation("mounted-artifacts"),
            0,
            Now));
        var handler = new ListWorkflowActivationSlotsRequestHandler(authority);

        var listed = await handler.Handle(new ListWorkflowActivationSlots("definition-1"), CancellationToken.None);
        var empty = await handler.Handle(new ListWorkflowActivationSlots("definition-absent"), CancellationToken.None);

        var view = Assert.Single(listed.Items);
        Assert.Equal("default", view.SlotName);
        Assert.Equal("import:mounted-artifacts:1", view.ActiveActivationId);
        // Ownership is read from the slot's explicit source field, never parsed out of the activation id (P3).
        Assert.Equal(WorkflowActivationSource.ArtifactReconciliationKind, view.SourceKind);
        Assert.Equal("mounted-artifacts", view.SourceId);
        Assert.Equal(1, view.Revision);
        Assert.Empty(empty.Items);
    }

    [Fact]
    public async Task Get_renders_the_live_slot_and_reports_an_absent_one_as_not_found()
    {
        var authority = new InMemoryWorkflowActivationAuthority();
        await authority.TryActivateAsync(new WorkflowActivationSlotRequest(
            "definition-1",
            "default",
            "publication-1",
            WorkflowActivationSource.Publishing,
            0,
            Now));
        var handler = new GetWorkflowActivationSlotRequestHandler(authority);

        var view = await handler.Handle(new GetWorkflowActivationSlot("definition-1", "default"), CancellationToken.None);

        Assert.Equal(WorkflowActivationSlotIdentity.Create("definition-1", "default"), view.SlotId);
        Assert.Equal("publication-1", view.ActiveActivationId);
        Assert.Equal(WorkflowActivationSource.PublishingKind, view.SourceKind);
        Assert.Null(view.SourceId);
        // A missing slot is a domain not-found the shared endpoint base renders as 404, never a raw fault (§2.23.5).
        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            handler.Handle(new GetWorkflowActivationSlot("definition-1", "blue"), CancellationToken.None));
    }

    /// <summary>
    /// The activation surface exposed by Runtime.Api is read-only, and that is a pinned decision rather than an
    /// oversight (T117).
    /// </summary>
    /// <remarks>
    /// A runtime-only engine composes no publishing and therefore has no external deactivation surface at all; it
    /// re-reconciles through a shell reload (FR-B-008). Adding slot reads is precisely the context in which a
    /// <c>DELETE</c> looks like the obvious next endpoint, so the absence is asserted rather than assumed. The
    /// second half is the one that actually bites: <c>IWorkflowActivationCoordinator</c> owns deactivation, and
    /// this API must never take a dependency on it.
    /// </remarks>
    [Fact]
    public void Runtime_api_exposes_no_activation_mutation_and_never_injects_the_coordinator()
    {
        var apiTypes = typeof(WorkflowsRuntimeApiFeature).Assembly.GetTypes();
        var activationRoutes = apiTypes
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(BaseEndpoint).IsAssignableFrom(type))
            .Select(type => RuntimeApiEndpointTestFactory.Create(type))
            .Where(endpoint => endpoint.Definition.Routes!.Any(route =>
                route.Contains("activation-slots", StringComparison.Ordinal)))
            .ToArray();
        var coordinatorConsumers = apiTypes
            .SelectMany(type => type
                .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .SelectMany(constructor => constructor.GetParameters())
                .Concat(type
                    .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                    .SelectMany(method => method.GetParameters()))
                .Select(parameter => (Type: type, parameter.ParameterType)))
            .Where(candidate => candidate.ParameterType == typeof(IWorkflowActivationCoordinator))
            .Select(candidate => candidate.Type.FullName!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // The traversal must actually have found the two reads, or every assertion below is vacuous.
        Assert.Equal(2, activationRoutes.Length);
        Assert.All(activationRoutes, endpoint => Assert.Equal(["GET"], endpoint.Definition.Verbs));
        Assert.Empty(coordinatorConsumers);
    }
}
