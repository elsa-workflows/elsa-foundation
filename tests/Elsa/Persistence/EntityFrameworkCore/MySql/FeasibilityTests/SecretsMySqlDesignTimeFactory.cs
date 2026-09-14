using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Elsa.Persistence.EntityFrameworkCore.MySql.FeasibilityTests;

public sealed class SecretsMySqlDesignTimeFactory : IDesignTimeDbContextFactory<SecretsMySqlDbContext>
{
    public SecretsMySqlDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ELSA_EF_MYSQL_TEST_CONNECTION_STRING")
            ?? "Server=localhost;Port=3306;Database=elsa;User ID=root;Password=root;AllowPublicKeyRetrieval=True;SslMode=Disabled";
        return MySqlTestContext.Create(connectionString);
    }
}
