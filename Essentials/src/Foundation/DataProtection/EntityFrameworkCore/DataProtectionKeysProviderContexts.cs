using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Foundation.DataProtection.EntityFrameworkCore;

public sealed class DataProtectionKeysSqliteDbContext(DbContextOptions<DataProtectionKeysSqliteDbContext> options)
    : DataProtectionKeysDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.Sqlite;
}

public sealed class DataProtectionKeysSqlServerDbContext(DbContextOptions<DataProtectionKeysSqlServerDbContext> options)
    : DataProtectionKeysDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.SqlServer;
}

public sealed class DataProtectionKeysPostgreSqlDbContext(DbContextOptions<DataProtectionKeysPostgreSqlDbContext> options)
    : DataProtectionKeysDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.PostgreSql;
}

public sealed class DataProtectionKeysMySqlDbContext(DbContextOptions<DataProtectionKeysMySqlDbContext> options)
    : DataProtectionKeysDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.MySql;
}
