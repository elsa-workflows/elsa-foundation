using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.UserTasks;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Sdk;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class UserTaskPostgreSqlTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableFact] public Task Journal_identity_race_retention_and_forms() => UserTaskProviderContract.Run(fixture,
        value => new RuntimePostgreSqlDbContext(new DbContextOptionsBuilder<RuntimePostgreSqlDbContext>().UseNpgsql(value).Options));
}
[Collection(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class UserTaskSqlServerTests(RuntimeBookmarksSqlServerFixture fixture)
{
    [SkippableFact] public Task Journal_identity_race_retention_and_forms() => UserTaskProviderContract.Run(fixture,
        value => new RuntimeSqlServerDbContext(new DbContextOptionsBuilder<RuntimeSqlServerDbContext>().UseSqlServer(value).Options));
}
[Collection(RuntimeBookmarksMySqlFixture.CollectionName)]
public sealed class UserTaskMySqlTests(RuntimeBookmarksMySqlFixture fixture)
{
    [SkippableFact] public Task Journal_identity_race_retention_and_forms() => UserTaskProviderContract.Run(fixture,
        value => new RuntimeMySqlDbContext(new DbContextOptionsBuilder<RuntimeMySqlDbContext>().UseMySQL(value).Options));
}
internal static class UserTaskProviderContract
{
    public static async Task Run(RuntimeBookmarksProviderFixture fixture, Func<string, RuntimeDbContext> open)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        var tenant = "task-provider-" + Guid.NewGuid().ToString("N"); var access = new Access(tenant); var now = DateTimeOffset.UtcNow;
        var codec = new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = "user-task-provider-recovery-key-at-least-32" }));
        EfWorkflowExecutionStateStore Runs(RuntimeDbContext context) => new(context, access, codec);
        UserTaskRecord TaskRow(string id, string? claimant = null) => new(id, tenant, "aG", "invocation-" + id, "node", "operation", "Purchase", null,
            claimant is null ? "aa" : null, claimant is null ? null : "finance", claimant, ["Approve", "Reject"], [], null, JsonSerializer.SerializeToElement(new { }),
            claimant is null ? UserTaskStatus.Open : UserTaskStatus.Claimed, 1, now, now, [new("created", "workflow", now)]);
        await using (var setup = open(fixture.ConnectionString))
        {
            await setup.Database.EnsureCreatedAsync();
            foreach (var id in new[] { "aa", "aG" }) await Runs(setup).SaveAsync(new(id, new("artifact", "definition", "version", "1", "hash"), WorkflowExecutionStatus.Completed,
                null, now, now, now, now, null, null, tenant, new Dictionary<string, string>()));
            var store = new EfUserTaskStore(setup, access);
            Assert.True(await store.TryWriteAsync(TaskRow("race"), null));
            Assert.True(await store.TryWriteAsync(TaskRow("mine-aa", "aa"), null));
            Assert.True(await store.TryWriteAsync(TaskRow("other-aG", "aG"), null));
            var mine = await store.QueryAsync(new(tenant, "aa", new HashSet<string> { "finance" }, "mine", 10));
            Assert.Equal(new[] { "mine-aa", "race" }, mine.Items.Select(x => x.Id).Order(StringComparer.Ordinal));
            Assert.DoesNotContain(mine.Items, x => x.ClaimantId == "aG");
            // The encoded execution keys of aa/aG differ only by case. A foreign task must never retain aa.
            Assert.True(await Runs(setup).DeleteAsync("aa"));
            Assert.False(await Runs(setup).DeleteAsync("aG"));
            var form = new UserTaskFormVersion(tenant, new("Purchase", "1", "Purchase", UserTaskProtocol.BasicForms, [new("AccountID", "Account", "text")]), now, "author");
            Assert.True(await store.TryCreateFormAsync(form)); Assert.False(await store.TryCreateFormAsync(form));
            Assert.Null(await store.FindFormAsync(tenant, "purchase", "1"));
        }
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var readers = 0;
        async Task<bool> Race(string actor)
        {
            await using var context = open(fixture.ConnectionString); var store = new EfUserTaskStore(context, access);
            var before = (await store.FindAsync(tenant, "race"))!;
            if (Interlocked.Increment(ref readers) == 2) ready.SetResult();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
            return await store.TryWriteAsync(before with { Revision = 2, Status = UserTaskStatus.CompletionPending,
                Decision = new("request-" + actor, actor, new(before.Id, "completion-" + actor, "Approve", JsonSerializer.SerializeToElement(new { })), now),
                History = before.History.Append(new UserTaskHistory("accepted", actor, now, "Approve")).ToArray() }, before.Revision);
        }
        var outcomes = await Task.WhenAll(Race("alice"), Race("bob")); Assert.Single(outcomes, value => value);
        await using var verification = open(fixture.ConnectionString); var journal = new EfUserTaskStore(verification, access);
        var winner = (await journal.FindAsync(tenant, "race"))!;
        Assert.Equal(2, winner.Revision); Assert.Equal(2, winner.History.Count); Assert.Equal(winner.History[1].ActorId, winner.Decision!.ActorId);
        Assert.Equal("AccountID", Assert.Single((await journal.FindFormAsync(tenant, "Purchase", "1"))!.Form.Fields).Key);
        foreach (var item in (await journal.RecoverAsync(100, null)).Items)
            Assert.True(await journal.TryWriteAsync(item with { Revision = item.Revision + 1, Status = UserTaskStatus.Completed }, item.Revision));
        Assert.True(await Runs(verification).DeleteAsync("aG"));
        Assert.Equal(UserTaskStatus.Completed, (await journal.FindAsync(tenant, "race"))!.Status);
    }
    private sealed class Access(string scope) : IPersistenceAccessContextAccessor { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new(scope)); }
}
