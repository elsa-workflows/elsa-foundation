using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Capabilities;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

public sealed class WorkflowActivationSlotInspectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("runtime/workflows/activation-slots/{definitionId}", typeof(ListWorkflowActivationSlots), typeof(WorkflowActivationSlotListView))]
    [InlineData("runtime/workflows/activation-slots/{definitionId}/{slotName}", typeof(GetWorkflowActivationSlot), typeof(WorkflowActivationSlotView))]
    public void Endpoints_expose_authenticated_runtime_read_contracts(string route, Type request, Type response)
    {
        var endpoint = RuntimeApiEndpointTestFactory.FindByRoute(route);

        Assert.Equal((request, response), RuntimeApiEndpointTestFactory.Contract(endpoint));
        RuntimeApiEndpointTestFactory.AssertPermissionPolicy(endpoint, WorkflowRuntimePermissions.WorkflowRuntimeRead);
        Assert.Null(endpoint.Definition.AnonymousVerbs);
    }

    [Fact]
    public void Capabilities_advertise_activation_slot_relations_without_changing_the_major_version()
    {
        var links = RuntimeApiCapabilities.StaticDeclaration.Links.ToDictionary(link => link.Rel, StringComparer.Ordinal);

        Assert.Equal("runtime/workflows/activation-slots/{definitionId}", links["workflow-activation-slots"].Href);
        Assert.True(links["workflow-activation-slots"].Templated);
        Assert.Equal("runtime/workflows/activation-slots/{definitionId}/{slotName}", links["workflow-activation-slot"].Href);
        Assert.True(links["workflow-activation-slot"].Templated);
        Assert.DoesNotContain("publication-slots", links.Keys, StringComparer.Ordinal);
        Assert.Equal(1, RuntimeApiCapabilities.StaticDeclaration.ContractMajorVersion);
    }

    [Fact]
    public async Task List_projects_explicit_ownership_and_unknown_definitions_are_empty()
    {
        var authority = new InMemoryWorkflowActivationAuthority();
        await authority.TryActivateAsync(new WorkflowActivationSlotRequest(
            "definition-1",
            "default",
            "import:mounted-artifacts:1",
            WorkflowActivationSource.ArtifactReconciliation("mounted-artifacts"),
            0,
            Now));
        var inspection = new WorkflowActivationSlotInspectionService(authority);

        var listed = await inspection.ListAsync(new ListWorkflowActivationSlots("definition-1"), CancellationToken.None);
        var empty = await inspection.ListAsync(new ListWorkflowActivationSlots("definition-absent"), CancellationToken.None);

        var view = Assert.Single(listed.Items);
        Assert.Equal("default", view.SlotName);
        Assert.Equal("import:mounted-artifacts:1", view.ActiveActivationId);
        Assert.Equal(WorkflowActivationSource.ArtifactReconciliationKind, view.SourceKind);
        Assert.Equal("mounted-artifacts", view.SourceId);
        Assert.Equal(1, view.Revision);
        Assert.Empty(empty.Items);
    }

    [Fact]
    public async Task Get_projects_the_live_slot_and_missing_slots_are_not_found()
    {
        var authority = new InMemoryWorkflowActivationAuthority();
        await authority.TryActivateAsync(new WorkflowActivationSlotRequest(
            "definition-1",
            "default",
            "publication-1",
            WorkflowActivationSource.Publishing,
            0,
            Now));
        var inspection = new WorkflowActivationSlotInspectionService(authority);

        var view = await inspection.GetAsync(new GetWorkflowActivationSlot("definition-1", "default"), CancellationToken.None);

        Assert.Equal(WorkflowActivationSlotIdentity.Create("definition-1", "default"), view.SlotId);
        Assert.Equal("publication-1", view.ActiveActivationId);
        Assert.Equal(WorkflowActivationSource.PublishingKind, view.SourceKind);
        Assert.Null(view.SourceId);
        await Assert.ThrowsAsync<Elsa.Primitives.Exceptions.EntityNotFoundException>(() =>
            inspection.GetAsync(new GetWorkflowActivationSlot("definition-1", "blue"), CancellationToken.None));
    }
}
