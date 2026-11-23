using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Persistence.Core;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.Testing;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Commands;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfWorkflowDesignPersistenceTests
{
    [Fact]
    public async Task A_real_payload_serializer_and_fresh_SQLite_context_preserve_the_complete_argument_document()
    {
        const string raw = """{ "OrderID":9007199254740993, "amount":1.00, "nil":null, "text":"\u0061 <+>" }""";
        var serializer = SerializationServices.PayloadSerializer();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        ArgumentValue argument;
        using (var source = JsonDocument.Parse(raw)) argument = new(source.RootElement, "Literal");
        var state = new WorkflowDefinitionState(
            [new("variable", "Variable", new TypeReference("Object"), null, ArgumentValue.FromClr(new PersistedScopedDefault { Name = "René <+>" }, "Literal"))],
            new ActivityNode("root", "activity", [new("input", argument, null, null, null, null)], []), [], [], null);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        string id;
        await using (var db = Create(connection))
        {
            await db.Database.EnsureCreatedAsync();
            db.Definitions.Add(new WorkflowDefinition { Id = "json-definition", TenantId = "tenant-a", Name = "JSON" });
            await db.SaveChangesAsync();
            var create = new EfCreateDraftCommand(db, access, new EfDesignAtomicWriter(db, access), new TestIdentity(), serializer, new TestLockProvider());
            id = await create.Execute(new("json-create"), "json-definition", state);
        }
        await using var fresh = Create(connection);
        var stored = await fresh.Drafts.AsNoTracking().SingleAsync(row => row.Id == id);
        using (var wire = JsonDocument.Parse(stored.StateSource))
            Assert.Equal(raw, wire.RootElement.GetProperty("rootActivity").GetProperty("inputs")[0].GetProperty("value").GetProperty("value").GetRawText());
        var loaded = (await new EfWorkflowDefinitionDraftStore(fresh, serializer, access).FindByIdAsync(id))!.State;
        var data = Assert.Single(loaded.RootActivity!.Inputs).Value.Value!.Value;
        Assert.Equal(raw, data.GetRawText());
        Assert.Equal(9007199254740993L, data.GetProperty("OrderID").GetInt64());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("nil").ValueKind);
        Assert.False(data.TryGetProperty("missing", out _));
        var value = Assert.Single(loaded.Variables).Default!.Value!.Value;
        Assert.Equal("René <+>", value.GetProperty("declared_name").GetString());
        Assert.False(value.TryGetProperty("name", out _));
        Assert.Equal("René <+>", JsonConversionContract.Deserialize<PersistedScopedDefault>(value)!.Name);
    }

    [JsonConversion(MemberMappings = ["declared_name=Name"])]
    public sealed class PersistedScopedDefault
    {
        public string Name { get; set; } = string.Empty;
    }
}
