using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class RuntimeModelReceiptPostgreSqlSmokeTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableFact]
    public Task PostgreSql_retained_model_receipt_compares_without_mutating_state() => RuntimeModelReceiptProviderSmoke.RunAsync(fixture,
        connection => new RuntimePostgreSqlDbContext(new DbContextOptionsBuilder<RuntimePostgreSqlDbContext>().UseNpgsql(connection).Options));
}

[Collection(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class RuntimeModelReceiptSqlServerSmokeTests(RuntimeBookmarksSqlServerFixture fixture)
{
    [SkippableFact]
    public Task SqlServer_retained_model_receipt_compares_without_mutating_state() => RuntimeModelReceiptProviderSmoke.RunAsync(fixture,
        connection => new RuntimeSqlServerDbContext(new DbContextOptionsBuilder<RuntimeSqlServerDbContext>().UseSqlServer(connection).Options));
}

[Collection(RuntimeBookmarksMySqlFixture.CollectionName)]
public sealed class RuntimeModelReceiptMySqlSmokeTests(RuntimeBookmarksMySqlFixture fixture)
{
    [SkippableFact]
    public Task MySql_retained_model_receipt_compares_without_mutating_state() => RuntimeModelReceiptProviderSmoke.RunAsync(fixture,
        connection => new RuntimeMySqlDbContext(new DbContextOptionsBuilder<RuntimeMySqlDbContext>().UseMySQL(connection).Options));
}

internal static class RuntimeModelReceiptProviderSmoke
{
    private static readonly DateTimeOffset Now = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
    public static async Task RunAsync(RuntimeBookmarksProviderFixture fixture, Func<string, RuntimeDbContext> open)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        foreach (var lifecycle in new[] { RuntimeModelLifecycle.Running, RuntimeModelLifecycle.TerminalSettled })
        {
            var access = new Access(new("model-receipt-" + Guid.NewGuid().ToString("N")));
            var scopeHash = EfRelationalIdentity.Hash(access.Current.RequireScope().Value);
            var lease = new RuntimeExecutionLease("lease", "execution", "owner", Now, Now.AddMinutes(10), 1);
            await using (var context = open(fixture.ConnectionString))
            {
                await context.Database.EnsureCreatedAsync();
                IWorkflowExecutableStore artifacts = new EfWorkflowExecutableStore(context, access);
                var seed = new WorkflowExecutableIdentity("artifact", "definition", "version", "1.0.0", "pending");
                var model = new RuntimeModelMaterial(1, seed, new("test.model", 1), "receipt-probe-v1",
                    JsonSerializer.SerializeToElement(new { waiting = "human" }), new(1, []), [], [], []);
                var pin = seed with { ArtifactHash = model.ComputeContentHash() };
                model = new(1, pin, model.Protocol, model.Profile, model.Definition, model.InputContract, [], [], []);
                await artifacts.SaveModelAsync(model);
                var material = (await artifacts.FindMaterialAsync(pin.ArtifactId))!;
                await new EfExecutionLivenessStateStore(context, access, new UnusedCodec()).SaveAsync(new("ownership:execution", "execution", lease, null, null, null));
                var leases = new WorkflowExecutableRootWriteLeaseManager(artifacts, Options.Create(new WorkflowExecutableGarbageCollectionOptions()), new Clock());
                var store = new EfRuntimeCheckpointCommitStore(context, access, new Clock(), leases);
                var state = new RuntimeModelExecutionState(1, access.Current.RequireScope(), "execution", null, model.Protocol, pin, material.IncarnationId,
                    1, lifecycle, new("system", "initiator"), Now, Now, JsonSerializer.SerializeToElement(new { waiting = "human" }),
                    lifecycle == RuntimeModelLifecycle.TerminalSettled ? JsonSerializer.SerializeToElement(new { status = "Completed" }) : null);
                RuntimeCheckpointCommit Commit(string id, long? expected, DateTimeOffset at) => new(id,
                    new(id, "ModelDecision", "execution", at, [], new Dictionary<string, string>()), new(null, null, [], [], [], [], []), [], new Dictionary<string, string>())
                {
                    ExpectedFence = lease.ToFence(),
                    ModelState = new(expected, state, new(state.Scope, state.ExecutionId, id, "test.input", id, state.Revision, at,
                        null, null, JsonSerializer.SerializeToElement(new { status = expected is null ? "Accepted" : "Rejected" })))
                };
                await store.CommitAsync(Commit("start", null, Now), new(RuntimeCheckpointPersistenceMode.Immediate));
                var before = await context.Set<RuntimeModelExecutionEntity>().AsNoTracking().SingleAsync(row => row.ScopeKeyHash == scopeHash);
                await store.CommitAsync(Commit("obsolete", 1, Now.AddSeconds(1)), new(RuntimeCheckpointPersistenceMode.Immediate));
                var after = await context.Set<RuntimeModelExecutionEntity>().AsNoTracking().SingleAsync(row => row.ScopeKeyHash == scopeHash);
                Assert.Equal(before.ContentJson, after.ContentJson);
                Assert.Equal(before.Revision, after.Revision);
            }
            await using var restarted = open(fixture.ConnectionString);
            var reader = new EfRuntimeModelExecutionReader(restarted, access, new UnusedCodec());
            Assert.Equal(lifecycle, (await reader.FindAsync("execution"))!.Lifecycle);
            Assert.Equal(1, (await reader.FindAsync("execution"))!.Revision);
            Assert.Equal("Rejected", (await reader.FindInputAsync("execution", "obsolete"))!.Result.GetProperty("status").GetString());
        }
    }

    private sealed class Access(PersistenceScope scope) : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(scope); }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class UnusedCodec : IRuntimeRecoveryContinuationCodec
    {
        public string Encode(string purpose, ReadOnlySpan<byte> payload) => throw new NotSupportedException();
        public byte[] Decode(string purpose, string token) => throw new NotSupportedException();
    }
}
