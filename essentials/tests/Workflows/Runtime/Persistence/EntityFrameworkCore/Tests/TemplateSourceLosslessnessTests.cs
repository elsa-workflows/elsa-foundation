using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class TemplateSourceLosslessnessTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("computed-alias")]
    [InlineData("constructor-added-requirement")]
    [InlineData("constructor-deduplicated-requirement")]
    [InlineData("null-owner-requirement")]
    public async Task Current_template_source_profile_does_not_drop_aliases_or_repair_missing_owner_data(string change)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(); var store = new EfExecutableActivityTemplateStore(context, new Accessor(), new Cursor());
        var root = new ExecutableNode("node", "node", "fixture", "1", "fixture.consumer", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>());
        var requirements = new[] { new RuntimeRequirement("fixture.consumer", "1") };
        var hash = ActivityTemplateHasher.Compute(root, new Dictionary<string, WorkflowExecutableResumeTarget>(), [], [], requirements, [], "fingerprint", new Dictionary<string, string>());
        var template = new ExecutableActivityTemplate("template", hash, root, new Dictionary<string, WorkflowExecutableResumeTarget>(), [], [], requirements,
            "fingerprint", new Dictionary<string, string>(), DateTimeOffset.UnixEpoch); await store.SaveAsync(template);
        if (change != "valid")
        {
            var row = await context.ExecutableActivityTemplates.AsNoTracking().SingleAsync(); var document = JsonNode.Parse(row.ContentJson)!;
            if (change == "computed-alias") document["template"]!["nodesById"] = new JsonObject();
            else if (change == "constructor-deduplicated-requirement")
            {
                var requirementsBody = document["template"]!["runtimeRequirements"]!.AsArray();
                requirementsBody.Add(requirementsBody[0]!.DeepClone());
            }
            else if (change == "null-owner-requirement") document["template"]!["runtimeRequirements"] = JsonNode.Parse("[null]");
            else document["template"]!["runtimeRequirements"] = new JsonArray();
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_runtime_executable_activity_template SET ContentJson={document.ToJsonString()}");
            Assert.Equal("runtime_source_stored_content_invalid", (await Assert.ThrowsAsync<RuntimeSourceReadException>(() =>
                store.FindBoundedAsync("template", new("partition"), new()).AsTask())).Code);
        }
        else Assert.Equal(template.TemplateHash, (await store.FindBoundedAsync("template", new("partition"), new()))!.Template.TemplateHash);
    }
    private sealed class Accessor : IPersistenceAccessContextAccessor { public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new("partition")); }
    private sealed class Cursor : IRuntimeRecoveryContinuationCodec { public string Encode(string purpose, ReadOnlySpan<byte> payload) => Convert.ToBase64String(payload); public byte[] Decode(string purpose, string token) => Convert.FromBase64String(token); }
}
