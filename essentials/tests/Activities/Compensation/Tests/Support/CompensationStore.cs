using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Compensation.Tests.Support;

/// <summary>
/// The runtime store a <see cref="CompensationHost"/> runs on (spec 261 OD18). A durable store outlives a host
/// generation, so a test can stop a host and start the next one over the same data; the in-memory stores belong to one
/// generation and refuse a second.
/// </summary>
public abstract class CompensationStore : IDisposable
{
    private const string RecoverySigningKey = "compensation-nesting-recovery-signing-key-32";
    private const string HierarchySigningKey = "compensation-nesting-hierarchy-signing-key-32";

    /// <summary>The provider name, as the qualification record names it.</summary>
    public abstract string Name { get; }

    /// <summary>Whether a later host generation sees what an earlier one committed.</summary>
    public abstract bool SurvivesRestart { get; }

    /// <summary>Registers the runtime stores on a host generation's services.</summary>
    public abstract void Configure(IServiceCollection services);

    public virtual void Dispose()
    {
    }

    /// <summary>A SQLite file of its own, removed on dispose: the store every pull request runs the journeys on.</summary>
    public static CompensationStore Sqlite() => new SqliteStore();

    /// <summary>The runtime's default in-memory stores: development and tests only, with no restart guarantee (Q7).</summary>
    public static CompensationStore InMemory() => new InMemoryStore();

    /// <summary>
    /// A server provider (<c>PostgreSql</c>, <c>SqlServer</c> or <c>MySql</c>) on an empty database of its own, whose
    /// schema the module migrator installs when the first host starts.
    /// </summary>
    public static CompensationStore EntityFramework(string provider, string connectionString) => new EntityFrameworkStore(provider, connectionString);

    private static void AddEntityFramework(IServiceCollection services, string provider, string connectionString) =>
        services
            .AddRuntimeEntityFrameworkCore(new RuntimeEntityFrameworkCoreOptions
            {
                Provider = provider,
                ConnectionString = connectionString,
                RecoveryContinuationSigningKey = RecoverySigningKey,
                HierarchyCursorSigningKey = HierarchySigningKey
            })
            .AddEfModuleMigrations<RuntimeDbContext>(provider);

    private sealed class SqliteStore : CompensationStore
    {
        private readonly string _databasePath = Path.Join(Path.GetTempPath(), $"elsa-compensation-nesting-{Guid.NewGuid():N}.db");

        public override string Name => "Sqlite";
        public override bool SurvivesRestart => true;

        public override void Configure(IServiceCollection services) =>
            AddEntityFramework(services, "Sqlite", $"Data Source={_databasePath};Pooling=False");

        public override void Dispose()
        {
            foreach (var file in new[] { _databasePath, $"{_databasePath}-wal", $"{_databasePath}-shm" })
                File.Delete(file);
        }
    }

    private sealed class InMemoryStore : CompensationStore
    {
        private bool _started;

        public override string Name => "InMemory";
        public override bool SurvivesRestart => false;

        public override void Configure(IServiceCollection services)
        {
            // The harness registers the in-memory stores itself; a second generation would start from nothing.
            if (_started)
                throw new InvalidOperationException("In-memory stores do not survive a host restart; start one host generation only.");
            _started = true;
        }
    }

    private sealed class EntityFrameworkStore(string provider, string connectionString) : CompensationStore
    {
        public override string Name => provider;
        public override bool SurvivesRestart => true;

        public override void Configure(IServiceCollection services) => AddEntityFramework(services, provider, connectionString);
    }
}
