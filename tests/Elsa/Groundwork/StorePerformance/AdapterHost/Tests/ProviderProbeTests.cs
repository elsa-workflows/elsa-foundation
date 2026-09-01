using Microsoft.Data.Sqlite;
using MongoDB.Bson;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.AdapterHost.Tests;

public sealed class ProviderProbeTests
{
    [Fact]
    public void Sqlite_memory_connections_are_not_admissible_for_checkpoint_evidence()
    {
        var settings = new SqliteConnectionStringBuilder("Data Source=:memory:;Cache=Shared");

        Assert.Throws<PerformanceContractException>(() => ProviderProbe.SqliteTopology(settings));
    }

    [Fact]
    public void Sqlite_file_connections_use_the_frozen_distinct_connection_topology()
    {
        var settings = new SqliteConnectionStringBuilder("Data Source=checkpoint.db;Cache=Shared");

        Assert.Equal("file-backed-distinct-connections", ProviderProbe.SqliteTopology(settings));
    }

    [Fact]
    public void Mongo_hello_requires_a_replica_set_or_sharded_cluster()
    {
        Assert.Throws<PerformanceContractException>(() =>
            ProviderProbe.MongoTopology(new BsonDocument("isWritablePrimary", true)));

        Assert.Equal(
            "transaction-capable-replica-set",
            ProviderProbe.MongoTopology(new BsonDocument("setName", "rs0")));
        Assert.Equal(
            "transaction-capable-sharded-cluster",
            ProviderProbe.MongoTopology(new BsonDocument("msg", "isdbgrid")));
    }
}
