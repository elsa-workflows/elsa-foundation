using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Elsa.Events;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Studio.Dashboard.Api;
using Elsa.Studio.Dashboard.Core.Resources;
using Elsa.Studio.Dashboard.Core.Contracts;
using Elsa.Studio.Dashboard.Core.Models;
using Elsa.Studio.Dashboard.Core.Exceptions;
using Elsa.Studio.Dashboard.Persistence.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Studio.Dashboard.ResourceTests;

public sealed class DashboardResourceApiTests
{
    [Fact]
    public async Task Question_details_and_updates_show_only_current_readable_same_tenant_dashboard_uses()
    {
        await using var host = await DashboardResourceApiHost.CreateAsync();
        var question = await host.CreateQuestionAsync();
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<DashboardQuestionUse>>(question.Uses));
        using (var shared = await host.SendAsync(HttpMethod.Put, $"/_elsa/studio/questions/{question.Id}/audience", "owner",
            new DashboardAudiencePage([new("user", "viewer", "viewer")]), question.Revision))
        {
            shared.EnsureSuccessStatusCode();
            question = (await shared.Content.ReadFromJsonAsync<DashboardResourceView>())!;
        }
        var alpha = await Board("Alpha", 2, share: true);
        var beta = await Board("Beta", 1, share: true);
        var hidden = await Board("Private operations", 1, share: false);
        await host.SeedForeignDashboardUseAsync(question.Id);

        using (var updated = await host.SendAsync(HttpMethod.Put, $"/_elsa/studio/questions/{question.Id}", "owner",
            new DashboardResourceInput { Name = "Revised question", PreserveDefinition = true }, question.Revision))
        {
            updated.EnsureSuccessStatusCode();
            var value = (await updated.Content.ReadFromJsonAsync<DashboardResourceView>())!;
            Assert.Equal(new[] { alpha.Id, beta.Id, hidden.Id }, value.Uses!.Select(use => use.Id));
        }
        using (var detail = await host.SendAsync(HttpMethod.Get, $"/_elsa/studio/questions/{question.Id}", "viewer"))
        {
            detail.EnsureSuccessStatusCode();
            var value = (await detail.Content.ReadFromJsonAsync<DashboardResourceView>())!;
            Assert.Equal(new[] { new DashboardQuestionUse(alpha.Id, "Alpha", 2), new DashboardQuestionUse(beta.Id, "Beta", 1) }, value.Uses);
        }
        using (var coarseDenied = await host.SendAsync(HttpMethod.Get, $"/_elsa/studio/questions/{question.Id}", "owner", fixture: "questions-only"))
        {
            coarseDenied.EnsureSuccessStatusCode();
            Assert.Empty((await coarseDenied.Content.ReadFromJsonAsync<DashboardResourceView>())!.Uses!);
        }
        using (var collection = await host.SendAsync(HttpMethod.Get, "/_elsa/studio/questions", "viewer"))
        {
            collection.EnsureSuccessStatusCode();
            Assert.Null(Assert.Single((await collection.Content.ReadFromJsonAsync<DashboardResourcePage>())!.Items).Uses);
        }
        using (var revoked = await host.SendAsync(HttpMethod.Put, $"/_elsa/studio/dashboards/{alpha.Id}/audience", "owner",
            new DashboardAudiencePage([]), alpha.Revision)) revoked.EnsureSuccessStatusCode();
        using (var detail = await host.SendAsync(HttpMethod.Get, $"/_elsa/studio/questions/{question.Id}", "viewer"))
        {
            detail.EnsureSuccessStatusCode();
            Assert.Equal(new DashboardQuestionUse(beta.Id, "Beta", 1), Assert.Single((await detail.Content.ReadFromJsonAsync<DashboardResourceView>())!.Uses!));
        }

        async Task<DashboardResourceView> Board(string name, int tiles, bool share)
        {
            using var created = await host.SendAsync(HttpMethod.Post, "/_elsa/studio/dashboards", "owner",
                new DashboardResourceInput { Name = name, OperationKey = name,
                    Tiles = Enumerable.Range(0, tiles).Select(index => new DashboardTileInput { Id = $"tile-{index}", QuestionId = question.Id }).ToArray() });
            created.EnsureSuccessStatusCode();
            var board = (await created.Content.ReadFromJsonAsync<DashboardResourceView>())!;
            Assert.Null(board.Uses);
            if (!share) return board;
            using var granted = await host.SendAsync(HttpMethod.Put, $"/_elsa/studio/dashboards/{board.Id}/audience", "owner",
                new DashboardAudiencePage([new("user", "viewer", "viewer")]), board.Revision);
            granted.EnsureSuccessStatusCode();
            return (await granted.Content.ReadFromJsonAsync<DashboardResourceView>())!;
        }
    }

    [Theory]
    [InlineData("DATASET_UNAVAILABLE", 404)]
    [InlineData("UNSUPPORTED_QUERY", 422)]
    [InlineData("QUERY_SOURCE_FAILED", 503)]
    [InlineData("QUERY_TIMEOUT", 504)]
    public async Task Native_preview_distinguishes_current_source_withdrawal_from_invalid_definition_and_outage(string code, int status)
    {
        await using var host = await DashboardResourceApiHost.CreateAsync();
        var definition = DashboardResourceTests.Question("q").Definition;
        using (var initial = await host.SendAsync(HttpMethod.Post, "/_elsa/studio/dashboard-queries/preview", "owner", definition))
            initial.EnsureSuccessStatusCode();
        host.Queries.FailureCode = code;
        using var changed = await host.SendAsync(HttpMethod.Post, "/_elsa/studio/dashboard-queries/preview", "owner", definition);
        Assert.Equal(status, (int)changed.StatusCode);
        using var problem = JsonDocument.Parse(await changed.Content.ReadAsStringAsync());
        Assert.Equal(code, problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(2, host.Queries.Executions);
    }

    [Theory]
    [InlineData("denied", 403)]
    [InlineData("unavailable", 503)]
    public async Task Native_reads_writes_and_queries_honor_the_installed_actor_context(string code, int status)
    {
        await using var host = await DashboardResourceApiHost.CreateAsync(actorRefusal: code);
        using var read = await host.SendAsync(HttpMethod.Get, "/_elsa/studio/questions", "owner");
        using var write = await host.SendAsync(HttpMethod.Post, "/_elsa/studio/questions", "owner", DashboardResourceTests.Question("refused"));
        using var query = await host.SendAsync(HttpMethod.Post, "/_elsa/studio/dashboard-queries/preview", "owner", DashboardResourceTests.Question("refused").Definition);
        Assert.Equal(status, (int)read.StatusCode);
        Assert.Equal(status, (int)write.StatusCode);
        Assert.Equal(status, (int)query.StatusCode);
        Assert.Equal(0, host.Queries.Executions);
        Assert.Equal(0, await host.StoredQuestionCountAsync());
    }

    [Fact]
    public async Task Native_host_refuses_ambiguous_query_replacement_before_serving_requests()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => DashboardResourceApiHost.CreateAsync(duplicateQueries: true));
        Assert.Contains("Exactly one IDashboardQueryService", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, null, 401)]
    [InlineData("owner", "untrusted", 401)]
    [InlineData("owner", "missing-user", 403)]
    [InlineData("owner", "duplicate-user", 403)]
    [InlineData("owner", "resource-denied", 403)]
    [InlineData("owner", "coarse-denied", 403)]
    [InlineData("owner", null, 200)]
    public async Task Native_endpoints_use_normalized_canonical_identity_and_shared_permission_policy(string? actor, string? fixture, int status)
    {
        await using var host = await DashboardResourceApiHost.CreateAsync();
        using var response = await host.SendAsync(HttpMethod.Get, "/_elsa/studio/questions", actor, fixture: fixture);
        Assert.Equal(status, (int)response.StatusCode);
    }

    [Fact]
    public async Task Canonical_owner_is_distinct_from_authentication_subject_and_server_fields_cannot_be_supplied()
    {
        await using var host = await DashboardResourceApiHost.CreateAsync();
        using var saved = await host.SendAsync(HttpMethod.Post, "/_elsa/studio/questions", "owner", DashboardResourceTests.Question("q"));
        saved.EnsureSuccessStatusCode();
        var question = await saved.Content.ReadFromJsonAsync<DashboardResourceView>();
        Assert.Equal("owner", question!.OwnerId);
        Assert.NotEqual("external-owner", question.OwnerId);
        Assert.Equal($"\"{question.Revision}\"", saved.Headers.ETag!.ToString());
        Assert.Contains("no-store", saved.Headers.CacheControl!.ToString());
        using var forged = new HttpRequestMessage(HttpMethod.Post, "/_elsa/studio/dashboards");
        forged.Headers.Add("X-Dashboard-Actor", "owner");
        forged.Content = new StringContent("""{"name":"Forged","operationKey":"forged","ownerId":"viewer","tenantId":"tenant-b"}""", Encoding.UTF8, "application/json");
        using var denied = await host.Client.SendAsync(forged);
        Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
    }

    [Fact]
    public async Task Live_role_removal_and_user_disable_revoke_resource_access_despite_stale_role_claims()
    {
        await using var host = await DashboardResourceApiHost.CreateAsync();
        var question = await host.CreateQuestionAsync();
        using (var shared = await host.SendAsync(HttpMethod.Put, $"/_elsa/studio/questions/{question.Id}/audience", "owner",
            new DashboardAudiencePage([new("role", "operators", "viewer")]), question.Revision))
            shared.EnsureSuccessStatusCode();
        using (var allowed = await host.SendAsync(HttpMethod.Get, $"/_elsa/studio/questions/{question.Id}", "viewer"))
            allowed.EnsureSuccessStatusCode();
        host.Directory.RemoveRole("tenant-a", "operators");
        using (var denied = await host.SendAsync(HttpMethod.Get, $"/_elsa/studio/questions/{question.Id}", "viewer"))
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        await ((IUserStore)host.Directory).SetStatusAsync("tenant-a", "owner", UserStatus.Disabled);
        using var inactive = await host.SendAsync(HttpMethod.Get, $"/_elsa/studio/questions/{question.Id}", "owner");
        Assert.Equal(HttpStatusCode.Forbidden, inactive.StatusCode);
    }

    [Fact]
    public async Task Membership_only_role_audience_uses_current_active_tenant_membership()
    {
        await using var host = await DashboardResourceApiHost.CreateAsync();
        var question = await host.CreateQuestionAsync();
        using (var shared = await host.SendAsync(HttpMethod.Put, $"/_elsa/studio/questions/{question.Id}/audience", "owner",
            new DashboardAudiencePage([new("role", "operators", "viewer")]), question.Revision))
            shared.EnsureSuccessStatusCode();
        var users = (IUserStore)host.Directory;
        var viewer = (await users.FindAsync("tenant-a", "viewer"))!;
        await users.SaveAsync(viewer with { RoleIds = new HashSet<string>() });
        var membership = new TenantMembershipRecord("tenant-a", "viewer", TenantMembershipStatus.Active,
            new HashSet<string>(["operators"], StringComparer.Ordinal), new HashSet<string>());
        await host.Directory.SaveAsync(membership);
        using (var allowed = await host.SendAsync(HttpMethod.Get, $"/_elsa/studio/questions/{question.Id}", "viewer"))
            allowed.EnsureSuccessStatusCode();
        await host.Directory.SaveAsync(membership with { Status = TenantMembershipStatus.Suspended });
        using (var suspended = await host.SendAsync(HttpMethod.Get, $"/_elsa/studio/questions/{question.Id}", "viewer"))
            Assert.Equal(HttpStatusCode.NotFound, suspended.StatusCode);
        await host.Directory.SaveAsync(membership);
        host.Directory.RemoveMembership("tenant-a", "viewer");
        // A role claim in the existing normalized fixture cannot revive a removed membership.
        using (var removed = await host.SendAsync(HttpMethod.Get, $"/_elsa/studio/questions/{question.Id}", "viewer"))
            Assert.Equal(HttpStatusCode.NotFound, removed.StatusCode);
        await host.Directory.SaveAsync(membership);
        host.Directory.RemoveRole("tenant-a", "operators");
        using var missingRole = await host.SendAsync(HttpMethod.Get, $"/_elsa/studio/questions/{question.Id}", "viewer");
        Assert.Equal(HttpStatusCode.NotFound, missingRole.StatusCode);
    }

    [Fact]
    public async Task Read_only_viewer_cannot_edit_share_or_delete_and_copy_requires_create_permission()
    {
        await using var host = await DashboardResourceApiHost.CreateAsync();
        var question = await host.CreateQuestionAsync();
        using var shared = await host.SendAsync(HttpMethod.Put, $"/_elsa/studio/questions/{question.Id}/audience", "owner",
            new DashboardAudiencePage([new("user", "viewer", "viewer")]), question.Revision);
        var current = (await shared.Content.ReadFromJsonAsync<DashboardResourceView>())!;
        using (var edit = await host.SendAsync(HttpMethod.Put, $"/_elsa/studio/questions/{question.Id}", "viewer",
            new DashboardResourceInput { Name = "Attack", PreserveDefinition = true }, current.Revision))
            Assert.Equal(HttpStatusCode.Forbidden, edit.StatusCode);
        using (var share = await host.SendAsync(HttpMethod.Put, $"/_elsa/studio/questions/{question.Id}/audience", "viewer",
            new DashboardAudiencePage([]), current.Revision))
            Assert.Equal(HttpStatusCode.Forbidden, share.StatusCode);
        using (var delete = await host.SendAsync(HttpMethod.Delete, $"/_elsa/studio/questions/{question.Id}", "viewer", revision: current.Revision))
            Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
        using var copy = await host.SendAsync(HttpMethod.Post, $"/_elsa/studio/questions/{question.Id}/copies", "viewer",
            new DashboardCopyInput("copy", "Copy"), current.Revision, fixture: "read-only");
        Assert.Equal(HttpStatusCode.Forbidden, copy.StatusCode);
    }

    [Fact]
    public async Task Concurrent_same_key_creates_converge_and_compare_and_swap_accepts_only_one_writer()
    {
        await using var host = await DashboardResourceApiHost.CreateAsync();
        var requests = await Task.WhenAll(
            host.SendAsync(HttpMethod.Post, "/_elsa/studio/questions", "owner", DashboardResourceTests.Question("same")),
            host.SendAsync(HttpMethod.Post, "/_elsa/studio/questions", "owner", DashboardResourceTests.Question("same")));
        using var first = requests[0]; using var second = requests[1];
        first.EnsureSuccessStatusCode(); second.EnsureSuccessStatusCode();
        var question = (await first.Content.ReadFromJsonAsync<DashboardResourceView>())!;
        Assert.Equal(question.Id, (await second.Content.ReadFromJsonAsync<DashboardResourceView>())!.Id);
        var changes = await Task.WhenAll(
            host.SendAsync(HttpMethod.Put, $"/_elsa/studio/questions/{question.Id}", "owner",
                new DashboardResourceInput { Name = "First", PreserveDefinition = true }, question.Revision),
            host.SendAsync(HttpMethod.Put, $"/_elsa/studio/questions/{question.Id}", "owner",
                new DashboardResourceInput { Name = "Second", PreserveDefinition = true }, question.Revision));
        using var changeOne = changes[0]; using var changeTwo = changes[1];
        Assert.Single(changes, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(changes, response => response.StatusCode == HttpStatusCode.PreconditionFailed);
    }

    [Fact]
    public async Task Revoke_during_editor_validation_cannot_be_overwritten_by_the_older_authorized_update()
    {
        await using var host = await DashboardResourceApiHost.CreateAsync();
        var question = await host.CreateQuestionAsync();
        using var grant = await host.SendAsync(HttpMethod.Put, $"/_elsa/studio/questions/{question.Id}/audience", "owner",
            new DashboardAudiencePage([new("user", "editor", "editor")]), question.Revision);
        question = (await grant.Content.ReadFromJsonAsync<DashboardResourceView>())!;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Validator.BeforeValidation = async actor => { if (actor.UserId == "editor") { entered.TrySetResult(); await release.Task; } };
        var editing = host.SendAsync(HttpMethod.Put, $"/_elsa/studio/questions/{question.Id}", "editor",
            DashboardResourceTests.Question("unused") with { OperationKey = null, Name = "Concurrent editor" }, question.Revision);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        try
        {
            using var revoke = await host.SendAsync(HttpMethod.Put, $"/_elsa/studio/questions/{question.Id}/audience", "owner",
                new DashboardAudiencePage([]), question.Revision);
            revoke.EnsureSuccessStatusCode();
        }
        finally { release.TrySetResult(); }
        using var staleSave = await editing;
        Assert.Equal(HttpStatusCode.PreconditionFailed, staleSave.StatusCode);
        using var history = await host.SendAsync(HttpMethod.Get, $"/_elsa/studio/questions/{question.Id}/revisions", "editor");
        Assert.Equal(HttpStatusCode.NotFound, history.StatusCode);
    }

    [Fact]
    public async Task Audience_pages_are_bounded_and_empty_pages_keep_continuation_without_cross_scope_reuse()
    {
        await using var host = await DashboardResourceApiHost.CreateAsync();
        host.Directory.SeedCandidates();
        using var first = await host.SendAsync(HttpMethod.Get, "/_elsa/studio/dashboard-audience-candidates?kind=user", "owner");
        var initial = (await first.Content.ReadFromJsonAsync<DashboardAudienceCandidatePage>())!;
        Assert.Empty(initial.Items); Assert.NotNull(initial.ContinuationToken);
        using var next = await host.SendAsync(HttpMethod.Get, "/_elsa/studio/dashboard-audience-candidates?kind=user&continuationToken=" +
            Uri.EscapeDataString(initial.ContinuationToken), "owner");
        var continued = (await next.Content.ReadFromJsonAsync<DashboardAudienceCandidatePage>())!;
        Assert.InRange(continued.Items.Count, 1, 100);
        Assert.Contains(continued.Items, item => item.Id == "candidate-100");
        using var otherTenant = await host.SendAsync(HttpMethod.Get, "/_elsa/studio/dashboard-audience-candidates?kind=user&continuationToken=" +
            Uri.EscapeDataString(initial.ContinuationToken), "owner", tenant: "tenant-b");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, otherTenant.StatusCode);
        using var otherKind = await host.SendAsync(HttpMethod.Get, "/_elsa/studio/dashboard-audience-candidates?kind=role&continuationToken=" +
            Uri.EscapeDataString(initial.ContinuationToken), "owner");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, otherKind.StatusCode);
        using var changedSearch = await host.SendAsync(HttpMethod.Get, "/_elsa/studio/dashboard-audience-candidates?kind=user&search=changed&continuationToken=" +
            Uri.EscapeDataString(initial.ContinuationToken), "owner");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, changedSearch.StatusCode);
        using var denied = await host.SendAsync(HttpMethod.Get, "/_elsa/studio/dashboard-audience-candidates?kind=user", "owner", fixture: "read-only");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Theory]
    [InlineData("/_elsa/studio/dashboards", """{"name":"Invalid","operationKey":"null-tile","tiles":[null]}""")]
    [InlineData("/_elsa/studio/questions", """{"name":"Invalid","operationKey":"null-query","definition":{"query":null,"visualization":null}}""")]
    public async Task Null_nested_resource_inputs_are_validation_errors_not_server_failures(string route, string payload)
    {
        await using var host = await DashboardResourceApiHost.CreateAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, route);
        request.Headers.Add("X-Dashboard-Actor", "owner");
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Role_candidate_search_can_continue_past_a_page_without_matches_and_rejects_modified_tokens()
    {
        await using var host = await DashboardResourceApiHost.CreateAsync();
        host.Directory.SeedRoleCandidates();
        using var first = await host.SendAsync(HttpMethod.Get, "/_elsa/studio/dashboard-audience-candidates?kind=role&search=target", "owner");
        var page = (await first.Content.ReadFromJsonAsync<DashboardAudienceCandidatePage>())!;
        Assert.Empty(page.Items); Assert.NotNull(page.ContinuationToken);
        using var next = await host.SendAsync(HttpMethod.Get, "/_elsa/studio/dashboard-audience-candidates?kind=role&search=target&continuationToken=" +
            Uri.EscapeDataString(page.ContinuationToken), "owner");
        Assert.Contains((await next.Content.ReadFromJsonAsync<DashboardAudienceCandidatePage>())!.Items, item => item.Id == "zz-target");
        var tampered = page.ContinuationToken[0] == 'A' ? "B" + page.ContinuationToken[1..] : "A" + page.ContinuationToken[1..];
        using var invalid = await host.SendAsync(HttpMethod.Get, "/_elsa/studio/dashboard-audience-candidates?kind=role&search=target&continuationToken=" +
            Uri.EscapeDataString(tampered), "owner");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
    }

    [Theory]
    [InlineData("owner", null, true)]
    [InlineData("editor", null, true)]
    [InlineData("viewer", null, false)]
    [InlineData("editor", "read-only", false)]
    [InlineData("owner", "read-only", false)]
    public async Task Bound_replacement_preview_requires_current_edit_rights_and_rechecks_the_new_source(string actor, string? fixture, bool allowed)
    {
        await using var host = await DashboardResourceApiHost.CreateAsync();
        var question = await host.CreateQuestionAsync();
        using var grant = await host.SendAsync(HttpMethod.Put, $"/_elsa/studio/questions/{question.Id}/audience", "owner",
            new DashboardAudiencePage([new("user", "editor", "editor"), new("user", "viewer", "viewer")]), question.Revision);
        grant.EnsureSuccessStatusCode(); question = (await grant.Content.ReadFromJsonAsync<DashboardResourceView>())!;
        // Original source support disappeared. Resource ACLs and edit permissions remain independent.
        host.Validator.Disclosure = (_, _) => false;
        var replacement = DashboardResourceTests.Question("replacement").Definition!;
        replacement = replacement with { Query = replacement.Query with { Dataset = new("replacement.records", "1") } };
        var route = $"/_elsa/studio/questions/{question.Id}/preview";
        using var preview = await host.SendAsync(HttpMethod.Post, route, actor, replacement, question.Revision, fixture);
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, preview.StatusCode);
        Assert.Equal(allowed ? 1 : 0, host.Queries.Executions);
        if (!allowed) return;
        Assert.Equal("replacement.records", host.Queries.LastDatasetId);
        Assert.Equal(actor, host.Queries.LastScope!.SubjectId);
        using (var saved = await host.SendAsync(HttpMethod.Get, $"/_elsa/studio/questions/{question.Id}", "owner"))
        {
            var current = (await saved.Content.ReadFromJsonAsync<DashboardResourceView>())!;
            Assert.Equal(question.Revision, current.Revision); Assert.Equal("withheld", current.DefinitionState); Assert.Null(current.Definition);
        }
        host.Queries.FailureCode = "QUERY_SCOPE_REQUIRED";
        using var deniedSource = await host.SendAsync(HttpMethod.Post, route, actor, replacement, question.Revision);
        Assert.Equal(HttpStatusCode.Forbidden, deniedSource.StatusCode);
        Assert.Equal(2, host.Queries.Executions);
    }

    [Fact]
    public async Task Bound_saved_draft_preview_rechecks_current_acl_and_revision_before_query_execution()
    {
        await using var host = await DashboardResourceApiHost.CreateAsync();
        var question = await host.CreateQuestionAsync();
        var draft = DashboardResourceTests.Question("draft").Definition!;
        var route = $"/_elsa/studio/questions/{question.Id}/preview";
        using (var denied = await host.SendAsync(HttpMethod.Post, route, "viewer", draft, question.Revision))
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.Equal(0, host.Queries.Executions);
        using (var missing = await host.SendAsync(HttpMethod.Post, route, "owner", draft))
            Assert.Equal((HttpStatusCode)428, missing.StatusCode);
        using (var stale = await host.SendAsync(HttpMethod.Post, route, "owner", draft, "old-revision"))
            Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        Assert.Equal(0, host.Queries.Executions);
        using var grant = await host.SendAsync(HttpMethod.Put, $"/_elsa/studio/questions/{question.Id}/audience", "owner",
            new DashboardAudiencePage([new("user", "viewer", "viewer")]), question.Revision);
        question = (await grant.Content.ReadFromJsonAsync<DashboardResourceView>())!;
        using (var preview = await host.SendAsync(HttpMethod.Post, route, "viewer", draft, question.Revision))
        {
            preview.EnsureSuccessStatusCode();
            Assert.Equal($"\"{question.Revision}\"", preview.Headers.ETag!.ToString());
        }
        Assert.Equal(1, host.Queries.Executions);
        Assert.Equal("viewer", host.Queries.LastScope!.SubjectId);
        using var revoked = await host.SendAsync(HttpMethod.Put, $"/_elsa/studio/questions/{question.Id}/audience", "owner",
            new DashboardAudiencePage([]), question.Revision);
        question = (await revoked.Content.ReadFromJsonAsync<DashboardResourceView>())!;
        using (var denied = await host.SendAsync(HttpMethod.Post, route, "viewer", draft, question.Revision))
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.Equal(1, host.Queries.Executions);
    }
}

internal sealed class DashboardResourceApiHost : IAsyncDisposable
{
    private readonly IHost host;
    private readonly string databasePath;
    public HttpClient Client { get; }
    public DashboardIdentityFixture Directory { get; }
    public ResourceValidator Validator { get; }
    public DashboardQueryExecutionFixture Queries { get; }
    private DashboardResourceApiHost(IHost host, string path, DashboardIdentityFixture directory, ResourceValidator validator,
        DashboardQueryExecutionFixture queries)
    { this.host = host; databasePath = path; Directory = directory; Validator = validator; Queries = queries; Client = host.GetTestClient(); }

    public static async Task<DashboardResourceApiHost> CreateAsync(bool duplicateQueries = false, string? actorRefusal = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"elsa-dashboard-api-{Guid.NewGuid():N}.db");
        var directory = new DashboardIdentityFixture(); var validator = new ResourceValidator(); var queries = new DashboardQueryExecutionFixture();
        var host = new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.ConfigureServices(services =>
            {
                services.AddLogging(logging => logging.ClearProviders()); services.AddRouting(); services.AddAuthorization(); services.AddOpenApi();
                services.AddAuthentication(DashboardAuthenticationFixture.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, DashboardAuthenticationFixture>(DashboardAuthenticationFixture.SchemeName, _ => { });
                services.AddFoundationIdentityAbstractions(options =>
                    options.NormalizedAuthenticationTypes = new HashSet<string>([DashboardAuthenticationFixture.SchemeName], StringComparer.Ordinal));
                services.AddSingleton<IUserStore>(directory); services.AddSingleton<IRoleStore>(directory);
                services.AddSingleton<ITenantMembershipStore>(directory);
                services.AddSingleton<IPagedUserStore>(directory); services.AddSingleton<IPagedRoleStore>(directory);
                services.AddScoped<IPermissionResourceHandler, DashboardResourceDenyFixture>();
                services.AddSingleton<IDashboardDefinitionValidator>(validator);
                services.AddSingleton<IDashboardQueryService>(queries);
                new EventsFeature().ConfigureServices(services);
                new DashboardResourcesApiFeature().ConfigureServices(services);
                if (actorRefusal is not null) services.Replace(ServiceDescriptor.Singleton<IDashboardActorContext>(new RefusingDashboardActorContext(actorRefusal)));
                if (duplicateQueries) services.AddSingleton<IDashboardQueryService>(queries);
                services.AddDbContext<DashboardResourcesSqliteDbContext>(options => options.UseSqlite($"Data Source={path};Pooling=False"));
                services.AddDashboardResourcesEntityFrameworkCore<DashboardResourcesSqliteDbContext>();
            });
            web.Configure(app =>
            {
                app.UseRouting(); app.UseAuthentication(); app.UseAuthorization();
                app.UseEndpoints(endpoints => new DashboardResourcesApiFeature().MapEndpoints(endpoints, null));
            });
        }).Build();
        try
        {
            await using (var scope = host.Services.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<DashboardResourcesSqliteDbContext>().Database.MigrateAsync();
            await host.StartAsync();
            return new(host, path, directory, validator, queries);
        }
        catch
        {
            host.Dispose(); File.Delete(path);
            throw;
        }
    }

    public async Task<DashboardResourceView> CreateQuestionAsync()
    {
        using var response = await SendAsync(HttpMethod.Post, "/_elsa/studio/questions", "owner", DashboardResourceTests.Question("question"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DashboardResourceView>())!;
    }

    public async Task<int> StoredQuestionCountAsync()
    {
        await using var scope = host.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IDashboardResourceStore>();
        var count = 0; string? after = null;
        while (true)
        {
            var page = await store.ReadPageAsync("tenant-a", "question", after);
            count += page.Count;
            if (page.Count < 16) return count;
            after = page[^1].Id;
        }
    }

    public async Task SeedForeignDashboardUseAsync(string questionId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var now = DateTimeOffset.UtcNow;
        var document = new DashboardResourceDocument("foreign-board", "dashboard", "tenant-b", "owner", "foreign-revision",
            new("Foreign dashboard", null, null, [new("foreign-tile", questionId, "medium")]), [], [], now, now);
        Assert.Equal("saved", (await scope.ServiceProvider.GetRequiredService<IDashboardResourceStore>().WriteAsync(new(document, null))).Status);
    }

    public Task<HttpResponseMessage> SendAsync(HttpMethod method, string uri, string? actor, object? body = null,
        string? revision = null, string? fixture = null, string tenant = "tenant-a")
    {
        var request = new HttpRequestMessage(method, uri);
        if (actor is not null) request.Headers.Add("X-Dashboard-Actor", actor);
        request.Headers.Add("X-Dashboard-Tenant", tenant);
        if (fixture is not null) request.Headers.Add("X-Dashboard-Fixture", fixture);
        if (revision is not null) request.Headers.TryAddWithoutValidation("If-Match", $"\"{revision}\"");
        if (body is not null) request.Content = JsonContent.Create(body, options: new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return Client.SendAsync(request);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose(); await host.StopAsync(); host.Dispose();
        File.Delete(databasePath);
    }
}

internal sealed class RefusingDashboardActorContext(string code) : IDashboardActorContext
{
    public ValueTask<DashboardActor> GetAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromException<DashboardActor>(new DashboardResourceException(code, "The installed current identity source refused this request."));
}

internal sealed class DashboardQueryExecutionFixture : IDashboardQueryService
{
    private int executions;
    public string? FailureCode { get; set; }
    public int Executions => Volatile.Read(ref executions);
    public DashboardRequestScope? LastScope { get; private set; }
    public string? LastDatasetId { get; private set; }
    public ValueTask<IReadOnlyList<DashboardDatasetDeclaration>> DiscoverAsync(DashboardRequestScope scope, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<DashboardDatasetDeclaration>>([]);
    public ValueTask<DashboardQueryValidation> ValidateAsync(DashboardRequestScope scope, DashboardQueryRequest request, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new DashboardQueryValidation(true, []));
    public ValueTask<DashboardQueryResult> ExecuteAsync(DashboardRequestScope scope, DashboardQueryRequest request, CancellationToken cancellationToken = default)
    {
        LastScope = scope; LastDatasetId = request.Query.Dataset.Id; Interlocked.Increment(ref executions);
        if (FailureCode is { } code) throw new DashboardQueryException(code, "The current source operation could not produce this preview.");
        return ValueTask.FromResult(new DashboardQueryResult { Interpretation = "Deterministic API authorization fixture; no provider or source query qualification." });
    }
    public ValueTask<DashboardQueryResult> DrillAsync(DashboardRequestScope scope, DashboardDrillRequest request, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new DashboardQueryResult());
}

internal sealed class DashboardAuthenticationFixture(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "dashboard-resource-fixture";
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var actor = Request.Headers["X-Dashboard-Actor"].FirstOrDefault();
        if (actor is null) return Task.FromResult(AuthenticateResult.NoResult());
        var fixture = Request.Headers["X-Dashboard-Fixture"].FirstOrDefault();
        var claims = new List<Claim> {
            new(ClaimTypes.NameIdentifier, $"external-{actor}"),
            new(IdentityClaimTypes.Normalized, fixture == "untrusted" ? "invalid" : "v1"),
            new(IdentityClaimTypes.TenantId, Request.Headers["X-Dashboard-Tenant"].FirstOrDefault() ?? "tenant-a"),
            new(IdentityClaimTypes.Provider, "fixture"), new(IdentityClaimTypes.Role, "operators")
        };
        if (fixture != "missing-user") claims.Add(new(IdentityClaimTypes.UserId, actor));
        if (fixture == "duplicate-user") claims.Add(new(IdentityClaimTypes.UserId, "another"));
        foreach (var permission in fixture switch {
            "coarse-denied" => Array.Empty<string>(),
            "questions-only" => ["studio/questions:read"],
            "read-only" => new[] { "studio/questions:read", "studio/dashboards:read" },
            _ => ["*"] })
            claims.Add(new(IdentityClaimTypes.Permission, permission));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName)));
    }
}

internal sealed class DashboardResourceDenyFixture(IHttpContextAccessor accessor) : IPermissionResourceHandler
{
    public ValueTask<PermissionEvaluationResult?> EvaluateAsync(PermissionEvaluationContext context, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<PermissionEvaluationResult?>(accessor.HttpContext?.Request.Headers["X-Dashboard-Fixture"].FirstOrDefault() == "resource-denied"
            ? PermissionEvaluationResult.Denied("Fixture denied current resource access.") : null);
}

internal sealed class DashboardIdentityFixture : IUserStore, IRoleStore, ITenantMembershipStore, IPagedUserStore, IPagedRoleStore
{
    private readonly ConcurrentDictionary<(string Tenant, string Id), UserRecord> users = new();
    private readonly ConcurrentDictionary<(string Tenant, string Id), RoleRecord> roles = new();
    private readonly ConcurrentDictionary<(string Tenant, string Id), TenantMembershipRecord> memberships = new();
    public DashboardIdentityFixture()
    {
        foreach (var tenant in new[] { "tenant-a", "tenant-b" })
        {
            foreach (var id in new[] { "owner", "editor", "viewer" })
                users[(tenant, id)] = User(tenant, id, UserStatus.Active, id == "viewer" ? ["operators"] : []);
            roles[(tenant, "operators")] = new("operators", tenant, "Operators", null, new HashSet<string>(), false);
        }
    }
    public void RemoveRole(string tenant, string id) => roles.TryRemove((tenant, id), out _);
    public void RemoveMembership(string tenant, string id) => memberships.TryRemove((tenant, id), out _);
    ValueTask<TenantMembershipRecord?> ITenantMembershipStore.FindAsync(string tenantId, string userId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(memberships.GetValueOrDefault((tenantId, userId)));
    public ValueTask SaveAsync(TenantMembershipRecord membership, CancellationToken cancellationToken = default)
    { memberships[(membership.TenantId, membership.UserId)] = membership; return ValueTask.CompletedTask; }
    public void SeedCandidates()
    {
        for (var index = 0; index < 205; index++)
            users[("tenant-a", $"candidate-{index:D3}")] = User("tenant-a", $"candidate-{index:D3}", index < 100 ? UserStatus.Disabled : UserStatus.Active, []);
    }
    public void SeedRoleCandidates()
    {
        for (var index = 0; index < 110; index++)
            roles[("tenant-a", $"role-{index:D3}")] = new($"role-{index:D3}", "tenant-a", $"Ordinary {index:D3}", null, new HashSet<string>(), false);
        roles[("tenant-a", "zz-target")] = new("zz-target", "tenant-a", "Target role", null, new HashSet<string>(), false);
    }
    private static UserRecord User(string tenant, string id, UserStatus status, string[] memberships) =>
        new(id, tenant, id, $"{id}@example.invalid", id, status, ResourceOwnership.Foundation, memberships.ToHashSet(StringComparer.Ordinal), new HashSet<string>());
    public ValueTask<UserRecord?> FindAsync(string tenantId, string userId, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(users.GetValueOrDefault((tenantId, userId)));
    public ValueTask<UserRecord?> FindByEmailAsync(string tenantId, string email, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(users.Values.FirstOrDefault(user => user.TenantId == tenantId && user.Email == email));
    public ValueTask SaveAsync(UserRecord user, CancellationToken cancellationToken = default)
    { users[(user.TenantId, user.Id)] = user; return ValueTask.CompletedTask; }
    public ValueTask<bool> SetStatusAsync(string tenantId, string userId, UserStatus status, CancellationToken cancellationToken = default)
    {
        if (!users.TryGetValue((tenantId, userId), out var user)) return ValueTask.FromResult(false);
        users[(tenantId, userId)] = user with { Status = status }; return ValueTask.FromResult(true);
    }
    public ValueTask<bool> DeleteAsync(string tenantId, string userId, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(users.TryRemove((tenantId, userId), out _));
    ValueTask<RoleRecord?> IRoleStore.FindAsync(string tenantId, string roleId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(roles.GetValueOrDefault((tenantId, roleId)));
    public ValueTask<IReadOnlyList<RoleRecord>> ListAsync(string tenantId, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<RoleRecord>>(roles.Values.Where(role => role.TenantId == tenantId).OrderBy(role => role.Id, StringComparer.Ordinal).ToArray());
    public ValueTask SaveAsync(RoleRecord role, CancellationToken cancellationToken = default)
    { roles[(role.TenantId, role.Id)] = role; return ValueTask.CompletedTask; }
    ValueTask<IamPage<UserRecord>> IPagedUserStore.ListPageAsync(string tenantId, IamPageRequest request, string? search, CancellationToken cancellationToken)
    {
        var found = users.Values.Where(user => user.TenantId == tenantId && (search is null || user.UserName == search || user.Email == search))
            .OrderBy(user => user.Id, StringComparer.Ordinal).ToArray();
        return ValueTask.FromResult(new IamPage<UserRecord>(found.Skip(request.Skip).Take(request.Take).ToArray(), found.LongLength));
    }
    ValueTask<IamPage<RoleRecord>> IPagedRoleStore.ListPageAsync(string tenantId, IamPageRequest request, CancellationToken cancellationToken)
    {
        var found = roles.Values.Where(role => role.TenantId == tenantId).OrderBy(role => role.Id, StringComparer.Ordinal).ToArray();
        return ValueTask.FromResult(new IamPage<RoleRecord>(found.Skip(request.Skip).Take(request.Take).ToArray(), found.LongLength));
    }
}
