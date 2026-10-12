using Elsa.Foundation.DataProtection.EntityFrameworkCore.Tests;

namespace Elsa.Foundation.DataProtection.EntityFrameworkCore.ProviderTests;

// The shared key ring suite on each container engine (#2191).

public sealed class PostgreSqlSharedKeyRingTests(PostgreSqlKeyRingServer server) : SharedKeyRingTests(server), IClassFixture<PostgreSqlKeyRingServer>;

public sealed class SqlServerSharedKeyRingTests(SqlServerKeyRingServer server) : SharedKeyRingTests(server), IClassFixture<SqlServerKeyRingServer>;

public sealed class MySqlSharedKeyRingTests(MySqlKeyRingServer server) : SharedKeyRingTests(server), IClassFixture<MySqlKeyRingServer>;
