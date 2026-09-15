using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Sdk;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class RuntimeOperationalStatePostgreSqlSmokeTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableFact]
    public Task PostgreSql_runtime_operational_state_smoke() => RuntimeOperationalStateProviderSmoke.RunAsync(fixture, c => new BookmarkStatePostgreSqlDbContext(new DbContextOptionsBuilder<BookmarkStatePostgreSqlDbContext>().UseNpgsql(c).Options), BookmarkStatePostgreSqlDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class RuntimeOperationalStateSqlServerSmokeTests(RuntimeBookmarksSqlServerFixture fixture)
{
    [SkippableFact]
    public Task SqlServer_runtime_operational_state_smoke() => RuntimeOperationalStateProviderSmoke.RunAsync(fixture, c => new BookmarkStateSqlServerDbContext(new DbContextOptionsBuilder<BookmarkStateSqlServerDbContext>().UseSqlServer(c).Options), BookmarkStateSqlServerDbContext.ExpectedProviderName);
}

[Collection(RuntimeBookmarksMySqlFixture.CollectionName)]
public sealed class RuntimeOperationalStateMySqlSmokeTests(RuntimeBookmarksMySqlFixture fixture)
{
    [SkippableFact]
    public Task MySql_runtime_operational_state_smoke() => RuntimeOperationalStateProviderSmoke.RunAsync(fixture, c => new BookmarkStateMySqlDbContext(new DbContextOptionsBuilder<BookmarkStateMySqlDbContext>().UseMySQL(c).Options), BookmarkStateMySqlDbContext.ExpectedProviderName);
}

internal static class RuntimeOperationalStateProviderSmoke
{
    private const string SigningKey = "ef-runtime-r14-r15-provider-signing-key-32-bytes";

    public static async Task RunAsync(RuntimeBookmarksProviderFixture fixture, Func<string, BookmarkStateDbContext> createContext, string expectedProvider)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        var scope = $"native-{Guid.NewGuid():N}";
        var connectionString = fixture.ConnectionString;
        await using (var context = createContext(connectionString))
        {
            Assert.Equal(expectedProvider, context.Database.ProviderName);
            await context.Database.EnsureCreatedAsync();
            var accessor = new FixedAccessor(scope);
            var codec = new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = SigningKey }));
            var values = new EfDurableValueStateStore(context, accessor, codec);
            var scheduler = new EfSchedulerStateStore(context, accessor);
            var liveness = new EfExecutionLivenessStateStore(context, accessor, codec);
            var holds = new EfWorkflowHoldStateStore(context, accessor);
            var value = Value("aa", "workflow-a");
            await values.SaveAsync(value);
            await values.SaveAsync(Value("aG", "workflow-a"));
            await scheduler.SaveAsync(new SchedulerState("workflow-a", 4));
            var livenessState = new ExecutionLivenessState("state-a", "workflow-a", null, null, null, null);
            Assert.Equal(ExecutionLivenessStateWriteStatus.Saved, (await liveness.TrySaveAsync(livenessState, 0)).Status);
            await holds.SaveAsync(new WorkflowHoldState(
                "global-control",
                activeHolds: [WorkflowHold.ForWorkflowExecution("embedded", "workflow-a", DateTimeOffset.UtcNow, "provider-smoke", "provider smoke hold")]));
            Assert.Equal("aG", (await values.ListPageAsync(new DurableValueStatePageQuery("workflow-a", 1))).Items.Single().DurableValueId);
            Assert.Equal(4, (await scheduler.FindAsync("workflow-a"))!.Version);
            Assert.Equal(1, (await liveness.FindVersionedAsync("workflow-a", "state-a"))!.Revision);
            Assert.Single(await holds.ListForWorkflowExecutionAsync("workflow-a"));

            await using var transaction = await context.Database.BeginTransactionAsync();
            await values.SaveAsync(Value("rolled-back", "workflow-a"));
            await transaction.RollbackAsync();
        }

        await using (var fresh = createContext(connectionString))
        {
            var values = new EfDurableValueStateStore(fresh, new FixedAccessor(scope), new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = SigningKey })));
            Assert.Null(await values.FindAsync("workflow-a", "rolled-back"));
            var liveness = new EfExecutionLivenessStateStore(fresh, new FixedAccessor(scope), new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = SigningKey })));
            var holds = new EfWorkflowHoldStateStore(fresh, new FixedAccessor(scope));
            Assert.NotNull(await liveness.FindAsync("workflow-a", "state-a"));
            Assert.Single(await holds.ListForWorkflowExecutionAsync("workflow-a"));
            await using var left = createContext(connectionString);
            await using var right = createContext(connectionString);
            var scopeKey = Elsa.Persistence.EntityFramework.EfRelationalIdentity.Encode(scope);
            var scopeHash = Elsa.Persistence.EntityFramework.EfRelationalIdentity.Hash(scope);
            var durableValueKey = Elsa.Persistence.EntityFramework.EfRelationalIdentity.Encode("aa");
            var durableValueHash = Elsa.Persistence.EntityFramework.EfRelationalIdentity.Hash("aa");
            var leftRow = await left.DurableValueStates.SingleAsync(row => row.ScopeKey == scopeKey && row.ScopeKeyHash == scopeHash && row.DurableValueId == durableValueKey && row.DurableValueIdHash == durableValueHash);
            var rightRow = await right.DurableValueStates.SingleAsync(row => row.ScopeKey == scopeKey && row.ScopeKeyHash == scopeHash && row.DurableValueId == durableValueKey && row.DurableValueIdHash == durableValueHash);
            leftRow.Revision++;
            await left.SaveChangesAsync();
            rightRow.Revision++;
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => right.SaveChangesAsync());
        }
    }

    private static DurableValueState Value(string valueId, string workflowId) => new(valueId, workflowId, valueId, new RuntimeValueTypeDescriptor("json", null, null), DurableValueLifecycle.Result, DurableValueStorage.Inline, JsonDocument.Parse("42").RootElement, null, null, DateTimeOffset.UtcNow, new Dictionary<string, string>());
    private sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor { public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope)); }
}
