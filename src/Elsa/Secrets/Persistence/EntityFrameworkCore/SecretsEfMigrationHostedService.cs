using CShells.Lifecycle;
using Elsa.Persistence.EntityFramework;
using Elsa.Secrets.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore;

/// <summary>
/// Applies or validates Secrets EF migrations on both lifecycle hooks CShells and plain hosts use.
/// Shell-scoped <see cref="IHostedService"/>s do not run; CShells activates and reloads through
/// <see cref="IShellInitializer"/>. The same instance is registered under both interfaces so a
/// feature enable or reload applies <see cref="EfMigratePolicy"/> without booting a second stack.
/// </summary>
public sealed class SecretsEfMigrationHostedService(
    IServiceScopeFactory scopes,
    SecretsEntityFrameworkCoreOptions options) : IHostedService, IShellInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        ApplyAsync(cancellationToken);

    public Task StartAsync(CancellationToken cancellationToken) => ApplyAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task ApplyAsync(CancellationToken cancellationToken)
    {
        // Same instance is resolved as both IHostedService and IShellInitializer. MigrateAsync is
        // idempotent, and a failed Validate must still fail if this instance is asked again.
        // Concurrent applies are serialized by EF's migration lock.
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SecretsDbContext>();
        var expected = EfRelationalProviderBinding.ExpectedProviderName(options.Provider);
        await ApplyDatabasePolicyAsync(context, expected, options.MigratePolicy, cancellationToken);
        await SecretsProjectionContract.EnsureCurrentAsync(context, cancellationToken);
    }

    private static Task ApplyDatabasePolicyAsync(
        SecretsDbContext context,
        string expectedProviderName,
        EfMigratePolicy policy,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(expectedProviderName, EfProviderNames.MySql, StringComparison.Ordinal))
            return EfDatabaseMigrator.ApplyAsync(context, expectedProviderName, policy, cancellationToken);

        EfProviderGuard.Ensure(context, expectedProviderName);

        return policy switch
        {
            EfMigratePolicy.AutoMigrate => context.Database.EnsureCreatedAsync(cancellationToken),
            EfMigratePolicy.Validate => EnsureMySqlSchemaExistsAsync(context, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(policy), policy, "Unknown EF migrate policy.")
        };
    }

    private static async Task EnsureMySqlSchemaExistsAsync(
        SecretsDbContext context,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        var openedHere = connection.State != System.Data.ConnectionState.Open;

        if (openedHere)
            await context.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                                  SELECT COUNT(*)
                                  FROM information_schema.tables
                                  WHERE table_schema = DATABASE() AND table_name = @tableName
                                  """;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@tableName";
            parameter.Value = SecretsEfModule.TableName;
            command.Parameters.Add(parameter);

            var count = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
            if (count == 0)
            {
                throw new InvalidOperationException(
                    "SecretsMySqlDbContext has no provisioned schema. Start once with AutoMigrate " +
                    "or create the schema out of process before using Validate.");
            }
        }
        finally
        {
            if (openedHere)
                await context.Database.CloseConnectionAsync();
        }
    }
}
