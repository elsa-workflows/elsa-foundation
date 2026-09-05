using MongoDB.Driver;
using Xunit;

namespace Elsa.Persistence.Groundwork.DesignConformance.MongoDb.Tests;

public sealed class MongoDbDesignProviderFixtureTests
{
    [Fact]
    public void Replica_set_connection_strings_authenticate_the_testcontainer_root_user_against_admin()
    {
        var connectionString = MongoDbDesignProviderFixture.BuildConnectionString(
            "mongodb://mongo:mongo@localhost:27017/?directConnection=true");
        var url = new MongoUrl(connectionString);

        Assert.Equal("mongo", url.Username);
        Assert.Equal("mongo", url.Password);
        Assert.Equal("admin", url.AuthenticationSource);
        Assert.Equal("rs0", url.ReplicaSetName);
    }
}
