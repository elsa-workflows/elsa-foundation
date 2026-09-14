using Elsa.Persistence.EntityFramework;
using Elsa.Secrets.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Tooling;

public sealed class SecretsMySqlDesignTimeFactory : IDesignTimeDbContextFactory<SecretsMySqlDbContext>
{
    public SecretsMySqlDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<SecretsMySqlDbContext>();
        builder.UseMySQL(
            SecretsDesignTimeConnection.Resolve(
                SecretsDesignTimeConnection.MySqlVariable,
                "Server=localhost;Port=3306;Database=elsa_secrets_design;User ID=root;******;AllowPublicKeyRetrieval=True;SslMode=Disabled"),
            mySql => mySql
                .MigrationsAssembly(typeof(SecretsMySqlDbContext).Assembly.GetName().Name)
                .MigrationsHistoryTable(EfMigrationsHistory.TableName(SecretsEfModule.HistoryModuleName)));
        return new SecretsMySqlDbContext(builder.Options);
    }
}
