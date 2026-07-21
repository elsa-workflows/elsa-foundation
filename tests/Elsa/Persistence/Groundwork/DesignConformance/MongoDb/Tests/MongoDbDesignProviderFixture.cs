using System.Diagnostics;
using DotNet.Testcontainers.Builders;
using MongoDB.Driver;
using Testcontainers.MongoDb;
using Xunit;

namespace Elsa.Persistence.Groundwork.DesignConformance.MongoDb.Tests;

/// <summary>
/// Starts a single throwaway transaction-capable MongoDB replica set for the whole design-conformance
/// collection and hands each fixture an isolated <c>elsa_design_{guid:N}</c> database name (MongoDB
/// isolates one shared container by database, not by server). The image is the T053-pinned
/// <c>mongo:7.0.24</c> started with <c>--replSet rs0</c> and the UnifiedHost replica-set initialization
/// callback. If Docker is unavailable the container fails to start and <see cref="IsAvailable"/> flips
/// to <c>false</c> with a <see cref="SkipReason"/> so integration callers can degrade instead of
/// throwing opaque connection failures.
/// </summary>
public sealed class MongoDbDesignProviderFixture : IAsyncLifetime
{
    /// <summary>The T053-pinned MongoDB image the design-conformance leaf runs against.</summary>
    public const string Image = "mongo:7.0.24";

    private const string ReplicaSetName = "rs0";

    private readonly MongoDbContainer _container = new MongoDbBuilder(Image)
        .WithReplicaSet(ReplicaSetName)
        .WithCommand("--setParameter", "enableTestCommands=1")
        .WithStartupCallback(EnsureReplicaSetInitializedAsync)
        .Build();

    public bool IsAvailable { get; private set; }

    public string? SkipReason { get; private set; }

    /// <summary>The replica-set connection string every design fixture shares; databases isolate them.</summary>
    public string ConnectionString => new MongoUrlBuilder(_container.GetConnectionString())
    {
        ReplicaSetName = ReplicaSetName
    }.ToString();

    /// <summary>
    /// Mints a fresh, uniquely-named <c>elsa_design_{guid:N}</c> database name so each design fixture
    /// materializes into an isolated MongoDB database on the shared container and cannot collide with
    /// its neighbours. Dropping the database on teardown is intentionally optional: the whole container
    /// is discarded when the collection completes.
    /// </summary>
    public string CreateDesignDatabaseName() => $"elsa_design_{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        try
        {
            await _container.StartAsync();
            IsAvailable = true;
        }
        catch (DockerUnavailableException exception)
        {
            IsAvailable = false;
            SkipReason = $"Docker/MongoDB container unavailable: {exception.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        if (IsAvailable)
            await _container.DisposeAsync();
    }

    private static async Task EnsureReplicaSetInitializedAsync(
        MongoDbContainer container,
        CancellationToken cancellationToken)
    {
        const string script = """
            const hello = db.adminCommand({hello: 1});
            if (hello.ok === 1 && hello.setName === "rs0") quit(0);
            const result = rs.initiate({_id: "rs0", members: [{_id: 0, host: "127.0.0.1:27017"}]});
            quit(result.ok === 1 || result.code === 23 ? 0 : 1);
            """;
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromMinutes(1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await container.ExecScriptAsync(script, cancellationToken);
                if (result.ExitCode == 0)
                    return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // The server is still accepting its first administrative command; retry without leaking output.
            }
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }

        throw new InvalidOperationException("MongoDB replica-set admission timed out; provider output was suppressed.");
    }
}

[CollectionDefinition(Name)]
public sealed class MongoDbDesignProviderCollection : ICollectionFixture<MongoDbDesignProviderFixture>
{
    public const string Name = "mongodb-design-provider";
}

/// <summary>
/// Starts a single standalone (non-replica-set) MongoDB container for the negative topology proof.
/// A standalone server cannot serve multi-document transactions, so Groundwork runtime admission must
/// reject it <em>before</em> any design document is written. Isolation is again by database name.
/// </summary>
public sealed class MongoDbStandaloneTopologyFixture : IAsyncLifetime
{
    /// <summary>The same pinned image as the replica-set fixture, started without <c>--replSet</c>.</summary>
    public const string Image = "mongo:7.0.24";

    private readonly MongoDbContainer _container = new MongoDbBuilder(Image).Build();

    public bool IsAvailable { get; private set; }

    public string? SkipReason { get; private set; }

    /// <summary>A standalone connection string with no replica-set name configured on its URI.</summary>
    public string ConnectionString => _container.GetConnectionString();

    /// <summary>Mints a fresh, uniquely-named database that admission must leave absent or empty.</summary>
    public string CreateDesignDatabaseName() => $"elsa_design_{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        try
        {
            await _container.StartAsync();
            IsAvailable = true;
        }
        catch (DockerUnavailableException exception)
        {
            IsAvailable = false;
            SkipReason = $"Docker/MongoDB container unavailable: {exception.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        if (IsAvailable)
            await _container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class MongoDbStandaloneTopologyCollection : ICollectionFixture<MongoDbStandaloneTopologyFixture>
{
    public const string Name = "mongodb-standalone-topology";
}
