using Elsa.Agent.Specialists.Core.Contracts;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Agent.Specialists.Persistence.EntityFrameworkCore;
using Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Agent.Specialists.ProviderTests;

public sealed class SpecialistJournalProviderTests
{
    [SkippableFact]
    public Task Conditional_ownership_and_accepted_replay_on_postgresql() => ProviderDatabase.RunAsync("PostgreSql", connection => RunAsync("PostgreSql", connection));
    [SkippableFact]
    public Task Conditional_ownership_and_accepted_replay_on_sql_server() => ProviderDatabase.RunAsync("SqlServer", connection => RunAsync("SqlServer", connection));
    [SkippableFact]
    public Task Conditional_ownership_and_accepted_replay_on_mysql() => ProviderDatabase.RunAsync("MySql", connection => RunAsync("MySql", connection));

    private static async Task RunAsync(string providerName, string connection)
    {
        var now = DateTimeOffset.UtcNow;
        var identity = new SpecialistInvocationIdentity("tenant", "tenant", "workflow", "attempt", "operation", "artifact", "hash", "version", "template");
        var candidate = new SpecialistTurnDocument(new string('a', 64), SpecialistTurnDocument.CalculateOperationKey(identity), new string('b', 64), identity,
            "opaque-protected-request", now.AddMinutes(5));
        await using (var first = Host())
        {
            await using (var scope = first.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<SpecialistsDbContext>();
                await context.Database.MigrateAsync();
                Assert.Single(await context.Database.GetAppliedMigrationsAsync());
            }
            var handles = await Task.WhenAll(Enumerable.Range(0, 8).Select(async index =>
            {
                await using var scope = first.CreateAsyncScope();
                var alternate = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(BitConverter.GetBytes(index))).ToLowerInvariant();
                return (await scope.ServiceProvider.GetRequiredService<ISpecialistTurnStore>().StageAsync(candidate with { Handle = alternate }, CancellationToken.None)).Handle;
            }));
            var handle = Assert.Single(handles.Distinct());
            candidate = candidate with { Handle = handle };
            var claims = await Task.WhenAll(Enumerable.Range(0, 8).Select(async index =>
            {
                await using var scope = first.CreateAsyncScope();
                return await scope.ServiceProvider.GetRequiredService<ISpecialistTurnStore>().ClaimAsync(handle, identity, "child-" + index, "fence-" + index, now, CancellationToken.None);
            }));
            var winner = Assert.Single(claims, value => value is not null)!;
            await using var ownerScope = first.CreateAsyncScope();
            var store = ownerScope.ServiceProvider.GetRequiredService<ISpecialistTurnStore>();
            Assert.True((await store.GetCapabilitiesAsync()).IsDurable);
            Assert.False(await store.DispatchAsync(handle, identity, "stale-fence", now, CancellationToken.None));
            Assert.False(await store.AcceptAsync(handle, identity, winner.Fence!, "premature", now, CancellationToken.None));
            Assert.True(await store.DispatchAsync(handle, identity, winner.Fence!, now, CancellationToken.None));
            Assert.False(await store.AcceptAsync(handle, identity, "stale-fence", "wrong-owner", now, CancellationToken.None));
            Assert.False(await store.AcceptAsync(handle, identity, winner.Fence!, "expired", candidate.Deadline, CancellationToken.None));
            Assert.True(await store.AcceptAsync(handle, identity, winner.Fence!, "opaque-protected-result-and-session", now, CancellationToken.None));
            await store.FailAsync(handle, identity, winner.Fence!, "late-failure", true, CancellationToken.None);
        }
        await using var restarted = Host();
        await using var restartScope = restarted.CreateAsyncScope();
        var recovered = restartScope.ServiceProvider.GetRequiredService<ISpecialistTurnStore>();
        var accepted = await recovered.ReadAsync(candidate.Handle, identity, CancellationToken.None);
        Assert.NotNull(accepted);
        Assert.Equal(SpecialistTurnStatus.Accepted, accepted.Status);
        Assert.Equal("opaque-protected-result-and-session", accepted.ProtectedResult);
        Assert.Equal(candidate.Deadline, accepted.Deadline);
        Assert.Equal(accepted, await recovered.StageAsync(candidate with { Handle = new string('c', 64), Deadline = now.AddHours(1) }, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await recovered.StageAsync(candidate with { RequestHash = new string('d', 64) }, CancellationToken.None));
        Assert.Null(await recovered.ReadAsync(candidate.Handle, identity with { ActivityExecutionId = "foreign-attempt" }, CancellationToken.None));
        Assert.Null(await recovered.ClaimAsync(candidate.Handle, identity, "replacement", "replacement", now, CancellationToken.None));

        ServiceProvider Host()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IPersistenceAccessContextAccessor>(new Access());
            services.AddSpecialistsEntityFrameworkCore(new() { Provider = providerName, ConnectionString = connection });
            return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        }
    }

    private sealed class Access : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new("tenant"));
    }
}
