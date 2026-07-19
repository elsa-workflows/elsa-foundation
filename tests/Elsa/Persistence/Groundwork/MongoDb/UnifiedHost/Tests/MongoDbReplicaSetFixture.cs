using System.Diagnostics;
using DotNet.Testcontainers.Builders;
using MongoDB.Driver;
using Testcontainers.MongoDb;
using Xunit;

namespace Elsa.Persistence.Groundwork.MongoDb.UnifiedHost.Tests;

/// <summary>Runs the unified-host contract on a transaction-capable MongoDB replica set.</summary>
public sealed class MongoDbReplicaSetFixture : IAsyncLifetime
{
    private const string Image = "mongo:7.0.24";
    private const string ReplicaSetName = "rs0";
    private readonly MongoDbContainer _container = new MongoDbBuilder(Image)
        .WithReplicaSet(ReplicaSetName)
        .WithCommand("--setParameter", "enableTestCommands=1")
        .WithStartupCallback(EnsureReplicaSetInitializedAsync)
        .Build();

    public bool IsAvailable { get; private set; }

    public string? SkipReason { get; private set; }

    public string ConnectionString => new MongoUrlBuilder(_container.GetConnectionString())
    {
        ReplicaSetName = ReplicaSetName
    }.ToString();

    /// <summary>Returns a database name used by one test's explicit schema application and host admission.</summary>
    public string CreateIsolatedDatabaseName() => $"elsa_{Guid.NewGuid():N}";

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
public sealed class MongoDbContainerCollection : ICollectionFixture<MongoDbReplicaSetFixture>
{
    public const string Name = "mongodb-container";
}
