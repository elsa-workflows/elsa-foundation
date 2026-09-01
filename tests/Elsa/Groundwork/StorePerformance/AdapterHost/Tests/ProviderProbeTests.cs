using Microsoft.Data.Sqlite;
using MongoDB.Bson;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.AdapterHost.Tests;

public sealed class ProviderProbeTests
{
    [Theory]
    [InlineData(":memory:")]
    [InlineData("file::memory:?cache=shared")]
    [InlineData("file:memdb1?mode=memory&cache=shared")]
    [InlineData("file:memdb1?cache=shared&mode=memory")]
    [InlineData("file:memdb1?mode=MEMORY;cache=shared")]
    public void Every_sqlite_memory_uri_form_is_not_admissible_for_checkpoint_evidence(string dataSource)
    {
        var settings = new SqliteConnectionStringBuilder($"Data Source={dataSource}");

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

        var replica = new BsonDocument
        {
            ["setName"] = "rs0",
            ["logicalSessionTimeoutMinutes"] = 30,
            ["isWritablePrimary"] = true,
            ["maxWireVersion"] = 21
        };
        Assert.Equal(
            "transaction-capable-replica-set",
            ProviderProbe.MongoTopology(replica));

        var sharded = new BsonDocument
        {
            ["msg"] = "isdbgrid",
            ["logicalSessionTimeoutMinutes"] = 30,
            ["isWritablePrimary"] = true,
            ["maxWireVersion"] = 21
        };
        Assert.Equal(
            "transaction-capable-sharded-cluster",
            ProviderProbe.MongoTopology(sharded));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("sha256:not-a-digest")]
    [InlineData("sha1:0000000000000000000000000000000000000000")]
    public void Server_provider_attestation_must_be_a_launcher_bound_sha256_digest(string? value)
    {
        Assert.Throws<PerformanceContractException>(() =>
            ProviderProbe.ValidateContainerAttestation("postgresql", value));

        Assert.Throws<PerformanceContractException>(() =>
            ProviderProbe.ValidateContainerAttestation("sqlserver", value));
    }

    [Fact]
    public void Server_provider_attestation_is_normalized_without_exposing_connection_material()
    {
        var value = ProviderProbe.ValidateContainerAttestation(
            "postgresql", "SHA256:" + new string('A', 64));

        Assert.Equal("sha256:" + new string('a', 64), value);
    }

    [Fact]
    public void Connection_options_digest_keeps_material_configuration_differences_distinct()
    {
        var one = ProviderProbe.ConnectionOptionsDigest("Max Pool Size=10;Command Timeout=30");
        var two = ProviderProbe.ConnectionOptionsDigest("Max Pool Size=20;Command Timeout=30");

        Assert.NotEqual(one, two);
        Assert.Matches("^[0-9a-f]{64}$", one);
        Assert.Matches("^[0-9a-f]{64}$", two);
    }
}
