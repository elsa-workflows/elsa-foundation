using System.Net;
using System.Text.Json;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Persistence.EntityFrameworkCore.Stores;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Agent.Tests.Authoring;

public sealed partial class AgentApiHttpTests
{
    private readonly SqliteConnection sourceConnection = new("Data Source=:memory:");
    private sealed class AgentSourceTestContext(SqliteConnection connection) : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(connection);
        protected override void OnModelCreating(ModelBuilder model)
        {
            model.AddSourceRevisions("agent_source", Database.ProviderName!);
            model.Entity<Elsa.Persistence.Core.SourceWorkingCopy>().HasIndex(x => x.SourceIdentity).IsUnique();
        }
    }
    private sealed class SourceAccess : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"));
    }
    private void AddSourceStorage(IServiceCollection services)
    {
        services.AddScoped(_ => new AgentSourceTestContext(sourceConnection));
        services.AddSingleton<IPersistenceAccessContextAccessor, SourceAccess>();
        services.AddScoped<EfAgentSources<AgentSourceTestContext>>();
        services.AddScoped<IAgentSourceReader>(provider => provider.GetRequiredService<EfAgentSources<AgentSourceTestContext>>());
        services.AddScoped<IAgentSourceWriter>(provider => provider.GetRequiredService<EfAgentSources<AgentSourceTestContext>>());
    }
    private static string SourceBody(string instructions = "Write a headline") => JsonSerializer.Serialize(new
    {
        agentId = "copywriter", draftId = "draft", name = "Copywriter",
        content = new { instructions, model = new { providerKey = "fixture", connectionName = "writer", modelId = "model-a" },
            inputs = new[] { new { key = "brief", name = "ProductBrief", type = new { alias = "String", collectionKind = 0 }, required = true } },
            results = new[] { new { key = "headline", name = "Headline", type = new { alias = "String", collectionKind = 0 } } },
            settings = new { }, limits = new { maximumModelCalls = 1, timeoutMilliseconds = 10000 } }
    });

    [Theory]
    [InlineData(".", "..")]
    [InlineData("..", ".")]
    [InlineData("writer/one", "draft/two")]
    [InlineData("writer%2Fone", "%25 + 名 ")]
    public async Task Opaque_source_and_copy_identities_round_trip_without_path_normalization(string agentId, string draftId)
    {
        const string root = "/agent/definitions";
        using var source = JsonDocument.Parse(SourceBody("Exact opaque source"));
        var content = source.RootElement.GetProperty("content").Clone();
        var create = JsonSerializer.Serialize(new { agentId, draftId, name = "Opaque copy", content });
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Post, root, create, "agent/definitions:manage")).StatusCode);
        var identity = "agentId=" + Uri.EscapeDataString(agentId);
        var copy = identity + "&draftId=" + Uri.EscapeDataString(draftId);
        var draft = await SendAsync(HttpMethod.Get, root + "/draft?" + copy, null, "agent/definitions:read");
        Assert.Equal(HttpStatusCode.OK, draft.StatusCode);
        using var read = JsonDocument.Parse(await draft.Content.ReadAsStringAsync());
        Assert.Equal(agentId, read.RootElement.GetProperty("agentId").GetString());
        Assert.Equal(draftId, read.RootElement.GetProperty("draftId").GetString());
        var save = JsonSerializer.Serialize(new { agentId, draftId, expectedRevision = 0, name = "Saved opaque copy", content });
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Put, root + "/draft", save, "agent/definitions:manage")).StatusCode);
        var captureBody = JsonSerializer.Serialize(new { agentId, draftId, expectedRevision = 1, operationId = "opaque review" });
        var capture = await SendAsync(HttpMethod.Post, root + "/captures", captureBody, "agent/definitions:manage");
        Assert.Equal(HttpStatusCode.OK, capture.StatusCode);
        using var captured = JsonDocument.Parse(await capture.Content.ReadAsStringAsync());
        var snapshotId = captured.RootElement.GetProperty("id").GetString()!;
        var discard = JsonSerializer.Serialize(new { agentId, draftId, expectedRevision = 1 });
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Post, root + "/discard", discard, "agent/definitions:manage")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, root + "/draft?" + copy, null, "agent/definitions:read")).StatusCode);
        var exact = await SendAsync(HttpMethod.Get, root + "/revision?" + identity + "&snapshotId=" + Uri.EscapeDataString(snapshotId), null, "agent/definitions:read");
        Assert.Equal(HttpStatusCode.OK, exact.StatusCode);
        using var retained = JsonDocument.Parse(await exact.Content.ReadAsStringAsync());
        Assert.Equal(draftId, retained.RootElement.GetProperty("sourceCopyId").GetString());
        Assert.Equal("Exact opaque source", retained.RootElement.GetProperty("content").GetProperty("instructions").GetString());
        var history = await SendAsync(HttpMethod.Get, root + "/revisions?" + identity, null, "agent/definitions:read");
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        using var page = JsonDocument.Parse(await history.Content.ReadAsStringAsync());
        Assert.Equal(1, page.RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Agent_source_wide_history_inspects_exact_content_after_draft_replacement()
    {
        const string root = "/agent/definitions";
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Post, root, SourceBody("Original instructions"), "agent/definitions:manage")).StatusCode);
        var capture = await SendAsync(HttpMethod.Post, root + "/captures", """{"agentId":"copywriter","draftId":"draft","expectedRevision":0,"operationId":"original"}""", "agent/definitions:manage");
        using var captured = JsonDocument.Parse(await capture.Content.ReadAsStringAsync());
        var snapshotId = captured.RootElement.GetProperty("id").GetString()!;
        using var editedContent = JsonDocument.Parse(SourceBody("Edited original copy"));
        var edit = JsonSerializer.Serialize(new { agentId = "copywriter", draftId = "draft", expectedRevision = 0, name = "Copywriter", content = editedContent.RootElement.GetProperty("content") });
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Put, root + "/draft", edit, "agent/definitions:manage")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Post, root + "/discard", """{"agentId":"copywriter","draftId":"draft","expectedRevision":1}""", "agent/definitions:manage")).StatusCode);
        var replacement = SourceBody("Replacement instructions").Replace("\"draftId\":\"draft\"", "\"draftId\":\"replacement\"", StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Post, root, replacement, "agent/definitions:manage")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Post, root + "/captures", """{"agentId":"copywriter","draftId":"replacement","expectedRevision":0,"operationId":"replacement"}""", "agent/definitions:manage")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Get, root + "/revisions?agentId=copywriter", null, "activities/design:read")).StatusCode);
        var exact = await SendAsync(HttpMethod.Get, root + "/revision?agentId=copywriter&snapshotId=" + snapshotId, null, "agent/definitions:read");
        Assert.Equal(HttpStatusCode.OK, exact.StatusCode);
        using var original = JsonDocument.Parse(await exact.Content.ReadAsStringAsync());
        Assert.Equal("draft", original.RootElement.GetProperty("sourceCopyId").GetString());
        Assert.Equal("Original instructions", original.RootElement.GetProperty("content").GetProperty("instructions").GetString());
        var history = await SendAsync(HttpMethod.Get, root + "/revisions?agentId=copywriter", null, "agent/definitions:read");
        using var page = JsonDocument.Parse(await history.Content.ReadAsStringAsync());
        Assert.Equal(2, page.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(2, page.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, root + "/revision?agentId=copywriter&snapshotId=missing", null, "agent/definitions:read")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, root + "/revision?agentId=other&snapshotId=" + snapshotId, null, "agent/definitions:read")).StatusCode);
    }

    [Fact]
    public async Task Agent_directory_keeps_history_identity_without_exposing_discarded_content_as_an_editable_draft()
    {
        const string root = "/agent/definitions";
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Post, root, SourceBody(), "agent/definitions:manage")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Post, root + "/discard", """{"agentId":"copywriter","draftId":"draft","expectedRevision":0}""", "agent/definitions:manage")).StatusCode);
        var listed = await SendAsync(HttpMethod.Get, root, null, "agent/definitions:read");
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        using var body = JsonDocument.Parse(await listed.Content.ReadAsStringAsync());
        Assert.Equal(1, body.RootElement.GetProperty("total").GetInt32());
        var item = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("copywriter", item.GetProperty("agentId").GetString());
        Assert.Equal("Copywriter", item.GetProperty("name").GetString());
        Assert.Equal("draft", item.GetProperty("sourceCopyId").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("draftId").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("revision").ValueKind);
        Assert.False(item.TryGetProperty("content", out _));
    }

    [Fact]
    public async Task Agent_source_uses_own_permissions_and_rejects_forged_authority_fields()
    {
        const string route = "/agent/definitions";
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Post, route, SourceBody(), "activities/design:manage")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Get, route, null, "activities/design:read")).StatusCode);
        var forged = SourceBody().Replace("{\"agentId\"", "{\"tenantId\":\"other\",\"agentId\"", StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Post, route, forged, "agent/definitions:manage")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Post, route, SourceBody(), "agent/definitions:manage")).StatusCode);
        var listed = await SendAsync(HttpMethod.Get, route, null, "agent/definitions:read");
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        using var page = JsonDocument.Parse(await listed.Content.ReadAsStringAsync());
        Assert.Equal(1, page.RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Agent_source_saved_revision_capture_history_and_discard_use_shared_storage()
    {
        const string root = "/agent/definitions";
        const string draft = root + "/draft";
        const string draftRead = root + "/draft?agentId=copywriter&draftId=draft";
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Post, root, SourceBody(), "agent/definitions:manage")).StatusCode);
        using var saved = JsonDocument.Parse(SourceBody("Edited"));
        var body = JsonSerializer.Serialize(new { agentId = "copywriter", draftId = "draft", expectedRevision = 0, name = "Copywriter", content = saved.RootElement.GetProperty("content") });
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Put, draft, body, "agent/definitions:manage")).StatusCode);
        var stale = await SendAsync(HttpMethod.Put, draft, body, "agent/definitions:manage");
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var capture = await SendAsync(HttpMethod.Post, root + "/captures", """{"agentId":"copywriter","draftId":"draft","expectedRevision":1,"operationId":"review-1"}""", "agent/definitions:manage");
        Assert.Equal(HttpStatusCode.OK, capture.StatusCode);
        var repeated = await SendAsync(HttpMethod.Post, root + "/captures", """{"agentId":"copywriter","draftId":"draft","expectedRevision":1,"operationId":"review-1"}""", "agent/definitions:manage");
        Assert.Equal(await capture.Content.ReadAsStringAsync(), await repeated.Content.ReadAsStringAsync());
        var history = await SendAsync(HttpMethod.Get, root + "/draft-revisions?agentId=copywriter&draftId=draft", null, "agent/definitions:read");
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        using var versions = JsonDocument.Parse(await history.Content.ReadAsStringAsync());
        Assert.Equal(1, versions.RootElement.GetArrayLength());
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Post, root + "/discard", """{"agentId":"copywriter","draftId":"draft","expectedRevision":1}""", "agent/definitions:manage")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, draftRead, null, "agent/definitions:read")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, root + "/draft-revisions?agentId=copywriter&draftId=draft", null, "agent/definitions:read")).StatusCode);
    }
}
