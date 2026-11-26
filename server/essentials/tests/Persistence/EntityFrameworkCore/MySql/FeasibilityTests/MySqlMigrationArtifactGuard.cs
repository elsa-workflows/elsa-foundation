using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Elsa.Persistence.EntityFrameworkCore.MySql.FeasibilityTests;

internal static class MySqlMigrationArtifactGuard
{
    public static void Ensure(SecretsMySqlDbContext context)
    {
        var migrationsAssembly = context.GetService<IMigrationsAssembly>().Assembly;
        if (!ReferenceEquals(migrationsAssembly, typeof(SecretsMySqlDbContext).Assembly))
        {
            throw new InvalidOperationException(
                "The MySQL feasibility migration artifact must be the test assembly before domain work starts.");
        }

        if (!string.Equals(context.Database.ProviderName, SecretsMySqlDbContext.ExpectedProviderName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The MySQL feasibility context is bound to '{context.Database.ProviderName}', not '{SecretsMySqlDbContext.ExpectedProviderName}'.");
        }
    }
}
