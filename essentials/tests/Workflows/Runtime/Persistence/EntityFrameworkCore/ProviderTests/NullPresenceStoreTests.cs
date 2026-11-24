using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Expressions.Core.Models;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Recovery;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Sdk;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

public sealed class NullPresenceLocalStoreTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InMemory_and_Sqlite_preserve_presence_across_runtime_stores(bool sqlite)
    {
        if (!sqlite) { await NullPresenceStoreContract.RunAsync(null); return; }
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        await NullPresenceStoreContract.RunAsync(context);
    }
}

[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class NullPresencePostgreSqlStoreTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableFact]
    public Task PostgreSql_preserves_runtime_null_and_absent() => NullPresenceStoreContract.NativeAsync(fixture,
        value => new RuntimePostgreSqlDbContext(new DbContextOptionsBuilder<RuntimePostgreSqlDbContext>().UseNpgsql(value).Options));
}
[Collection(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class NullPresenceSqlServerStoreTests(RuntimeBookmarksSqlServerFixture fixture)
{
    [SkippableFact]
    public Task SqlServer_preserves_runtime_null_and_absent() => NullPresenceStoreContract.NativeAsync(fixture,
        value => new RuntimeSqlServerDbContext(new DbContextOptionsBuilder<RuntimeSqlServerDbContext>().UseSqlServer(value).Options));
}
[Collection(RuntimeBookmarksMySqlFixture.CollectionName)]
public sealed class NullPresenceMySqlStoreTests(RuntimeBookmarksMySqlFixture fixture)
{
    [SkippableFact]
    public Task MySql_preserves_runtime_null_and_absent() => NullPresenceStoreContract.NativeAsync(fixture,
        value => new RuntimeMySqlDbContext(new DbContextOptionsBuilder<RuntimeMySqlDbContext>().UseMySQL(value).Options));
}

internal static class NullPresenceStoreContract
{
    public static async Task NativeAsync(RuntimeBookmarksProviderFixture fixture, Func<string, RuntimeDbContext> create)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        await using var context = create(fixture.ConnectionString);
        await context.Database.EnsureCreatedAsync();
        await RunAsync(context);
    }

    public static async Task RunAsync(RuntimeDbContext? context)
    {
        var scope = "null-presence-" + Guid.NewGuid().ToString("N");
        var accessor = new Accessor(scope);
        var codec = new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = new string('n', 32) }));
        IWorkflowExecutionStateStore workflows = context is null ? new InMemoryWorkflowExecutionStateStore() : new EfWorkflowExecutionStateStore(context, accessor, codec);
        IActivityExecutionStateStore activities = context is null ? new InMemoryActivityExecutionStateStore() : new EfActivityExecutionStateStore(context, accessor, codec);
        IDurableValueStateStore values = context is null ? new InMemoryDurableValueStateStore() : new EfDurableValueStateStore(context, accessor, codec);
        IWorkflowExecutableStore executables = context is null ? new InMemoryWorkflowExecutableStore() : new EfWorkflowExecutableStore(context, accessor);
        var now = DateTimeOffset.UnixEpoch;
        var type = new ValueTypeDescriptor("String");
        var entries = new Dictionary<string, ValueEnvelope> { ["null"] = ValueEnvelope.Null(type, ValueProtectionPolicy.InstanceInline), ["absent"] = ValueEnvelope.Absent(type, ValueProtectionPolicy.InstanceInline) };
        var bindings = entries.ToDictionary(pair => pair.Key, pair => new RuntimeInputBinding(pair.Key, type, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal, literal: pair.Value));
        var node = new ExecutableNode("root", "root", "test", "1", "test", JsonSerializer.SerializeToElement(new { }), bindings, new Dictionary<string, string>());
        var identity = new WorkflowExecutableIdentity("null-artifact", "definition", "version", "1", "hash");
        await executables.SaveAsync(new(identity, node, new Dictionary<string, WorkflowExecutableResumeTarget>(), now, new Dictionary<string, string>(), incidentStrategy: IncidentStrategyBuiltIns.FaultReference));
        var workflow = new WorkflowExecutionState("null-workflow", identity, WorkflowExecutionStatus.Running, null, now, now, now, null, null, null, scope, new Dictionary<string, string>())
            { RootVariableFrame = new VariableFrameFactory().CreateRoot("null-workflow", VariableReference.WorkflowScopeId, entries) };
        await workflows.SaveAsync(workflow);
        var activity = new ActivityExecutionState(new("invocation", "null-workflow", "root", "root", "test", "1"),
            ActivityExecutionStatus.Completed, null, 1, now, now, now, null, null, null, null,
            ActivitySchedulingProvenance.From("null-workflow", null, null, null, null, null, null, "test"), null, [], [], 0, 0, new Dictionary<string, string>(),
            InputSnapshot: new("invocation", "contract", "bindings", entries, now));
        await activities.SaveAsync(activity);
        foreach (var change in RuntimeWorkflowStateSeed.BuildSeedChanges("null-workflow", new Dictionary<string, object?> { ["null"] = null }, now))
            await values.SaveAsync(change.State!);
        await values.SaveAsync(new("output-null", "null-workflow", "output:null", new("String", null, null), DurableValueLifecycle.Instance,
            DurableValueStorage.Inline, JsonSerializer.SerializeToElement<object?>(null), null, "invocation", now, new Dictionary<string, string>()));
        foreach (var minimum in new[] { DurableValueStorage.External, DurableValueStorage.Custom })
            await values.SaveAsync(new("output-null-" + minimum, "null-workflow", "output:" + minimum, new("String", null, null), DurableValueLifecycle.Instance,
                DurableValueStorage.Inline, JsonSerializer.SerializeToElement<object?>(null), null, "invocation", now,
                new Dictionary<string, string> { [RuntimeMetadataKeys.InputName] = minimum.ToString(), [RuntimeMetadataKeys.NullMinimumStorage] = minimum.ToString() }));
        context?.ChangeTracker.Clear();
        var restored = await executables.FindAsync(identity.ArtifactId);
        Assert.Equal(ValuePresence.Absent, restored!.RootActivity.InputBindings["absent"].Literal!.Presence);
        Assert.Null(restored.RootActivity.InputBindings["absent"].LiteralValue);
        Assert.Equal(JsonValueKind.Null, restored.RootActivity.InputBindings["null"].LiteralValue!.Value.ValueKind);
        var storedWorkflow = await workflows.FindAsync("null-workflow");
        Assert.Equal(ValuePresence.ExplicitNull, storedWorkflow!.RootVariableFrame!.Values["null"].Presence);
        Assert.Equal(ValuePresence.Absent, storedWorkflow.RootVariableFrame.Values["absent"].Presence);
        var storedActivity = await activities.FindAsync("null-workflow", "invocation");
        Assert.Equal(ValuePresence.ExplicitNull, storedActivity!.InputSnapshot!.Values["null"].Presence);
        Assert.Equal(ValuePresence.Absent, storedActivity.InputSnapshot.Values["absent"].Presence);
        var storedValues = (await values.ListPageAsync(new("null-workflow", 10))).Items;
        Assert.Equal(4, storedValues.Count);
        var envelopes = RuntimeInputBindingStateProjection.ProjectWorkflowInputEnvelopes(storedValues);
        foreach (var minimum in new[] { DurableValueStorage.External, DurableValueStorage.Custom })
        {
            Assert.Equal(ValuePresence.ExplicitNull, envelopes[minimum.ToString()].Presence);
            Assert.Equal(minimum, envelopes[minimum.ToString()].Policy.Storage);
        }
        Assert.All(storedValues, value => Assert.Equal(JsonValueKind.Null, value.InlineValue!.Value.ValueKind));
        Assert.Null(await values.FindAsync("null-workflow", "output-absent"));
    }

    private sealed class Accessor(string scope) : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope)); }
}
