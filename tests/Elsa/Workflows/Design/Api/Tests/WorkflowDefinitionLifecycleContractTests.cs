using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Tests.Support;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace Elsa.Workflows.Design.Api.Tests;

/// <summary>Preserves the canonical definition/draft/version lifecycle route and model contract.</summary>
public sealed class WorkflowDefinitionLifecycleContractTests
{
    public static TheoryData<string, string, string> LifecycleEndpoints => new()
    {
        { "Definitions.List", "GET", "design/workflows/definitions" },
        { "Definitions.Add", "POST", "design/workflows/definitions" },
        { "Definitions.Get", "GET", "design/workflows/definitions/{definitionId}" },
        { "Definitions.UpdateMetadata", "PATCH", "design/workflows/definitions/{definitionId}" },
        { "Definitions.SoftDelete", "DELETE", "design/workflows/definitions/{definitionId}" },
        { "Definitions.Restore", "POST", "design/workflows/definitions/{definitionId}/restore" },
        { "Definitions.DeletePermanently", "DELETE", "design/workflows/definitions/{definitionId}/permanent" },
        { "Drafts.Get", "GET", "design/workflows/drafts/{draftId}" },
        { "Drafts.Replace", "PUT", "design/workflows/drafts/{draftId}" },
        { "Drafts.Discard", "DELETE", "design/workflows/drafts/{draftId}" },
        { "Drafts.PromotionPreflight", "POST", "design/workflows/drafts/{draftId}/promotion-preflight" },
        { "Drafts.Promote", "POST", "design/workflows/drafts/{draftId}/promote" },
        { "Versions.Get", "GET", "design/workflows/versions/{versionId}" }
    };

    [Theory]
    [MemberData(nameof(LifecycleEndpoints))]
    public void Canonical_lifecycle_operation_has_its_domain_route(string endpointName, string verb, string route)
    {
        var endpoint = WorkflowDesignEndpointTestSupport.Find(WorkflowDesignEndpointTestSupport.MapEndpoints(), route, verb);
        var operation = endpointName switch
        {
            "Definitions.UpdateMetadata" => "DefinitionsUpdate",
            "Definitions.SoftDelete" => "DefinitionsDelete",
            _ => endpointName.Replace(".", string.Empty, StringComparison.Ordinal)
        };
        Assert.Equal($"ElsaWorkflowsDesignApiEndpoints{operation}", endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
        var security = Assert.IsType<EndpointSecurityDispositionMetadata>(endpoint.Metadata.GetMetadata<EndpointSecurityDispositionMetadata>());
        var expectedPermission = verb == "GET" ? WorkflowDesignPermissions.Read : WorkflowDesignPermissions.Manage;
        var policy = Assert.IsType<PermissionPolicyParseResult>(new PermissionPolicyCodec().Parse(security.Value!));
        Assert.Contains(PermissionKey.Normalize(expectedPermission), policy.Descriptor!.Permissions);
        Assert.NotNull(endpoint.Metadata.GetMetadata<AuthorizeAttribute>());
    }

    [Fact]
    public void Definition_creation_accepts_authored_state_and_layout_without_a_concrete_root_kind()
    {
        var properties = typeof(AddDefinition).GetProperties().Select(property => property.Name).ToArray();
        Assert.Contains("InitialState", properties);
        Assert.Contains("Layout", properties);
        Assert.DoesNotContain("RootKind", properties);
        Assert.DoesNotContain("RootActivityVersionId", properties);
    }

    [Fact]
    public void Definition_details_expose_the_current_draft_as_a_first_class_resource() =>
        Assert.Equal(typeof(WorkflowDraftView), typeof(WorkflowDefinitionDetailsView).GetProperty(nameof(WorkflowDefinitionDetailsView.Draft))?.PropertyType);
}
