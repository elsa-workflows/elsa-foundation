using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Persistence.EntityFrameworkCore;
using Elsa.Agent.Persistence.EntityFrameworkCore.Services;
using Elsa.Agent.Persistence.EntityFrameworkCore.Stores;
using Elsa.Agent.Tests;
using Elsa.Persistence.EntityFramework;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Persistence.Tests;

/// <summary>
/// One file-backed SQLite database with the module's actual migrations applied, the real Secrets protector, and stores
/// built over it. Each instance is a fresh database; separate store instances over one database are separate processes.
/// </summary>
internal sealed class AgentSqliteDatabase : IDisposable
{
    private static readonly string Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "elsa-agent-ef-tests", Environment.ProcessId.ToString());
    private readonly ServiceProvider _contexts;

    public AgentSqliteDatabase(ISecretKeyRing? keys = null)
    {
        Directory.CreateDirectory(Root);
        Path = System.IO.Path.Combine(Root, $"agent-{Guid.NewGuid():N}.db");
        ConnectionString = $"Data Source={Path};Pooling=False";
        Keys = keys ?? new TestKeyRing();
        _contexts = Contexts(ConnectionString);
        using var scope = _contexts.CreateScope();
        scope.ServiceProvider.GetRequiredService<AgentDbContext>().Database.Migrate();
    }

    public string Path { get; }
    public string ConnectionString { get; }
    public ISecretKeyRing Keys { get; }
    public AgentRecordCodec Codec => new(new DefaultSecretValueProtector(Keys));

    /// <summary>A record store over this database. Without a clock, leases use the database's own time.</summary>
    public EfAgentSessionRecordStore RecordStore(TimeProvider? clock = null, AgentRecordCodec? codec = null, ServiceProvider? contexts = null)
        => new((contexts ?? _contexts).GetRequiredService<IServiceScopeFactory>(), static scoped => scoped.GetRequiredService<AgentDbContext>(),
            codec ?? Codec, TimeSpan.FromSeconds(30), clock is null ? null : new AgentTimeProviderClock(clock));

    public EfAgentAuditStore AuditStore(AgentRecordCodec? codec = null, ServiceProvider? contexts = null)
        => new((contexts ?? _contexts).GetRequiredService<IServiceScopeFactory>(), static scoped => scoped.GetRequiredService<AgentDbContext>(), codec ?? Codec);

    /// <summary>A fresh context for direct inspection or tampering, as an operator with database access would.</summary>
    public AgentDbContext Open() => _contexts.CreateScope().ServiceProvider.GetRequiredService<AgentDbContext>();

    public async Task<int> ExecuteAsync(string sql)
    {
        await using var db = Open();
        return await db.Database.ExecuteSqlRawAsync(sql);
    }

    /// <summary>Every text value of every row in every agent table, keyed by table and column.</summary>
    public async Task<IReadOnlyList<(string Table, string Column, string Value)>> ReadAllTextAsync()
    {
        var values = new List<(string, string, string)>();
        await using var db = Open();
        await db.Database.OpenConnectionAsync();
        var connection = db.Database.GetDbConnection();
        foreach (var table in new[] { "elsa_agent_sessions", "elsa_agent_messages", "elsa_agent_disclosure_snapshots", "elsa_agent_context",
                     "elsa_agent_proposals", "elsa_agent_call_keys", "elsa_agent_audit", "elsa_agent_audit_owners", "elsa_agent_audit_sequence" })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM {table}";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                for (var column = 0; column < reader.FieldCount; column++)
                    if (!reader.IsDBNull(column) && reader.GetValue(column) is string text)
                        values.Add((table, reader.GetName(column), text));
        }
        return values;
    }

    /// <summary>A second, independent context registration over the same database (another process's connection pool).</summary>
    public ServiceProvider NewContexts() => Contexts(ConnectionString);

    internal static ServiceProvider Contexts(string connectionString, Action<DbContextOptionsBuilder>? configure = null)
    {
        var binding = EfModuleBinding.For(typeof(AgentDbContext));
        var services = new ServiceCollection();
        services.AddDbContext<AgentSqliteDbContext>((provider, builder) =>
        {
            binding.Apply(builder, provider, "Sqlite", connectionString, null);
            configure?.Invoke(builder);
        });
        services.AddScoped<AgentDbContext>(provider => provider.GetRequiredService<AgentSqliteDbContext>());
        return services.BuildServiceProvider();
    }

    internal static void DeleteAll()
    {
        try { Directory.Delete(Root, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        _contexts.Dispose();
        try { File.Delete(Path); }
        catch (IOException) { }
    }
}

/// <summary>A key ring holding one random AES-256 key; a second ring cannot read what the first protected.</summary>
internal sealed class TestKeyRing : ISecretKeyRing
{
    private readonly SecretEncryptionKey _key = new("test-" + Guid.NewGuid().ToString("N")[..8], RandomNumberGenerator.GetBytes(32));

    public SecretEncryptionKey ActiveKey => _key;

    public bool TryGetKey(string keyId, out SecretEncryptionKey key)
    {
        key = _key;
        return keyId == _key.KeyId;
    }
}

/// <summary>Runs the Agent restart, deduplication, execution and ownership scenarios over the EF store on SQLite.</summary>
internal static class EfRestartComposition
{
#pragma warning disable CA2255 // A test assembly selects the store its linked scenarios run over, once, at load.
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void UseEfRecordStore()
    {
        // One fresh database per harness: each harness is one store shared by its hosts, as one database is. The
        // database and its context provider are disposed when the harness ends.
        AgentRestartHarness.DefaultStore = clock =>
        {
            var database = new AgentSqliteDatabase();
            var store = database.RecordStore(clock);
            Databases[store] = database;
            return store;
        };
        AgentRestartHarness.ReleaseDefaultStore = store =>
        {
            if (Databases.TryRemove(store, out var database)) database.Dispose();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            foreach (var store in Databases.Keys) if (Databases.TryRemove(store, out var database)) database.Dispose();
            AgentSqliteDatabase.DeleteAll();
        };
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<IAgentSessionRecordStore, AgentSqliteDatabase> Databases = new(ReferenceEqualityComparer.Instance);

    /// <summary>The database a harness's default store runs over, while that harness is alive.</summary>
    internal static AgentSqliteDatabase DatabaseOf(IAgentSessionRecordStore store) => Databases[store];
}
