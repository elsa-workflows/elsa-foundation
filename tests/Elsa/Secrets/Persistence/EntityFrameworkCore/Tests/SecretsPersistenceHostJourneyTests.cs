using CShells;
using CShells.AspNetCore.Configuration;
using CShells.DependencyInjection;
using CShells.Lifecycle;
using Elsa.Secrets.Core.Contracts;
using Groundwork.Store;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Stores;
using Elsa.Secrets.Persistence.Groundwork.Stores;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Phase 3 selected-backend journeys: configuration-driven catalog, management create/update,
/// runtime resolve, host restart, persist. Does not boot <c>Elsa.Workbench</c> Program (full
/// product composition / OpenIddict). Does not flip committed Workbench defaults.
/// </summary>
public sealed class SecretsPersistenceHostJourneyTests
{
    private const string EntityFrameworkBackend = "entity-framework";
    private const string GroundworkBackend = "groundwork";
    private const string SecretName = "payments.journey";
    private const string SecretValue = "phase-3-secret";
    private const string IdentitySecretName = "identity.projection";

    [Fact]
    public void Host_catalog_discovers_workbench_shaped_secrets_features()
    {
        var names = SecretsHostCatalog.Assemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsAbstract: false, IsInterface: false } && typeof(CShells.Features.IShellFeature).IsAssignableFrom(type))
            .Select(type => type.GetCustomAttributes(typeof(CShells.Features.ShellFeatureAttribute), inherit: false)
                .OfType<CShells.Features.ShellFeatureAttribute>()
                .FirstOrDefault()?.Name)
            .Where(name => name is not null)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("Secrets", names);
        Assert.Contains("SecretsEntityFrameworkCore", names);
        Assert.Contains("SecretsGroundworkPersistence", names);
        Assert.Contains("GroundworkProviderSqlite", names);
    }

    [Fact]
    public async Task Configuration_entity_framework_create_resolve_restart_persists()
    {
        var path = NewDbPath("ef");
        try
        {
            await RunJourneyAsync(
                firstStart: EntityFrameworkShells(path, "AutoMigrate"),
                restart: EntityFrameworkShells(path, "Validate"),
                expectedRepository: typeof(EfSecretRepository),
                expectedBackend: EntityFrameworkBackend);
        }
        finally
        {
            DeleteSqliteFiles(path);
        }
    }

    [Fact]
    public async Task Configuration_groundwork_create_resolve_restart_persists()
    {
        var path = NewDbPath("gw");
        try
        {
            var shells = GroundworkShells(path);
            await RunJourneyAsync(
                firstStart: shells,
                restart: shells,
                expectedRepository: typeof(GroundworkSecretRepository),
                expectedBackend: GroundworkBackend);
        }
        finally
        {
            DeleteSqliteFiles(path);
        }
    }

    private static async Task RunJourneyAsync(
        string firstStart,
        string restart,
        Type expectedRepository,
        string expectedBackend)
    {
        await using (var host = await SecretsHostCatalog.StartAsync(firstStart))
        {
            var shell = await host.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(SecretsHostCatalog.ShellName);
            await using (var scope = shell.ServiceProvider.CreateAsyncScope())
            {
                AssertSelectedBackend(scope.ServiceProvider, expectedRepository, expectedBackend);

                var manager = scope.ServiceProvider.GetRequiredService<ISecretManager>();
                var created = await manager.CreateAsync(
                    SecretsHostCatalog.TenantId,
                    new CreateSecretRequest
                    {
                        Name = SecretName,
                        DisplayName = "Journey secret",
                        TypeName = SecretTypeNames.Text,
                        StoreName = SecretStoreNames.Encrypted,
                        Value = SecretValue
                    });
                Assert.Equal(SecretName, created.Name);
                Assert.Null(created.Description);

                var resolved = await scope.ServiceProvider
                    .GetRequiredService<ISecretValueResolver>()
                    .ResolveAsync(SecretsHostCatalog.TenantId, new SecretReference(SecretName, SecretTypeNames.Text));
                Assert.True(resolved.Succeeded);
                Assert.Equal(SecretValue, resolved.Value);

                var updated = await manager.UpdateAsync(
                    SecretsHostCatalog.TenantId,
                    SecretName,
                    new UpdateSecretMetadataRequest { DisplayName = "Journey secret updated" });
                Assert.Equal("Journey secret updated", updated.DisplayName);

                await AssertRevisionConflictAsync(scope.ServiceProvider);
                await SaveAndQueryLongNonAsciiIdentityAsync(scope.ServiceProvider);
            }

            // Groundwork SQLite holds a process schema lock per file. Release the shell connection
            // before the restarted host opens the same store.
            if (shell.ServiceProvider.GetService<IStorageProviderConnection>() is IAsyncDisposable asyncConnection)
                await asyncConnection.DisposeAsync();
            else
                (shell.ServiceProvider.GetService<IStorageProviderConnection>() as IDisposable)?.Dispose();
            await host.StopAsync();
        }

        await using (var host = await SecretsHostCatalog.StartAsync(restart))
        {
            var shell = await host.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(SecretsHostCatalog.ShellName);
            await using var scope = shell.ServiceProvider.CreateAsyncScope();
            AssertSelectedBackend(scope.ServiceProvider, expectedRepository, expectedBackend);

            var found = await scope.ServiceProvider
                .GetRequiredService<ISecretManager>()
                .FindAsync(SecretsHostCatalog.TenantId, SecretName);
            Assert.NotNull(found);
            Assert.Equal("Journey secret updated", found.DisplayName);

            var resolved = await scope.ServiceProvider
                .GetRequiredService<ISecretValueResolver>()
                .ResolveAsync(SecretsHostCatalog.TenantId, new SecretReference(SecretName));
            Assert.True(resolved.Succeeded);
            Assert.Equal(SecretValue, resolved.Value);

            await AssertLongNonAsciiIdentityPersistedAsync(scope.ServiceProvider);
        }
    }

    private static async Task AssertRevisionConflictAsync(IServiceProvider services)
    {
        var repository = services.GetRequiredService<ISecretRepository>();
        var revisions = Assert.IsAssignableFrom<IRevisionAwareSecretRepository>(repository);
        var current = await revisions.FindWithRevisionAsync(SecretsHostCatalog.TenantId, SecretName);
        Assert.NotNull(current);

        current.Secret.Description = "OCC winner";
        var saved = await revisions.SaveWithRevisionAsync(current.Secret, current.Revision);
        Assert.Equal(SecretRevisionSaveStatus.Saved, saved.Status);

        current.Secret.Description = "stale overwrite";
        var stale = await revisions.SaveWithRevisionAsync(current.Secret, current.Revision);
        Assert.Equal(SecretRevisionSaveStatus.Conflict, stale.Status);

        var persisted = await repository.FindAsync(SecretsHostCatalog.TenantId, SecretName);
        Assert.NotNull(persisted);
        Assert.Equal("OCC winner", persisted.Description);
    }

    private static async Task SaveAndQueryLongNonAsciiIdentityAsync(IServiceProvider services)
    {
        var repository = services.GetRequiredService<ISecretRepository>();
        var identity = LongNonAsciiIdentity();
        await repository.SaveAsync(new Secret
        {
            TenantId = SecretsHostCatalog.TenantId,
            Name = IdentitySecretName,
            DisplayName = "München identity projection",
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
        });

        var page = await repository.ListPageAsync(
            SecretsHostCatalog.TenantId,
            new SecretRepositoryListRequest(
                typeName: identity.TypeName.ToLowerInvariant(),
                storeName: identity.StoreName.ToLowerInvariant(),
                scope: identity.Scope.ToLowerInvariant()));
        Assert.Equal(IdentitySecretName, Assert.Single(page.Items).Name);
    }

    private static async Task AssertLongNonAsciiIdentityPersistedAsync(IServiceProvider services)
    {
        var identity = LongNonAsciiIdentity();
        var page = await services.GetRequiredService<ISecretRepository>().ListPageAsync(
            SecretsHostCatalog.TenantId,
            new SecretRepositoryListRequest(
                typeName: identity.TypeName.ToLowerInvariant(),
                storeName: identity.StoreName.ToLowerInvariant(),
                scope: identity.Scope.ToLowerInvariant()));
        var secret = Assert.Single(page.Items);
        Assert.Equal(IdentitySecretName, secret.Name);
        Assert.Equal(identity.TypeName, secret.TypeName);
        Assert.Equal(identity.StoreName, secret.StoreName);
        Assert.Equal(identity.Scope, secret.Scope);
    }

    private static (string TypeName, string StoreName, string Scope) LongNonAsciiIdentity() =>
        ($"Typé-{new string('Ä', 96)}", $"Storé-{new string('Ö', 96)}", $"Scopé-{new string('Ü', 96)}");

    private static void AssertSelectedBackend(IServiceProvider services, Type repositoryType, string backend)
    {
        Assert.IsType(repositoryType, services.GetRequiredService<ISecretRepository>());
        Assert.Equal(backend, services.GetRequiredService<SecretRepositoryBackend>().Name);
    }

    private static string EntityFrameworkShells(string path, string migratePolicy) =>
        $$"""
        {
          "CShells": {
            "Shells": {
              "secrets-persistence": {
                "Name": "secrets-persistence",
                "Features": {
                  "Secrets": {},
                  "SecretsJourneyEncryption": {
                    "EncryptionKey": "{{SecretsHostCatalog.EncryptionKey}}"
                  },
                  "SecretsEntityFrameworkCore": {
                    "Provider": "Sqlite",
                    "ConnectionString": "Data Source={{path.Replace("\\", "/")}};Cache=Shared;Pooling=False",
                    "MigratePolicy": "{{migratePolicy}}"
                  }
                }
              }
            }
          }
        }
        """;

    private static string GroundworkShells(string path) =>
        $$"""
        {
          "CShells": {
            "Shells": {
              "secrets-persistence": {
                "Name": "secrets-persistence",
                "Features": {
                  "Secrets": {},
                  "SecretsJourneyEncryption": {
                    "EncryptionKey": "{{SecretsHostCatalog.EncryptionKey}}"
                  },
                  "GroundworkProviderSqlite": {
                    "ConnectionString": "Data Source={{path.Replace("\\", "/")}}"
                  },
                  "SecretsGroundworkPersistence": {}
                }
              }
            }
          }
        }
        """;

    private static string NewDbPath(string suffix) =>
        Path.Join(Path.GetTempPath(), $"elsa-secrets-journey-{suffix}-{Guid.NewGuid():N}.db");

    private static void DeleteSqliteFiles(string path)
    {
        File.Delete(path);
        File.Delete($"{path}-wal");
        File.Delete($"{path}-shm");
    }
}
