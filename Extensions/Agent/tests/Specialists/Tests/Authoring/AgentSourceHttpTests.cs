using System.Net;
using System.Text.Json;
using Elsa.Agent.Specialists.Core.Contracts;
using Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.Stores;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Agent.Specialists.Tests.Authoring;

public sealed partial class SpecialistsApiHttpTests
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
        const string draft = root + "/copywriter/drafts/draft";
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Post, root, SourceBody(), "agent/definitions:manage")).StatusCode);
        using var saved = JsonDocument.Parse(SourceBody("Edited"));
        var body = JsonSerializer.Serialize(new { expectedRevision = 0, name = "Copywriter", content = saved.RootElement.GetProperty("content") });
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Put, draft, body, "agent/definitions:manage")).StatusCode);
        var stale = await SendAsync(HttpMethod.Put, draft, body, "agent/definitions:manage");
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var capture = await SendAsync(HttpMethod.Post, draft + "/captures", """{"expectedRevision":1,"operationId":"review-1"}""", "agent/definitions:manage");
        Assert.Equal(HttpStatusCode.OK, capture.StatusCode);
        var repeated = await SendAsync(HttpMethod.Post, draft + "/captures", """{"expectedRevision":1,"operationId":"review-1"}""", "agent/definitions:manage");
        Assert.Equal(await capture.Content.ReadAsStringAsync(), await repeated.Content.ReadAsStringAsync());
        var history = await SendAsync(HttpMethod.Get, draft + "/revisions", null, "agent/definitions:read");
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        using var versions = JsonDocument.Parse(await history.Content.ReadAsStringAsync());
        Assert.Equal(1, versions.RootElement.GetArrayLength());
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Post, draft + "/discard", """{"expectedRevision":1}""", "agent/definitions:manage")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, draft, null, "agent/definitions:read")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, draft + "/revisions", null, "agent/definitions:read")).StatusCode);
    }
}
