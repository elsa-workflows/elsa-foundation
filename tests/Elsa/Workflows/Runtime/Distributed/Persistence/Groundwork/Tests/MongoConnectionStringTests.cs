using MongoDB.Driver;
using Xunit;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Tests;

public sealed class MongoConnectionStringTests
{
    [Fact]
    public void Testcontainer_credentials_and_admin_auth_source_are_preserved_for_the_named_database()
    {
        var connectionString = GroundworkV2DistributedProviderMatrixTests.NativeProviderRuntime.BuildMongoConnectionString(
            "mongodb://mongo:mongo@localhost:27017/?directConnection=true",
            "elsa");
        var url = new MongoUrl(connectionString);

        Assert.Equal("mongo", url.Username);
        Assert.Equal("mongo", url.Password);
        Assert.Equal("elsa", url.DatabaseName);
        Assert.Equal("admin", url.AuthenticationSource);
        Assert.Equal("rs0", url.ReplicaSetName);
        Assert.True(url.DirectConnection);
    }
}
