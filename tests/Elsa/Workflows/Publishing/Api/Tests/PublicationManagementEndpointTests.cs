using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Api.Tests.Support;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Requests;
using Microsoft.AspNetCore.Authorization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class PublicationManagementEndpointTests
{
    public static TheoryData<string, string, string> ManagementEndpoints => new()
    {
        { "PreflightWorkflowPublicationSnapshotEndpoint", "publishing/workflows/preflight", WorkflowPublishingPermissions.Read },
        { "PreflightWorkflowPublicationEndpoint", "publishing/workflows/{versionId:regex(^(?!drafts$).+$)}/preflight", WorkflowPublishingPermissions.Read },
        { "PublishWorkflowEndpoint", "publishing/workflows/{versionId:regex(^(?!drafts$).+$)}/publish", WorkflowPublishingPermissions.Manage },
        { "UnpublishPublicationSlotEndpoint", "publishing/workflows/{definitionId}/slots/{slotName}", WorkflowPublishingPermissions.Manage },
        { "RestorePublicationSlotEndpoint", "publishing/workflows/{definitionId}/slots/{slotName}/restore", WorkflowPublishingPermissions.Manage },
        { "GetWorkflowPublicationPolicyEndpoint", "publishing/workflows/{definitionId}/policy", WorkflowPublishingPermissions.Read },
        { "SetWorkflowPublicationPolicyEndpoint", "publishing/workflows/{definitionId}/policy", WorkflowPublishingPermissions.Manage }
    };

    [Theory]
    [MemberData(nameof(ManagementEndpoints))]
    public void Canonical_endpoint_has_pinned_route_and_action_scoped_permission(
        string endpointName,
        string route,
        string permission)
    {
        var endpoint = PublishingMinimalApiTestSurface.Named(endpointName);

        Assert.Equal(route, endpoint.RoutePattern.RawText?.TrimStart('/'));
        var authorization = Assert.Single(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
        var parsed = new PermissionPolicyCodec().Parse(authorization.Policy!);
        Assert.Equal(PermissionPolicyParseStatus.Valid, parsed.Status);
        var descriptor = Assert.IsType<PermissionPolicyDescriptor>(parsed.Descriptor);
        Assert.Equal(PermissionRequirementMode.Single, descriptor.Mode);
        Assert.Equal(PermissionKey.Normalize(permission), Assert.Single(descriptor.Permissions));
    }

    [Fact]
    public void Snapshot_preflight_and_publish_expose_the_review_token_contract()
    {
        AssertProperties(typeof(PreflightWorkflowPublicationSnapshot),
            "DefinitionId", "State", "Layout", "Action", "SlotName", "ExpectedPublicationId");
        AssertProperties(typeof(PublicationSnapshotPreflightView),
            "PreflightToken", "CandidateHash", "DefinitionId", "VersionId", "SlotName", "ResolvedAction",
            "PolicySource", "PolicyRevision", "CanActivate", "Claims", "Triggers", "Conflicts");
        Assert.NotNull(typeof(PublishWorkflowRequest).GetProperty("PreflightToken"));
    }

    [Theory]
    [InlineData(PublicationPolicyDefaultActionView.Replace, "replace")]
    [InlineData(PublicationPolicyDefaultActionView.RequireExplicitSlot, "requireExplicitSlot")]
    public void Workflow_policy_uses_stable_public_action_names(
        PublicationPolicyDefaultActionView action,
        string expected)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(
            new PublicationPolicyView("definition-1", action, "default", PublicationPolicySourceView.Workflow, 1, DateTimeOffset.UnixEpoch),
            options));

        Assert.Equal(expected, document.RootElement.GetProperty("defaultAction").GetString());
    }

    [Theory]
    [InlineData(Elsa.Workflows.Publishing.Core.Models.PublicationStatus.Candidate, PublicationStatusView.Preparing)]
    [InlineData(Elsa.Workflows.Publishing.Core.Models.PublicationStatus.PendingProjection, PublicationStatusView.Pending)]
    [InlineData(Elsa.Workflows.Publishing.Core.Models.PublicationStatus.Active, PublicationStatusView.Active)]
    [InlineData(Elsa.Workflows.Publishing.Core.Models.PublicationStatus.Retired, PublicationStatusView.Retired)]
    [InlineData(Elsa.Workflows.Publishing.Core.Models.PublicationStatus.Failed, PublicationStatusView.Failed)]
    public void Internal_publication_statuses_map_to_the_stable_management_contract(
        Elsa.Workflows.Publishing.Core.Models.PublicationStatus status,
        PublicationStatusView expected) =>
        Assert.Equal(expected, PublicationContract.ToView(status));

    [Fact]
    public void Trigger_preflight_maps_internal_claims_to_the_stable_client_shape()
    {
        var claim = new Elsa.Workflows.Publishing.Core.Models.PublicationTriggerClaim(
            "claim-1", "publication-1", "artifact-1", "node-1", "Http", "/bar",
            Elsa.Workflows.Publishing.Core.Models.PublicationTriggerCardinality.Exclusive,
            new Dictionary<string, string>());
        var change = PublicationTriggerChangeView.From(new Elsa.Workflows.Publishing.Core.Models.PublicationTriggerChange(
            "Http", "/bar", Elsa.Workflows.Publishing.Core.Models.PublicationTriggerChangeKind.Added, claim));

        Assert.Equal("http:/bar", change.Key);
        Assert.Equal(PublicationTriggerChangeKindView.Added, change.Change);
        Assert.Equal(PublicationTriggerCardinalityView.Exclusive, change.Cardinality);
    }

    [Theory]
    [InlineData(PublicationActionView.Replace, "replace")]
    [InlineData(PublicationActionView.SideBySide, "sideBySide")]
    public void Publication_intent_uses_stable_public_action_names(PublicationActionView action, string expected)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(
            new PublishWorkflowRequest("version-1", action),
            options));

        Assert.Equal(expected, document.RootElement.GetProperty("action").GetString());
    }

    private static void AssertProperties(Type type, params string[] properties) =>
        Assert.Equal(properties, type.GetProperties().Select(property => property.Name));
}
