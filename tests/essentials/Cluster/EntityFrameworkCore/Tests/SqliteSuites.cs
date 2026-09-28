using Elsa.Cluster.EntityFrameworkCore.Testing;
using Elsa.Cluster.Testing;

namespace Elsa.Cluster.EntityFrameworkCore.Tests;

/// <summary>The EF provider against the whole conformance kit, on a SQLite file of its own per test (spec 183, User Story 5).</summary>
public sealed class SqliteEfClusterMembershipConformanceTests()
    : ClusterMembershipConformanceTests(new EfClusterMembershipConformanceFixture(EfClusterMembershipTestStore.CreateSqlite()));

/// <summary>User stories 2 and 4 on SQLite.</summary>
public sealed class SqliteEfClusterMembershipScenarioTests() : EfClusterMembershipScenarioTests(EfClusterMembershipTestStore.CreateSqlite());
