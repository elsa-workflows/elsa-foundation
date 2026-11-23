using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Persistence.Core;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.Testing;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
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
        using (var wire = JsonDocument.Parse(Assert.IsType<string>(stored.StateSource)))
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

        var firstText = stored.StateSource;
        var revision = stored.Source!.Revision;
        using var equivalent = JsonDocument.Parse("""{"text":"a <+>","nil":null,"amount":1e0,"OrderID":9007199254740993}""");
        var retry = state with { RootActivity = state.RootActivity! with
        {
            Inputs = [new("input", new ArgumentValue(equivalent.RootElement, "Literal"), null, null, null, null)]
        } };
        var replay = new EfCreateDraftCommand(fresh, access, new EfDesignAtomicWriter(fresh, access), new TestIdentity("retry"), serializer, new TestLockProvider());
        Assert.Equal(id, await replay.Execute(new("json-create"), "json-definition", retry));
        Assert.Equal(1, await fresh.Drafts.CountAsync());
        Assert.Equal(1, await fresh.Operations.CountAsync());
        var after = await fresh.Drafts.AsNoTracking().SingleAsync(row => row.Id == id);
        Assert.Equal(firstText, after.StateSource);
        Assert.Equal(revision, after.Source!.Revision);
    }

    [Fact]
    public async Task Malformed_materialized_state_is_a_normalized_serialization_failure_before_any_write()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Create(connection);
        await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var command = new EfMaterializeWorkflowDefinitionVersionCommand(db, access, new EfDesignAtomicWriter(db, access), SerializationServices.PayloadSerializer());
        var version = new WorkflowDefinitionVersion("definition", "1.0.0", "not JSON") { Id = "invalid", TenantId = "tenant-a" };
        var error = await Assert.ThrowsAsync<DesignPersistenceException>(() => command.Execute(new("invalid-state"), version));
        Assert.Equal(DesignPersistenceFailureKind.Serialization, error.FailureKind);
        Assert.Equal(DesignPersistenceDomain.Workflow, error.Domain);
        Assert.Equal(EfMaterializeWorkflowDefinitionVersionCommand.OperationKind, error.Operation);
        Assert.IsAssignableFrom<JsonException>(error.InnerException);
        Assert.Empty(await db.Operations.ToListAsync());
        Assert.Empty(await db.Versions.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_create_state_serialization_failure_names_the_selected_operation_before_any_write(bool inFolder)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Create(connection);
        await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var cause = new JsonException("State serializer failed.");
        var command = new EfAddWorkflowDefinitionCommand(db, access, new EfDesignAtomicWriter(db, access),
            new TestSerializer(writeFailure: cause), new TestIdentity(), new TestLockProvider());
        var definition = new WorkflowDefinition { Id = "definition", Name = "Definition" };
        var draft = new WorkflowDefinitionDraft { Id = "draft", WorkflowDefinitionId = "definition", State = State() };
        var failure = await Assert.ThrowsAsync<DesignPersistenceException>(() => command.Execute(new("create-failed"), definition, draft,
            [], [], inFolder ? "folder-00000000000000000000000000000001" : null, "actor"));
        Assert.Equal(inFolder ? "workflow.definition.create-in-folder.v1" : "workflow.definition.create.v1", failure.Operation);
        Assert.Equal(DesignPersistenceFailureKind.Serialization, failure.FailureKind);
        Assert.Same(cause, failure.InnerException);
        Assert.Empty(await db.Definitions.ToListAsync());
        Assert.Empty(await db.Drafts.ToListAsync());
        Assert.Empty(await db.Operations.ToListAsync());
    }

    [JsonConversion(MemberMappings = ["declared_name=Name"])]
    public sealed class PersistedScopedDefault
    {
        public string Name { get; set; } = string.Empty;
    }
}
