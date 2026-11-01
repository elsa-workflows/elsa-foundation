using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Elsa.Api.Capabilities.Contracts;
using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Tasks.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using Host = Elsa.Workflows.Design.Api.Tests.WorkflowsDesignApiContractTests.AuthorizationHost;

namespace Elsa.Workflows.Design.Api.Tests;

public sealed class WorkflowLibraryApiContractTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Organization_setting_controls_organization_relations_without_removing_paged_browsing(bool? configured, bool enabled)
    {
        await using var host = await StartAsync(new LibraryProbe(), organizationEnabled: configured);
        var relations = await RelationsAsync(host);
        Assert.Contains("workflow-definitions-page", relations);
        foreach (var relation in new[] { "workflow-folders", "workflow-folder-rename", "workflow-folder-move",
                     "workflow-folder-delete-empty", "workflow-definition-folder-move", "workflow-organization",
                     "workflow-tags", "workflow-tag", "workflow-tags-bulk", "workflow-views", "workflow-view", "workflow-favorites" })
            Assert.Equal(enabled, relations.Contains(relation));
    }

    [Theory]
    [InlineData("GET", "/design/workflows/folders", null)]
    [InlineData("GET", "/design/workflows/folders/invoices", null)]
    [InlineData("POST", "/design/workflows/folders", "{\"name\":\"New folder\"}")]
    [InlineData("POST", "/design/workflows/folders/invoices/rename", "{\"name\":\"Renamed\",\"revision\":\"f1\"}")]
    [InlineData("POST", "/design/workflows/folders/invoices/move", "{\"parentId\":null,\"revision\":\"f1\"}")]
    [InlineData("DELETE", "/design/workflows/folders/invoices?revision=f1", null)]
    [InlineData("POST", "/design/workflows/definitions/move", "{\"definitionIds\":[\"orders\"],\"folderId\":\"invoices\"}")]
    [InlineData("GET", "/design/workflows/definitions/orders/organization", null)]
    [InlineData("PUT", "/design/workflows/definitions/orders/organization", "{\"tags\":[],\"revision\":\"r1\"}")]
    [InlineData("GET", "/design/workflows/tags", null)]
    [InlineData("GET", "/design/workflows/tags/critical", null)]
    [InlineData("POST", "/design/workflows/tags", "{\"name\":\"New tag\"}")]
    [InlineData("PUT", "/design/workflows/tags/critical", "{\"name\":\"Renamed\",\"revision\":\"t1\"}")]
    [InlineData("POST", "/design/workflows/tags/bulk", "{\"targets\":[{\"definitionId\":\"orders\",\"revision\":\"r1\"}],\"add\":[],\"remove\":[]}")]
    [InlineData("GET", "/design/workflows/views", null)]
    [InlineData("POST", "/design/workflows/views", "{\"name\":\"New view\",\"query\":{}}")]
    [InlineData("PUT", "/design/workflows/views/view-1", "{\"name\":\"Renamed\",\"query\":{},\"revision\":\"v1\"}")]
    [InlineData("DELETE", "/design/workflows/views/view-1?revision=v1", null)]
    [InlineData("PUT", "/design/workflows/definitions/orders/favorite", "{}")]
    [InlineData("DELETE", "/design/workflows/definitions/orders/favorite", null)]
    public async Task Disabled_organization_refuses_stale_operations_before_store_mutations(string method, string path, string? body)
    {
        var probe = new LibraryProbe();
        await using var host = await StartAsync(probe, organizationEnabled: false);
        using var response = await SendAsync(host, method, path, "library-all", body);
        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        Assert.Contains("disabled", await response.Content.ReadAsStringAsync());
        Assert.Null(probe.Actor);
        Assert.Null(probe.Subject);
        Assert.Null(probe.DefinitionId);
        Assert.Null(probe.TagId);
        Assert.Null(probe.Bulk);
        Assert.False(probe.ListedTags);
    }

    [Theory]
    [InlineData("folderId=invoices")]
    [InlineData("unfiled=true")]
    [InlineData("includeDescendants=true")]
    [InlineData("favoritesOnly=true")]
    [InlineData("tagFilters=%5B%7B%22tagId%22%3A%22critical%22%2C%22operator%22%3A%22exists%22%7D%5D")]
    public async Task Disabled_organization_filters_cannot_become_a_broader_page(string query)
    {
        var probe = new LibraryProbe();
        await using var host = await StartAsync(probe, organizationEnabled: false);
        using var response = await SendAsync(host, "GET", $"/design/workflows/definitions/page?{query}", "library-read");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("disabled", await response.Content.ReadAsStringAsync());
        Assert.Null(probe.Query);
    }

    [Fact]
    public async Task Disabled_organization_preserves_ordinary_paging_and_visibility_without_organization_projection()
    {
        var probe = new LibraryProbe();
        await using var host = await StartAsync(probe, organizationEnabled: false);
        using var response = await SendAsync(host, "GET", "/design/workflows/definitions/page?searchTerm=invoice&sort=modified&publicationState=published&pageSize=25", "library-all");
        response.EnsureSuccessStatusCode();
        Assert.Equal("invoice", probe.Query!.SearchTerm);
        Assert.Equal("modified", probe.Query.Sort);
        Assert.Equal("published", probe.Query.PublicationState);
        Assert.Equal("test-tenant-action-v1", probe.ReadScope!.Revision);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var page = document.RootElement;
        Assert.Equal(123, page.GetProperty("totalCount").GetInt32());
        Assert.Equal("next-page", page.GetProperty("nextContinuationToken").GetString());
        var row = Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.False(row.TryGetProperty("folderId", out var folder) && folder.ValueKind != JsonValueKind.Null);
        Assert.False(row.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array && tags.GetArrayLength() > 0);
        Assert.False(row.GetProperty("favorite").GetBoolean());
        Assert.All(page.GetProperty("permissions").EnumerateObject(), permission => Assert.False(permission.Value.GetBoolean()));
    }

    [Fact]
    public async Task Disabled_organization_does_not_bypass_folder_resource_authorization()
    {
        var probe = new LibraryProbe { IncludeHiddenFolder = true };
        await using var host = await StartAsync(probe,
            services => services.AddSingleton<IPermissionResourceHandler, DeniedFolderPolicy>(), organizationEnabled: false);
        using var page = await SendAsync(host, "GET", "/design/workflows/definitions/page", "library-read");
        Assert.Equal(HttpStatusCode.Forbidden, page.StatusCode);
        using var details = await SendAsync(host, "GET", "/design/workflows/definitions/orders", "library-read");
        Assert.Equal(HttpStatusCode.Forbidden, details.StatusCode);
    }

    [Fact]
    public async Task Disabled_organization_refuses_folder_placement_before_creation_but_keeps_ordinary_creation()
    {
        var add = new LibraryAddCommand();
        await using var host = await StartAsync(new LibraryProbe(), services =>
        {
            services.RemoveAll<IAddWorkflowDefinitionCommand>();
            services.AddSingleton<IAddWorkflowDefinitionCommand>(add);
        }, organizationEnabled: false);
        using var scoped = await SendAsync(host, "POST", "/design/workflows/definitions", "library-all",
            "{\"name\":\"Invoice\",\"folderId\":\"invoices\"}");
        Assert.Equal(HttpStatusCode.NotImplemented, scoped.StatusCode);
        Assert.Contains("disabled", await scoped.Content.ReadAsStringAsync());
        Assert.False(add.Executed);
        using var ordinary = await SendAsync(host, "POST", "/design/workflows/definitions", "library-all", "{\"name\":\"Invoice\"}");
        ordinary.EnsureSuccessStatusCode();
        Assert.True(add.Executed);
        Assert.DoesNotContain("folderBreadcrumb", await ordinary.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("folderId=invoices")]
    [InlineData("favoritesOnly=true")]
    [InlineData("tagFilters=%5B%5D")]
    public async Task Disabled_organization_refuses_scoped_legacy_list_requests(string query)
    {
        await using var host = await StartAsync(new LibraryProbe(), organizationEnabled: false);
        using var response = await SendAsync(host, "GET", $"/design/workflows/definitions?{query}", "library-read");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("disabled", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Disabled_organization_omits_metadata_from_details_without_losing_the_definition()
    {
        await using var host = await StartAsync(new LibraryProbe(), organizationEnabled: false);
        using var response = await SendAsync(host, "GET", "/design/workflows/definitions/orders", "library-read");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"id\":\"sample-definition\"", body);
        Assert.DoesNotContain("folderBreadcrumb", body);
        Assert.DoesNotContain("critical", body);
    }

    [Fact]
    public void Organization_setting_has_a_true_default_and_boolean_manifest_metadata()
    {
        var feature = new WorkflowsDesignApiFeature();
        Assert.True(feature.WorkflowOrganizationEnabled);
        var property = typeof(WorkflowsDesignApiFeature).GetProperty(nameof(feature.WorkflowOrganizationEnabled))!;
        Assert.Equal(typeof(bool), property.PropertyType);
        var hint = Assert.Single(property.GetCustomAttributesData(), attribute => attribute.AttributeType.Name == "ManifestSettingAttribute");
        Assert.Equal("true", Assert.Single(hint.NamedArguments, argument => argument.MemberName == "DefaultValue").TypedValue.Value);
        Assert.Equal("Workflow organization", Assert.Single(hint.NamedArguments, argument => argument.MemberName == "DisplayName").TypedValue.Value);
    }

    [Theory]
    [InlineData("GET", "/design/workflows/folders", null)]
    [InlineData("GET", "/design/workflows/definitions/page", null)]
    [InlineData("GET", "/design/workflows/definitions/orders/organization", null)]
    [InlineData("POST", "/design/workflows/folders", "{\"name\":\"Finance\"}")]
    [InlineData("PUT", "/design/workflows/definitions/orders/favorite", "{}")]
    public async Task Optional_library_routes_fail_truthfully_when_backend_is_absent(string method, string path, string? body)
    {
        await using var host = await Host.StartAsync();
        using var response = await SendAsync(host, method, path, "library-all", body);
        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        Assert.Contains("not supported", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Library_capability_is_present_only_when_the_store_is_composed()
    {
        await using var absent = await Host.StartAsync();
        await using var present = await StartAsync(new LibraryProbe());
        Assert.DoesNotContain("workflow-folders", await RelationsAsync(absent));
        var relations = await RelationsAsync(present);
        Assert.Contains("workflow-folders", relations);
        Assert.Contains("workflow-definitions-page", relations);
        Assert.Contains("workflow-organization", relations);
        Assert.Contains("workflow-views", relations);
        Assert.Contains("workflow-favorites", relations);
    }

    [Fact]
    public async Task Paged_route_forwards_every_membership_predicate_and_preserves_authoritative_projection()
    {
        var probe = new LibraryProbe();
        await using var host = await StartAsync(probe, services => services.AddSingleton<IWorkflowDefinitionPublicationStateLookup>(
            new PublicationLookup(new Dictionary<string, WorkflowDefinitionPublicationSummary> { ["orders"] = new(WorkflowDefinitionPublicationState.Published, "version-published") })));
        var filters = Uri.EscapeDataString("[{\"tagId\":\"critical\",\"operator\":\"exists\"}]");
        using var response = await SendAsync(host, "GET",
            $"/design/workflows/definitions/page?searchTerm=invoice&state=all&folderId=finance&includeDescendants=true&favoritesOnly=true&sort=modified&pageSize=25&continuationToken=next&publicationState=published&tagFilters={filters}", "library-read");
        response.EnsureSuccessStatusCode();
        Assert.Equal("user-a", probe.Subject);
        Assert.Equal("invoice", probe.Query!.SearchTerm);
        Assert.Equal("finance", probe.Query.FolderId);
        Assert.True(probe.Query.IncludeDescendants);
        Assert.True(probe.Query.FavoritesOnly);
        Assert.Equal("published", probe.Query.PublicationState);
        Assert.Equal("critical", Assert.Single(probe.Query.TagFilters!).TagId);
        Assert.Equal("modified", probe.Query.Sort);
        Assert.Equal(25, probe.Query.PageSize);
        Assert.Equal("next", probe.Query.ContinuationToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var page = document.RootElement;
        Assert.Equal(123, page.GetProperty("totalCount").GetInt32());
        var row = Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal("invoices", row.GetProperty("folderId").GetString());
        Assert.Equal("version-published", row.GetProperty("publishedVersionId").GetString());
        Assert.Equal("version-latest", row.GetProperty("latestVersionId").GetString());
        Assert.True(row.GetProperty("favorite").GetBoolean());
        Assert.False(page.GetProperty("permissions").GetProperty("canAssignTags").GetBoolean());
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[null]")]
    public async Task Malformed_tag_filter_never_becomes_an_unfiltered_query(string filter)
    {
        var probe = new LibraryProbe();
        await using var host = await StartAsync(probe);
        using var response = await SendAsync(host, "GET", $"/design/workflows/definitions/page?tagFilters={Uri.EscapeDataString(filter)}", "library-read");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(probe.Query);
    }

    [Theory]
    [InlineData(WorkflowDefinitionPublicationState.Published, "published")]
    [InlineData(WorkflowDefinitionPublicationState.Retracted, "retracted")]
    [InlineData(WorkflowDefinitionPublicationState.Retired, "retired")]
    [InlineData(WorkflowDefinitionPublicationState.Draft, "draft")]
    public async Task Page_enriches_current_publication_without_reading_workflow_versions_or_changing_membership(
        WorkflowDefinitionPublicationState state, string expectedState)
    {
        var probe = new LibraryProbe();
        var lookup = new PublicationLookup(new Dictionary<string, WorkflowDefinitionPublicationSummary>
        {
            ["orders"] = new(state, state == WorkflowDefinitionPublicationState.Published ? "version-published" : null)
        });
        await using var host = await StartAsync(probe, services => services.AddSingleton<IWorkflowDefinitionPublicationStateLookup>(lookup));
        using var response = await SendAsync(host, "GET", "/design/workflows/definitions/page", "library-read");
        response.EnsureSuccessStatusCode();
        Assert.Equal(["orders"], lookup.RequestedIds);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var row = Assert.Single(document.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(expectedState, row.GetProperty("publicationState").GetString());
        Assert.Equal("version-latest", row.GetProperty("latestVersionId").GetString());
        if (state == WorkflowDefinitionPublicationState.Published)
        {
            Assert.Equal("version-published", row.GetProperty("publishedVersionId").GetString());
            Assert.Equal("1.0.0", row.GetProperty("publishedVersion").GetString());
            Assert.Equal(["version-published"], probe.RequestedVersionIds);
        }
        else
        {
            Assert.False(row.TryGetProperty("publishedVersionId", out _));
            Assert.Null(probe.RequestedVersionIds);
        }
        Assert.Equal(123, document.RootElement.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Page_keeps_publication_unknown_when_lookup_has_no_evidence()
    {
        var probe = new LibraryProbe();
        await using var host = await StartAsync(probe);
        using var response = await SendAsync(host, "GET", "/design/workflows/definitions/page", "library-read");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var row = Assert.Single(document.RootElement.GetProperty("items").EnumerateArray());
        Assert.False(row.TryGetProperty("publicationState", out _));
        Assert.False(row.TryGetProperty("publishedVersionId", out _));
        Assert.Null(probe.RequestedVersionIds);
    }

    [Fact]
    public async Task Deleted_workflow_organization_is_readable_with_assignment_and_move_actions_disabled()
    {
        await using var host = await StartAsync(new LibraryProbe(), services => services.AddSingleton<IWorkflowDefinitionStore, DeletedDefinitionStore>());
        using var response = await SendAsync(host, "GET", "/design/workflows/definitions/orders/organization", "library-all");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var permissions = document.RootElement.GetProperty("permissions");
        Assert.False(permissions.GetProperty("canAssignTags").GetBoolean());
        Assert.False(permissions.GetProperty("canManageFolders").GetBoolean());
        Assert.True(permissions.GetProperty("canManageTags").GetBoolean());
    }

    [Theory]
    [InlineData("{\"definitionId\":\"orders\"}")]
    [InlineData("{\"definitionId\":\"orders\",\"revision\":null}")]
    [InlineData("null")]
    public async Task Bulk_request_requires_a_revision_for_every_target_before_any_store_mutation(string target)
    {
        var probe = new LibraryProbe();
        await using var host = await StartAsync(probe);
        using var response = await SendAsync(host, "POST", "/design/workflows/tags/bulk", "library-assign",
            "{\"targets\":[" + target + "],\"add\":[],\"remove\":[],\"operationKey\":\"invalid-revision\"}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(probe.Bulk);
    }

    [Fact]
    public async Task Folder_and_organization_routes_bind_path_and_take_actor_from_the_principal()
    {
        var probe = new LibraryProbe();
        await using var host = await StartAsync(probe);
        using var create = await SendAsync(host, "POST", "/design/workflows/folders", "library-all",
            "{\"name\":\"Invoices\",\"parentId\":\"finance\",\"actorId\":\"forged\"}");
        create.EnsureSuccessStatusCode();
        Assert.Equal("user-a", probe.Actor);
        Assert.Equal("finance", probe.FolderId);
        using var replace = await SendAsync(host, "PUT", "/design/workflows/definitions/orders/organization", "library-all",
            "{\"tags\":[{\"tagId\":\"critical\"}],\"revision\":\"r1\",\"operationKey\":\"edit-1\",\"actorId\":\"forged\"}");
        replace.EnsureSuccessStatusCode();
        Assert.Equal("orders", probe.DefinitionId);
        Assert.Equal("r1", probe.Revision);
        Assert.Equal("edit-1", probe.OperationKey);
        Assert.Equal("user-a", probe.Actor);
        using var document = JsonDocument.Parse(await replace.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("permissions").GetProperty("canAssignTags").GetBoolean());
    }

    [Fact]
    public async Task Read_permission_allows_private_views_and_favorites_but_does_not_grant_tag_or_share_mutation()
    {
        var probe = new LibraryProbe();
        await using var host = await StartAsync(probe);
        using var favorite = await SendAsync(host, "PUT", "/design/workflows/definitions/orders/favorite", "library-read-other", "{}");
        Assert.Equal(HttpStatusCode.NoContent, favorite.StatusCode);
        Assert.Equal("user-b", probe.Subject);
        using var savePrivate = await SendAsync(host, "POST", "/design/workflows/views", "library-read",
            "{\"name\":\"My critical workflows\",\"query\":{},\"ownerId\":\"forged\"}");
        savePrivate.EnsureSuccessStatusCode();
        Assert.Equal("user-a", probe.Subject);
        Assert.False(probe.CanShare);
        using var saveShared = await SendAsync(host, "POST", "/design/workflows/views", "library-read",
            "{\"name\":\"Shared\",\"query\":{},\"shared\":true}");
        Assert.Equal(HttpStatusCode.Forbidden, saveShared.StatusCode);
        using var editTags = await SendAsync(host, "PUT", "/design/workflows/definitions/orders/organization", "library-read",
            "{\"tags\":[],\"revision\":\"r1\"}");
        Assert.Equal(HttpStatusCode.Forbidden, editTags.StatusCode);
        using var createTag = await SendAsync(host, "POST", "/design/workflows/tags", "library-assign", "{\"name\":\"Critical\"}");
        Assert.Equal(HttpStatusCode.Forbidden, createTag.StatusCode);
    }

    [Fact]
    public async Task Personal_operations_refuse_an_authenticated_principal_without_a_subject()
    {
        var probe = new LibraryProbe();
        await using var host = await StartAsync(probe);
        using var response = await SendAsync(host, "PUT", "/design/workflows/definitions/orders/favorite", "library-no-subject", "{}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(probe.Subject);
    }

    [Fact]
    public async Task Bulk_edits_enforce_each_target_policy_and_do_not_probe_denied_definitions()
    {
        var probe = new LibraryProbe();
        await using var host = await StartAsync(probe, services => services.AddSingleton<IPermissionResourceHandler, DeniedDefinitionPolicy>());
        using var response = await SendAsync(host, "POST", "/design/workflows/tags/bulk", "library-assign",
            "{\"targets\":[{\"definitionId\":\"orders\",\"revision\":\"r1\"},{\"definitionId\":\"hidden\",\"revision\":\"r1\"}],\"add\":[{\"tagId\":\"critical\"}],\"remove\":[],\"operationKey\":\"bulk-1\"}");
        response.EnsureSuccessStatusCode();
        Assert.Equal("orders", Assert.Single(probe.Bulk!.Targets).DefinitionId);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(["success", "denied"], document.RootElement.EnumerateArray().Select(row => row.GetProperty("status").GetString()!).ToArray());
    }

    [Fact]
    public async Task Atomic_moves_check_every_target_before_calling_the_store()
    {
        var probe = new LibraryProbe();
        await using var host = await StartAsync(probe, services => services.AddSingleton<IPermissionResourceHandler, DeniedDefinitionPolicy>());
        using var response = await SendAsync(host, "POST", "/design/workflows/definitions/move", "library-all",
            "{\"definitionIds\":[\"orders\",\"hidden\"],\"folderId\":\"invoices\"}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(probe.MovedIds);
    }

    [Fact]
    public async Task A_page_with_a_resource_policy_refusal_never_returns_rows_or_a_count()
    {
        var probe = new LibraryProbe { PageDefinitionId = "hidden" };
        await using var host = await StartAsync(probe, services => services.AddSingleton<IPermissionResourceHandler, DeniedDefinitionPolicy>());
        using var response = await SendAsync(host, "GET", "/design/workflows/definitions/page", "library-read");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("totalCount", body);
        Assert.DoesNotContain("hidden", body);
    }

    [Fact]
    public async Task Resource_handlers_without_query_visibility_fail_before_any_inventory_query_or_count()
    {
        var probe = new LibraryProbe { CandidateIds = ["visibleAlpha", "deniedBeta"] };
        await using var host = await StartAsync(probe, services =>
        {
            services.RemoveAll<IWorkflowLibraryVisibilityPolicy>();
            services.AddSingleton<IPermissionResourceHandler, DeniedDefinitionPolicy>();
        });
        using var response = await SendAsync(host, "GET", "/design/workflows/definitions/page?pageSize=1", "library-read");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(probe.Query);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("totalCount", body);
        Assert.DoesNotContain("nextContinuationToken", body);
        Assert.DoesNotContain("deniedBeta", body);
    }

    [Fact]
    public async Task Trusted_query_visibility_reaches_the_store_before_paging_and_exposes_only_the_authorized_total()
    {
        var probe = new LibraryProbe { CandidateIds = ["visibleAlpha", "deniedBeta"] };
        var scope = new WorkflowLibraryReadScope("policy-r2", DeniedDefinitionIds: ["deniedBeta"]);
        await using var host = await StartAsync(probe, services =>
        {
            services.RemoveAll<IWorkflowLibraryVisibilityPolicy>();
            services.AddSingleton<IWorkflowLibraryVisibilityPolicy>(new FixedVisibility(scope));
            services.AddSingleton<IPermissionResourceHandler, DeniedDefinitionPolicy>();
        });
        using var response = await SendAsync(host, "GET", "/design/workflows/definitions/page?pageSize=1&allowedDefinitionIds=deniedBeta", "library-read");
        response.EnsureSuccessStatusCode();
        Assert.Equal("policy-r2", probe.ReadScope!.Revision);
        Assert.Equal(["deniedBeta"], probe.ReadScope.DeniedDefinitionIds);
        Assert.Equal(0, probe.LegacyQueryCalls);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(1, document.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("nextContinuationToken").ValueKind);
        Assert.Equal("visibleAlpha", Assert.Single(document.RootElement.GetProperty("items").EnumerateArray()).GetProperty("id").GetString());
        Assert.DoesNotContain("deniedBeta", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Tenant_action_only_hosts_can_browse_without_a_query_policy()
    {
        var probe = new LibraryProbe();
        await using var host = await StartAsync(probe, services =>
        {
            services.RemoveAll<IWorkflowLibraryVisibilityPolicy>();
            services.RemoveAll<IPermissionResourceHandler>();
        });
        using var response = await SendAsync(host, "GET", "/design/workflows/definitions/page", "library-read");
        response.EnsureSuccessStatusCode();
        Assert.Equal("tenant-action-v1", probe.ReadScope!.Revision);
        Assert.Null(probe.ReadScope.AllowedDefinitionIds);
        Assert.Null(probe.ReadScope.DeniedDefinitionIds);
    }

    [Fact]
    public async Task A_malformed_host_visibility_scope_fails_before_the_store()
    {
        var probe = new LibraryProbe();
        await using var host = await StartAsync(probe, services =>
        {
            services.RemoveAll<IWorkflowLibraryVisibilityPolicy>();
            services.AddSingleton<IWorkflowLibraryVisibilityPolicy>(new FixedVisibility(new(" ")));
        });
        using var response = await SendAsync(host, "GET", "/design/workflows/definitions/page", "library-read");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(probe.Query);
    }

    [Fact]
    public async Task A_second_visibility_policy_cannot_override_a_restrictive_policy_or_query_the_inventory()
    {
        var probe = new LibraryProbe { CandidateIds = ["visibleAlpha", "deniedBeta"] };
        await using var host = await StartAsync(probe, services =>
        {
            services.RemoveAll<IWorkflowLibraryVisibilityPolicy>();
            services.AddSingleton<IWorkflowLibraryVisibilityPolicy>(new FixedVisibility(new("restrictive", DeniedDefinitionIds: ["deniedBeta"])));
            services.AddSingleton<IWorkflowLibraryVisibilityPolicy>(new FixedVisibility(new("broader")));
        });
        var validator = Assert.Single(host.Services.GetServices<IStartupTask>(), task =>
            task.GetType().Name == "LibraryVisibilityStartupTask");
        var startupException = await Assert.ThrowsAsync<InvalidOperationException>(() => validator.ExecuteAsync(CancellationToken.None));
        Assert.Contains(nameof(IWorkflowLibraryVisibilityPolicy), startupException.Message, StringComparison.Ordinal);
        Assert.Contains("multiple registrations", startupException.Message, StringComparison.Ordinal);
        using var response = await SendAsync(host, "GET", "/design/workflows/definitions/page", "library-read");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("totalCount", body);
        Assert.DoesNotContain("deniedBeta", body);
        Assert.Null(probe.Query);
        Assert.Null(probe.ReadScope);
    }

    [Fact]
    public async Task Definition_details_cannot_bypass_resource_scoped_library_read_permission()
    {
        var probe = new LibraryProbe();
        await using var host = await StartAsync(probe, services => services.AddSingleton<IPermissionResourceHandler, DeniedDefinitionPolicy>());
        using var response = await SendAsync(host, "GET", "/design/workflows/definitions/hidden", "library-read");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(host.Domain.LastReadDefinitionId);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("folderBreadcrumb", body);
        Assert.DoesNotContain("critical", body);
    }

    [Theory]
    [InlineData("GET", "/design/workflows/folders/invoices", null)]
    [InlineData("GET", "/design/workflows/definitions/orders/organization", null)]
    [InlineData("GET", "/design/workflows/definitions/page", null)]
    [InlineData("GET", "/design/workflows/definitions/orders", null)]
    [InlineData("PATCH", "/design/workflows/definitions/orders", "{\"name\":\"Renamed\"}")]
    [InlineData("PUT", "/design/workflows/definitions/orders/organization", "{\"tags\":[],\"revision\":\"r2\"}")]
    public async Task Nested_folder_metadata_requires_read_permission_on_every_returned_ancestor(string method, string path, string? input)
    {
        var probe = new LibraryProbe { IncludeHiddenFolder = true };
        await using var host = await StartAsync(probe, services => services.AddSingleton<IPermissionResourceHandler, DeniedFolderPolicy>());
        using var response = await SendAsync(host, method, path, "library-all", input);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Secret finance root", body);
        Assert.DoesNotContain("folderBreadcrumb", body);
    }

    [Theory]
    [InlineData(true, HttpStatusCode.Forbidden)]
    [InlineData(false, HttpStatusCode.OK)]
    public async Task Creating_in_a_folder_checks_returned_ancestors_before_committing(bool denyAncestor, HttpStatusCode expected)
    {
        var probe = new LibraryProbe { IncludeHiddenFolder = true };
        var add = new LibraryAddCommand();
        await using var host = await StartAsync(probe, services =>
        {
            services.RemoveAll<IAddWorkflowDefinitionCommand>();
            services.AddSingleton<IAddWorkflowDefinitionCommand>(add);
            if (denyAncestor) services.AddSingleton<IPermissionResourceHandler, DeniedFolderPolicy>();
        });
        using var response = await SendAsync(host, "POST", "/design/workflows/definitions", "library-all",
            "{\"name\":\"New invoice\",\"folderId\":\"invoices\"}");
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(!denyAncestor, add.Executed);
        var body = await response.Content.ReadAsStringAsync();
        if (denyAncestor)
        {
            Assert.DoesNotContain("Secret finance root", body);
            Assert.DoesNotContain("folderBreadcrumb", body);
        }
        else Assert.Contains("Secret finance root", body);
    }

    [Theory]
    [InlineData(true, HttpStatusCode.Forbidden)]
    [InlineData(false, HttpStatusCode.OK)]
    public async Task Metadata_updates_authorize_their_response_before_any_write(bool denyAncestor, HttpStatusCode expected)
    {
        var probe = new LibraryProbe { IncludeHiddenFolder = true };
        await using var host = await StartAsync(probe, services =>
        {
            if (denyAncestor) services.AddSingleton<IPermissionResourceHandler, DeniedFolderPolicy>();
        });
        using var response = await SendAsync(host, "PATCH", "/design/workflows/definitions/orders", "library-all",
            "{\"name\":\"Renamed invoice\"}");
        Assert.Equal(expected, response.StatusCode);
        if (denyAncestor) Assert.Null(host.Domain.LastSavedDefinition);
        else Assert.Equal("Renamed invoice", host.Domain.LastSavedDefinition!.Name);
    }

    [Fact]
    public async Task Stale_organization_writes_are_structured_conflicts()
    {
        var probe = new LibraryProbe { ConflictOnReplace = true };
        await using var host = await StartAsync(probe);
        using var response = await SendAsync(host, "PUT", "/design/workflows/definitions/orders/organization", "library-assign",
            "{\"tags\":[],\"revision\":\"stale\"}");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(409, document.RootElement.GetProperty("statusCode").GetInt32());
    }

    [Fact]
    public async Task Catalog_and_view_updates_bind_route_identity_and_expose_authorized_actions()
    {
        var probe = new LibraryProbe();
        await using var host = await StartAsync(probe);
        using var catalog = await SendAsync(host, "PUT", "/design/workflows/tags/critical", "library-catalog",
            "{\"name\":\"Business critical\",\"mode\":\"controlled\",\"cardinality\":\"single\",\"revision\":\"t1\",\"values\":[{\"id\":\"high\",\"name\":\"High\"}]}");
        catalog.EnsureSuccessStatusCode();
        Assert.Equal("critical", probe.TagId);
        Assert.Equal("controlled", probe.TagInput!.Mode);
        Assert.Equal("high", Assert.Single(probe.TagInput.Values!).Id);
        Assert.Equal("user-a", probe.Actor);
        using var update = await SendAsync(host, "PUT", "/design/workflows/views/view-1", "library-share",
            "{\"name\":\"Published finance\",\"query\":{\"folderId\":\"finance\",\"publicationState\":\"published\"},\"shared\":true,\"revision\":\"v1\"}");
        update.EnsureSuccessStatusCode();
        Assert.Equal("view-1", probe.ViewId);
        Assert.True(probe.CanShare);
        using var document = JsonDocument.Parse(await update.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("canEdit").GetBoolean());
        Assert.True(document.RootElement.GetProperty("canDelete").GetBoolean());
        using var delete = await SendAsync(host, "DELETE", "/design/workflows/views/view-1?revision=v2", "library-share");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal("v2", probe.Revision);
    }

    [Theory]
    [InlineData(false, HttpStatusCode.OK)]
    [InlineData(true, HttpStatusCode.NotFound)]
    public async Task Exact_tag_lookup_uses_read_permission_and_stable_identity_without_listing_the_catalog(bool missing, HttpStatusCode expected)
    {
        var probe = new LibraryProbe { MissingTag = missing };
        await using var host = await StartAsync(probe);
        using var response = await SendAsync(host, "GET", "/design/workflows/tags/tag-00000000000000000000000000000001", "library-read");
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("tag-00000000000000000000000000000001", probe.TagId);
        Assert.False(probe.ListedTags);
    }

    [Theory]
    [InlineData("POST", "/design/workflows/folders/invoices/rename", "{\"name\":\"Billing\",\"revision\":\"f1\"}")]
    [InlineData("POST", "/design/workflows/folders/invoices/move", "{\"parentId\":null,\"revision\":\"f1\"}")]
    [InlineData("DELETE", "/design/workflows/folders/invoices?revision=f1", null)]
    public async Task Folder_mutations_bind_the_revision_and_return_no_content(string method, string path, string? body)
    {
        var probe = new LibraryProbe();
        await using var host = await StartAsync(probe);
        using var response = await SendAsync(host, method, path, "library-all", body);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("invoices", probe.FolderId);
        Assert.Equal("f1", probe.Revision);
        Assert.Equal("user-a", probe.Actor);
    }

    private static Task<Host> StartAsync(LibraryProbe store, Action<IServiceCollection>? configure = null,
        bool? organizationEnabled = null) => Host.StartAsync(services =>
    {
        services.AddSingleton<IWorkflowLibraryStore>(store);
        services.AddSingleton<IAuthSessionService, PrincipalSession>();
        services.AddSingleton<IWorkflowLibraryVisibilityPolicy>(new FixedVisibility(new("test-tenant-action-v1")));
        configure?.Invoke(services);
    }, feature =>
    {
        if (organizationEnabled is not { } enabled) return;
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WorkflowOrganizationEnabled"] = enabled ? "true" : "false"
        }).Build().Bind(feature);
    });

    private static async Task<HashSet<string>> RelationsAsync(Host host)
    {
        var relations = new HashSet<string>();
        foreach (var source in host.Services.GetServices<IApiCapabilitySource>())
            foreach (var declaration in await source.GetCapabilitiesAsync())
                foreach (var link in declaration.Links) relations.Add(link.Rel);
        return relations;
    }

    private static Task<HttpResponseMessage> SendAsync(Host host, string method, string path, string identity, string? body = null)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Add(Host.IdentityHeader, identity);
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return host.Client.SendAsync(request);
    }

    private sealed class PrincipalSession : IAuthSessionService
    {
        public ValueTask<AuthSession> GetAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AuthSession("authenticated", principal.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                null, principal.FindFirst(IdentityClaimTypes.TenantId)?.Value, new HashSet<string>(), new HashSet<string>(), "fresh", null));
    }

    private sealed class DeniedDefinitionPolicy : IPermissionResourceHandler
    {
        public ValueTask<PermissionEvaluationResult?> EvaluateAsync(PermissionEvaluationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<PermissionEvaluationResult?>(context.Resource is WorkflowLibraryResource { DefinitionId: "hidden" or "deniedBeta" }
                ? PermissionEvaluationResult.Denied() : null);
    }

    private sealed class FixedVisibility(WorkflowLibraryReadScope scope) : IWorkflowLibraryVisibilityPolicy
    {
        public ValueTask<WorkflowLibraryReadScope> GetScopeAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default) => ValueTask.FromResult(scope);
    }

    private sealed class DeniedFolderPolicy : IPermissionResourceHandler
    {
        public ValueTask<PermissionEvaluationResult?> EvaluateAsync(PermissionEvaluationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<PermissionEvaluationResult?>(context.Resource is WorkflowLibraryResource { FolderId: "finance" }
                ? PermissionEvaluationResult.Denied() : null);
    }

    private sealed class LibraryAddCommand : IAddWorkflowDefinitionCommand
    {
        public bool Executed;
        public Task<WorkflowDefinitionCreated> Execute(DesignOperationKey operationKey, WorkflowDefinition definition,
            WorkflowDefinitionDraft draft, CancellationToken cancellationToken = default)
        {
            Executed = true;
            return Task.FromResult(new WorkflowDefinitionCreated(definition.Id, draft.Id));
        }
        public Task<WorkflowDefinitionCreated> Execute(DesignOperationKey operationKey, WorkflowDefinition definition,
            WorkflowDefinitionDraft draft, IReadOnlyCollection<DesignMetadataRecord> layout,
            IReadOnlyCollection<Elsa.Workflows.Design.Core.Models.ActivityPresentationRecord> activityPresentation,
            string? folderId, string actorId, CancellationToken cancellationToken = default) =>
            Execute(operationKey, definition, draft, cancellationToken);
    }

    private sealed class PublicationLookup(IReadOnlyDictionary<string, WorkflowDefinitionPublicationSummary> summaries) : IWorkflowDefinitionPublicationStateLookup
    {
        public IReadOnlyCollection<string>? RequestedIds;
        public Task<IReadOnlyDictionary<string, WorkflowDefinitionPublicationSummary>> ListByDefinitionIdsAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default)
        { RequestedIds = ids; return Task.FromResult(summaries); }
    }

    private sealed class DeletedDefinitionStore : IWorkflowDefinitionStore
    {
        public Task<WorkflowDefinition> GetAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult(new WorkflowDefinition { Id = id, DeletedAt = DateTimeOffset.UnixEpoch });
        public async Task<WorkflowDefinition?> FindByIdAsync(string id, CancellationToken cancellationToken = default) => await GetAsync(id, cancellationToken);
        public Task<IReadOnlyList<WorkflowDefinition>> ListAsync(WorkflowDefinitionFilter filter, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkflowDefinition>>([]);
    }

    private sealed class LibraryProbe : IWorkflowLibraryStore
    {
        public string? Subject, Actor, DefinitionId, FolderId, Revision, OperationKey, TagId, ViewId;
        public WorkflowTagInput? TagInput;
        public WorkflowLibraryQuery? Query;
        public WorkflowLibraryReadScope? ReadScope;
        public IReadOnlyList<string>? CandidateIds;
        public int LegacyQueryCalls;
        public WorkflowTagBulkEdit? Bulk;
        public IReadOnlyList<string>? MovedIds;
        public IReadOnlyList<string>? RequestedVersionIds;
        public bool CanShare, ConflictOnReplace, MissingTag, ListedTags, IncludeHiddenFolder;
        public string PageDefinitionId = "orders";
        private static WorkflowFolder Folder => new("invoices", "finance", "Invoices", "INVOICES", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "f1");
        private static WorkflowFolder HiddenFolder => new("finance", null, "Secret finance root", "SECRET FINANCE ROOT", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "f0");
        private WorkflowOrganization Organization(string id) => new(id, "invoices", IncludeHiddenFolder ? [HiddenFolder, Folder] : [Folder], [new("critical")], "r2");
        private static WorkflowTag Tag => new("critical", "Critical", null, "marker", "single", false, "t1", []);
        public Task<WorkflowFolderPage> ListFoldersAsync(string? parentId, int pageSize = 50, string? continuationToken = null, CancellationToken cancellationToken = default) => Task.FromResult(new WorkflowFolderPage([Folder], null));
        public Task<WorkflowFolderDetail?> GetFolderAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<WorkflowFolderDetail?>(new(Folder, IncludeHiddenFolder ? [HiddenFolder] : []));
        public Task<WorkflowFolder> CreateFolderAsync(string name, string? parentId, string actorId, CancellationToken cancellationToken = default) { Actor = actorId; FolderId = parentId; return Task.FromResult(Folder); }
        public Task RenameFolderAsync(string id, string name, string? revision, string actorId, CancellationToken cancellationToken = default) { FolderId = id; Revision = revision; Actor = actorId; return Task.CompletedTask; }
        public Task MoveFolderAsync(string id, string? parentId, string? revision, string actorId, CancellationToken cancellationToken = default) { FolderId = id; Revision = revision; Actor = actorId; return Task.CompletedTask; }
        public Task DeleteEmptyFolderAsync(string id, string? revision, string actorId, CancellationToken cancellationToken = default) { FolderId = id; Revision = revision; Actor = actorId; return Task.CompletedTask; }
        public Task MoveDefinitionsAsync(IReadOnlyList<string> definitionIds, string? folderId, string actorId, string operationKey, CancellationToken cancellationToken = default) { MovedIds = definitionIds; FolderId = folderId; Actor = actorId; return Task.CompletedTask; }
        public Task<WorkflowLibraryPage> ListDefinitionsAsync(WorkflowLibraryQuery query, string? subjectId, CancellationToken cancellationToken = default)
        {
            LegacyQueryCalls++;
            Query = query; Subject = subjectId;
            var definition = new WorkflowDefinition { Id = PageDefinitionId, Name = "Collect payment", CreatedAt = DateTimeOffset.UnixEpoch, LastModifiedAt = DateTimeOffset.UnixEpoch };
            return Task.FromResult(new WorkflowLibraryPage([new(definition, Organization(PageDefinitionId), true,
                LatestVersionId: "version-latest", LatestVersion: "2.0.0", PublishedVersionId: "version-published", PublishedVersion: "1.0.0", PublicationState: "published")], "next-page", 123));
        }
        public Task<WorkflowLibraryPage> ListDefinitionsAsync(WorkflowLibraryQuery query, string? subjectId,
            WorkflowLibraryReadScope readScope, CancellationToken cancellationToken = default)
        {
            ReadScope = readScope;
            if (CandidateIds is null) return ListDefinitionsAsync(query, subjectId, cancellationToken);
            Query = query; Subject = subjectId;
            var visible = CandidateIds.Where(id => (readScope.AllowedDefinitionIds is null || readScope.AllowedDefinitionIds.Contains(id, StringComparer.Ordinal)) &&
                !(readScope.DeniedDefinitionIds?.Contains(id, StringComparer.Ordinal) ?? false)).ToArray();
            var rows = visible.Take(query.PageSize).Select(id => new WorkflowLibraryDefinition(
                new WorkflowDefinition { Id = id, Name = id }, Organization(id), false)).ToArray();
            return Task.FromResult(new WorkflowLibraryPage(rows, visible.Length > query.PageSize ? "next-page" : null, visible.Length));
        }
        public Task<WorkflowOrganization?> GetOrganizationAsync(string definitionId, CancellationToken cancellationToken = default) => Task.FromResult<WorkflowOrganization?>(Organization(definitionId));
        public Task<IReadOnlyDictionary<string, string>> GetVersionLabelsAsync(IReadOnlyList<string> versionIds, CancellationToken cancellationToken = default)
        { RequestedVersionIds = versionIds; return Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string> { ["version-published"] = "1.0.0" }); }
        public Task<WorkflowOrganization> ReplaceTagsAsync(string definitionId, string revision, IReadOnlyList<WorkflowTagAssignment> tags, string actorId, string operationKey, CancellationToken cancellationToken = default)
        {
            DefinitionId = definitionId; Revision = revision; Actor = actorId; OperationKey = operationKey;
            return ConflictOnReplace ? Task.FromException<WorkflowOrganization>(new WorkflowLibraryConflictException("Reload organization before editing.")) : Task.FromResult(Organization(definitionId));
        }
        public Task<IReadOnlyList<WorkflowTagEditOutcome>> EditTagsAsync(WorkflowTagBulkEdit edit, string actorId, CancellationToken cancellationToken = default) { Bulk = edit; Actor = actorId; return Task.FromResult<IReadOnlyList<WorkflowTagEditOutcome>>(edit.Targets.Select(target => new WorkflowTagEditOutcome(target.DefinitionId, "success", "r2")).ToArray()); }
        public Task<WorkflowTagPage> ListTagsAsync(string? search = null, int pageSize = 50, string? continuationToken = null, CancellationToken cancellationToken = default) { ListedTags = true; return Task.FromResult(new WorkflowTagPage([Tag], null)); }
        public Task<WorkflowTag?> GetTagAsync(string id, CancellationToken cancellationToken = default) { TagId = id; return Task.FromResult<WorkflowTag?>(MissingTag ? null : Tag); }
        public Task<WorkflowTag> CreateTagAsync(WorkflowTagInput input, string actorId, CancellationToken cancellationToken = default) { Actor = actorId; return Task.FromResult(Tag); }
        public Task<WorkflowTag> UpdateTagAsync(string id, WorkflowTagInput input, string actorId, CancellationToken cancellationToken = default) { Actor = actorId; TagId = id; TagInput = input; return Task.FromResult(Tag); }
        public Task<IReadOnlyList<WorkflowLibraryView>> ListViewsAsync(string subjectId, CancellationToken cancellationToken = default) { Subject = subjectId; return Task.FromResult<IReadOnlyList<WorkflowLibraryView>>([]); }
        public Task<WorkflowLibraryView> SaveViewAsync(string? id, WorkflowLibraryViewInput input, string subjectId, bool canShare, CancellationToken cancellationToken = default) { Subject = subjectId; CanShare = canShare; ViewId = id; return Task.FromResult(new WorkflowLibraryView(id ?? "view-1", input.Name, input.Shared, subjectId, input.Query, input.Columns ?? [], "v1")); }
        public Task DeleteViewAsync(string id, string revision, string subjectId, bool canShare, CancellationToken cancellationToken = default) { Subject = subjectId; CanShare = canShare; Revision = revision; return Task.CompletedTask; }
        public Task SetFavoriteAsync(string definitionId, bool favorite, string subjectId, CancellationToken cancellationToken = default) { Subject = subjectId; DefinitionId = definitionId; return Task.CompletedTask; }
    }
}
