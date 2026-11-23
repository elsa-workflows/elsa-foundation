using System.Text;
using System.Text.Json;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfWorkflowDesignPersistenceTests
{
    public static IEnumerable<object[]> SavedViewVectors()
    {
        using var stream = typeof(EfWorkflowDesignPersistenceTests).Assembly.GetManifestResourceStream("S9NestedBindingVectors.json")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.GetProperty("savedViews").EnumerateArray().Select(vector => new object[] { vector.Clone() }).ToArray();
    }

    [Theory]
    [MemberData(nameof(SavedViewVectors))]
    public async Task A_real_saved_view_pins_both_the_keyed_create_and_atomic_request_JSON_projection(JsonElement vector)
    {
        const string freeze = "JSON hashes freeze at Elsa 4's first stable release; before then regenerate goldens and fixtures together, never add an old-hash verifier.";
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Create(connection);
        await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var library = new EfWorkflowLibraryStore(db, access, new EfDesignAtomicWriter(db, access), new TestLockProvider());
        var input = new WorkflowLibraryViewInput(vector.GetProperty("title").GetString()!, new(SearchTerm: "René <+>", Sort: "modified"),
            vector.GetProperty("shared").GetBoolean(), vector.GetProperty("columns").EnumerateArray().Select(column => column.GetString()!).ToArray());
        var result = await library.SaveViewAsync(null, input, "alice", true, "key");
        var row = await db.LibraryViews.AsNoTracking().SingleAsync();
        var marker = await db.Operations.AsNoTracking().SingleAsync(operation => operation.OperationKind == "workflow.view.save.v1");
        using var content = JsonDocument.Parse(row.ContentJson);
        var canonical = Encoding.UTF8.GetString(JsonHashInput.CanonicalBytes(content.RootElement));
        var name = vector.GetProperty("name").GetString();
        Assert.True(canonical == vector.GetProperty("contentCanonical").GetString(), $"{name}: saved content preimage differs. {freeze}");
        Assert.True(row.OperationFingerprint == vector.GetProperty("keyedHash").GetString(), $"{name}: keyed create hash {row.OperationFingerprint} differs. {freeze}");
        Assert.True(marker.RequestFingerprint == vector.GetProperty("requestHash").GetString(), $"{name}: atomic request hash {marker.RequestFingerprint} differs. {freeze}");
        var text = row.ContentJson;
        var audit = await db.LibraryAudit.CountAsync();
        Assert.Equal(result.Id, (await library.SaveViewAsync(null, input, "alice", true, "key")).Id);
        Assert.Equal(audit, await db.LibraryAudit.CountAsync());
        Assert.Equal(text, (await db.LibraryViews.AsNoTracking().SingleAsync()).ContentJson);
    }

    [Fact]
    public async Task A_saved_view_does_not_accept_the_old_nested_text_fingerprint_or_rewrite_its_content()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Create(connection);
        await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var library = new EfWorkflowLibraryStore(db, access, new EfDesignAtomicWriter(db, access), new TestLockProvider());
        var input = new WorkflowLibraryViewInput("View <é>", new(SearchTerm: "René <+>", Sort: "modified"), Columns: ["name", "tags"]);
        await library.SaveViewAsync(null, input, "alice", false, "key");
        var row = await db.LibraryViews.SingleAsync();
        row.OperationFingerprint = JsonHashInput.Hash(new { name = input.Name, content = row.ContentJson, input.Shared });
        await db.SaveChangesAsync();
        var text = row.ContentJson;
        var audit = await db.LibraryAudit.CountAsync();
        await Assert.ThrowsAsync<WorkflowLibraryConflictException>(() => library.SaveViewAsync(null, input, "alice", false, "key"));
        Assert.Equal(audit, await db.LibraryAudit.CountAsync());
        Assert.Equal(text, (await db.LibraryViews.AsNoTracking().SingleAsync()).ContentJson);
        Assert.Equal(1, await db.Operations.CountAsync());
    }
}
