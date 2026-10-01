using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.PostCommitOutboxContinuationDeferralContract;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Runs <see cref="PostCommitOutboxContinuationDeferralContract"/> against the in-memory checkpoint store and the EF Core
/// outbox store on SQLite. The native-provider smoke tests run the same contract on PostgreSQL, SQL Server, and MySQL.
/// </summary>
public sealed class PostCommitOutboxContinuationDeferralContractTests : IAsyncDisposable
{
    private const string InMemory = "in-memory";
    private const string EntityFramework = "entity-framework";
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private RuntimeSqliteDbContext? _context;

    public static TheoryData<string> Stores => new(InMemory, EntityFramework);

    [Theory]
    [MemberData(nameof(Stores))]
    public async Task A_sweep_leaves_a_continuation_to_the_drain_that_owns_its_execution(string store) =>
        await ASweepLeavesAContinuationToTheDrainThatOwnsItsExecutionAsync(await CreateBackendAsync(store));

    [Theory]
    [MemberData(nameof(Stores))]
    public async Task A_sweep_claims_a_continuation_once_its_owner_releases_or_loses_the_lease(string store) =>
        await ASweepClaimsAContinuationOnceItsOwnerReleasesOrLosesTheLeaseAsync(await CreateBackendAsync(store));

    public async ValueTask DisposeAsync()
    {
        if (_context is not null)
            await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task<Backend> CreateBackendAsync(string store)
    {
        if (store == InMemory)
        {
            var liveness = new InMemoryExecutionLivenessStateStore();
            var checkpoints = new InMemoryRuntimeCheckpointCommitStore(operationalStateStore: liveness);
            return new Backend(checkpoints, item => checkpoints.AddPendingForTestingAsync(item), Ownership(liveness));
        }

        await _connection.OpenAsync();
        _context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(_connection).Options);
        await _context.Database.EnsureCreatedAsync();
        return PostCommitOutboxContinuationDeferralContract.EntityFramework(_context, "tenant-a");
    }
}
