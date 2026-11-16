using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Capabilities;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Options;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

public sealed class WorkflowActivationSlotInspectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Empty_listing_advertises_the_default_channel_creation_policy()
    {
        var inspection = new WorkflowActivationSlotInspectionService(new InMemoryWorkflowActivationAuthority());

        var listed = await inspection.ListAsync(new ListWorkflowActivationSlots("new-definition"), CancellationToken.None);
        var json = JsonSerializer.SerializeToElement(listed, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.True(json.GetProperty("allowAdHocChannels").GetBoolean());
        Assert.Empty(json.GetProperty("channels").EnumerateArray());
        Assert.Empty(json.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Declared_channels_are_global_and_slots_keep_definition_scoped_authority()
    {
        var authority = new InMemoryWorkflowActivationAuthority();
        await authority.TryActivateAsync(new("definition-1", "default", "publication-1", WorkflowActivationSource.Publishing, 0, Now));
        var inspection = new WorkflowActivationSlotInspectionService(authority, Options.Create(new ActivationChannelOptions
        {
            AllowAdHocChannels = false,
            Channels =
            [
                new() { Name = "default", DisplayName = "Production", Description = "Normal traffic" },
                new() { Name = "canary", DisplayName = "Canary", Description = "Separate routes required" }
            ]
        }));

        var listed = await inspection.ListAsync(new("definition-1"), CancellationToken.None);
        var empty = await inspection.ListAsync(new("definition-2"), CancellationToken.None);
        var slot = await inspection.GetAsync(new("definition-1", "default"), CancellationToken.None);

        Assert.False(listed.AllowAdHocChannels);
        Assert.Equal(["canary", "default"], listed.Channels.Select(channel => channel.Name));
        Assert.Equal(listed.Channels, empty.Channels);
        Assert.Empty(empty.Items);
        Assert.Equal("definition-1", Assert.Single(listed.Items).DefinitionId);
        Assert.Equal(new ActivationChannelView("default", "Production", "Normal traffic"), slot.Channel);
        Assert.Equal(slot.Channel, Assert.Single(listed.Items).Channel);
        Assert.Equal("publication-1", slot.ActiveActivationId);
    }

    [Fact]
    public async Task Existing_undeclared_slots_remain_readable_with_no_invented_channel_metadata()
    {
        var authority = new InMemoryWorkflowActivationAuthority();
        await authority.TryActivateAsync(new("definition-1", "legacy", "publication-1", WorkflowActivationSource.Publishing, 0, Now));
        var inspection = new WorkflowActivationSlotInspectionService(authority, Options.Create(new ActivationChannelOptions
        {
            AllowAdHocChannels = false,
            Channels = [new() { Name = "default" }]
        }));

        var listed = await inspection.ListAsync(new("definition-1"), CancellationToken.None);

        Assert.Null(Assert.Single(listed.Items).Channel);
        Assert.Equal("legacy", Assert.Single(listed.Items).SlotName);
        Assert.Equal("default", Assert.Single(listed.Channels).DisplayName);
        Assert.False(listed.AllowAdHocChannels);
    }

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
