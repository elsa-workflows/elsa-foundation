using System.Text.Json;
using CShells.AspNetCore.Configuration;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Persistence.EntityFramework;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Features;
using Elsa.Secrets.Options;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Stores;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.PostgreSql.Tests;

[Collection(PostgresContainerCollection.Name)]
public sealed class PostgreSqlSecretsShellJourneyTests(PostgresContainerFixture fixture)
{
    private const string ShellName = "secrets-postgresql";
    private const string TenantId = "tenant-a";
    private const string SecretName = "payments.postgresql";
    private const string SecretValue = "postgresql-shell-value";

    [SkippableFact]
    public async Task Configuration_driven_shell_migrates_resolves_restarts_and_uses_only_npgsql()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");
        var connectionString = await fixture.CreateIsolatedDatabaseAsync();

        await using (var app = await StartAsync(connectionString, EfMigratePolicy.AutoMigrate))
        {
            var shell = await app.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
            await using (var scope = shell.ServiceProvider.CreateAsyncScope())
            {
                await AssertFirstStartAsync(scope.ServiceProvider);
            }
            await app.StopAsync();
        }

        await using (var app = await StartAsync(connectionString, EfMigratePolicy.Validate))
        {
            var shell = await app.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
            await using var scope = shell.ServiceProvider.CreateAsyncScope();
            await AssertRestartAsync(scope.ServiceProvider);
        }
    }

    private static async Task AssertFirstStartAsync(IServiceProvider services)
    {
        var repository = Assert.IsType<EfSecretRepository>(services.GetRequiredService<ISecretRepository>());
        var context = services.GetRequiredService<SecretsDbContext>();
        Assert.IsType<SecretsPostgreSqlDbContext>(context);
        Assert.Equal(EfProviderNames.PostgreSql, context.Database.ProviderName);
        Assert.Same(typeof(SecretsDbContext).Assembly, context.GetService<IMigrationsAssembly>().Assembly);
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());

        var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetName().Name)
            .Where(name => name is not null)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("Npgsql.EntityFrameworkCore.PostgreSQL", loadedAssemblies);
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore.Sqlite", loadedAssemblies);
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore.SqlServer", loadedAssemblies);

        var manager = services.GetRequiredService<ISecretManager>();
        await manager.CreateAsync(TenantId, new CreateSecretRequest
        {
            Name = SecretName,
            DisplayName = "PostgreSQL shell secret",
            TypeName = SecretTypeNames.Text,
            StoreName = SecretStoreNames.Encrypted,
            Value = SecretValue
        });

        var resolved = await services.GetRequiredService<ISecretValueResolver>()
            .ResolveAsync(TenantId, new SecretReference(SecretName));
        Assert.True(resolved.Succeeded);
        Assert.Equal(SecretValue, resolved.Value);

        var revisions = Assert.IsAssignableFrom<IRevisionAwareSecretRepository>(repository);
        var current = await revisions.FindWithRevisionAsync(TenantId, SecretName);
        Assert.NotNull(current);
        current.Secret.Description = "OCC winner";
        Assert.Equal(
            SecretRevisionSaveStatus.Saved,
            (await revisions.SaveWithRevisionAsync(current.Secret, current.Revision)).Status);
        current.Secret.Description = "stale overwrite";
        Assert.Equal(
            SecretRevisionSaveStatus.Conflict,
            (await revisions.SaveWithRevisionAsync(current.Secret, current.Revision)).Status);

        var identity = LongNonAsciiIdentity();
        await repository.SaveAsync(Secret(identity));
        var page = await repository.ListPageAsync(
            TenantId,
            new SecretRepositoryListRequest(
                typeName: identity.TypeName.ToLowerInvariant(),
                storeName: identity.StoreName.ToLowerInvariant(),
                scope: identity.Scope.ToLowerInvariant()));
        Assert.Equal("identity.postgresql", Assert.Single(page.Items).Name);
    }

    private static async Task AssertRestartAsync(IServiceProvider services)
    {
        var context = services.GetRequiredService<SecretsDbContext>();
        Assert.Equal(EfProviderNames.PostgreSql, context.Database.ProviderName);
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());

        var persisted = await services.GetRequiredService<ISecretRepository>().FindAsync(TenantId, SecretName);
        Assert.NotNull(persisted);
        Assert.Equal("OCC winner", persisted.Description);

        var resolved = await services.GetRequiredService<ISecretValueResolver>()
            .ResolveAsync(TenantId, new SecretReference(SecretName));
        Assert.True(resolved.Succeeded);
        Assert.Equal(SecretValue, resolved.Value);

        var identity = LongNonAsciiIdentity();
        var page = await services.GetRequiredService<ISecretRepository>().ListPageAsync(
            TenantId,
            new SecretRepositoryListRequest(
                typeName: identity.TypeName.ToLowerInvariant(),
                storeName: identity.StoreName.ToLowerInvariant(),
                scope: identity.Scope.ToLowerInvariant()));
        Assert.Equal("identity.postgresql", Assert.Single(page.Items).Name);
    }

    private static async Task<WebApplication> StartAsync(string connectionString, EfMigratePolicy migratePolicy)
    {
        var configurationPath = Path.Join(Path.GetTempPath(), $"elsa-secrets-postgresql-shell-{Guid.NewGuid():N}.json");
        var configuration = new
        {
            CShells = new
            {
                Shells = new Dictionary<string, object>
                {
                    [ShellName] = new
                    {
                        Name = ShellName,
                        Features = new Dictionary<string, object>
                        {
                            ["Secrets"] = new { },
                            ["PostgreSqlSecretsJourneyEncryption"] = new { },
                            ["SecretsEntityFrameworkCore"] = new
                            {
                                Provider = "PostgreSql",
                                ConnectionString = connectionString,
                                MigratePolicy = migratePolicy.ToString()
                            }
                        }
                    }
                }
            }
        };
        await File.WriteAllTextAsync(configurationPath, JsonSerializer.Serialize(configuration));

        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Configuration.AddJsonFile(configurationPath, optional: false, reloadOnChange: false);
            builder.Services.AddCShellsAspNetCore(shells => shells
                .WithHostAssemblies()
                .WithAssemblies(
                    typeof(SecretsFeature).Assembly,
                    typeof(SecretsEntityFrameworkCoreFeature).Assembly,
                    typeof(PostgreSqlSecretsJourneyEncryptionFeature).Assembly)
                .WithConfigurationProvider(builder.Configuration));

            var app = builder.Build();
            app.MapShells();
            await app.StartAsync();
            return app;
        }
        finally
        {
            File.Delete(configurationPath);
        }
    }

    private static (string TypeName, string StoreName, string Scope) LongNonAsciiIdentity() =>
        ($"Typé-{new string('Ä', 96)}", $"Storé-{new string('Ö', 96)}", $"Scopé-{new string('Ü', 96)}");

    private static Secret Secret((string TypeName, string StoreName, string Scope) identity) => new()
    {
        TenantId = TenantId,
        Name = "identity.postgresql",
        DisplayName = "München PostgreSQL identity",
        TypeName = identity.TypeName,
        StoreName = identity.StoreName,
        Scope = identity.Scope,
        Versions =
        [
            new SecretVersion
            {
                Version = 1,
                Status = SecretStatus.Active,
                Payload = SecretPayload.FromValue("identity-value")
            }
        ]
    };
}

[ShellFeature(name: "PostgreSqlSecretsJourneyEncryption")]
public sealed class PostgreSqlSecretsJourneyEncryptionFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services) =>
        services.PostConfigure<SecretsOptions>(options => options.EncryptionKey = "phase-3-postgresql-shell-key");
}
