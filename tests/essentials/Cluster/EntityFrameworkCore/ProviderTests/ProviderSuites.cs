using Elsa.Cluster.EntityFrameworkCore.Testing;
using Elsa.Cluster.Testing;
using Xunit;

namespace Elsa.Cluster.EntityFrameworkCore.ProviderTests;

// The conformance kit and user stories 2 and 4 on each container engine (spec 183, User Story 5, scenario 2; FR-033).

[Collection(PostgreSqlMembershipDatabase.Collection)]
public sealed class PostgreSqlEfClusterMembershipConformanceTests(PostgreSqlMembershipDatabase database)
    : ClusterMembershipConformanceTests(new EfClusterMembershipConformanceFixture(database.Store));

[Collection(PostgreSqlMembershipDatabase.Collection)]
public sealed class PostgreSqlEfClusterMembershipScenarioTests(PostgreSqlMembershipDatabase database) : EfClusterMembershipScenarioTests(database.Store);

[Collection(SqlServerMembershipDatabase.Collection)]
public sealed class SqlServerEfClusterMembershipConformanceTests(SqlServerMembershipDatabase database)
    : ClusterMembershipConformanceTests(new EfClusterMembershipConformanceFixture(database.Store));

[Collection(SqlServerMembershipDatabase.Collection)]
public sealed class SqlServerEfClusterMembershipScenarioTests(SqlServerMembershipDatabase database) : EfClusterMembershipScenarioTests(database.Store);

[Collection(MySqlMembershipDatabase.Collection)]
public sealed class MySqlEfClusterMembershipConformanceTests(MySqlMembershipDatabase database)
    : ClusterMembershipConformanceTests(new EfClusterMembershipConformanceFixture(database.Store));

[Collection(MySqlMembershipDatabase.Collection)]
public sealed class MySqlEfClusterMembershipScenarioTests(MySqlMembershipDatabase database) : EfClusterMembershipScenarioTests(database.Store);
