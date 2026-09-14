using Elsa.Persistence.EntityFramework;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Persistence.EntityFrameworkCore;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Entities;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.MySql.Tests;

[Collection(MySqlContainerCollection.Name)]
public sealed class MySqlEfSecretRepositoryTests(MySqlContainerFixture fixture)
{
    [Fact]
    public void Production_context_exposes_the_MySQL_provider_model()
    {
        using var context = CreateContext("Server=localhost;Database=unused;User ID=root;Password=root");
        Assert.Equal(SecretsMySqlDbContext.ExpectedProviderName, context.Database.ProviderName);
        var model = context.GetService<IDesignTimeModel>().Model;
        var entity = model.FindEntityType(typeof(SecretRecord))!;
        Assert.Equal(SecretsEfModule.TableName, entity.GetTableName());
        Assert.Equal(SecretsMySqlDbContext.CharacterSet, model.FindAnnotation("MySQL:Charset")?.Value);
        Assert.Equal(SecretsMySqlDbContext.Collation, model.GetCollation());
        Assert.Equal(SecretsMySqlDbContext.Collation,
            entity.FindProperty(nameof(SecretRecord.NormalizedName))!.FindAnnotation("MySQL:Collation")?.Value);
        Assert.Equal("json", entity.FindProperty(nameof(SecretRecord.Payload))!.GetColumnType());
        Assert.Equal("bigint", entity.FindProperty(nameof(SecretRecord.MaxActiveVersionExpiresAt))!.GetColumnType());
        Assert.Equal("varbinary(16)", entity.FindProperty(nameof(SecretRecord.ConcurrencyToken))!.GetColumnType());
    }

    [Fact]
    public void Feature_DI_selects_the_production_MySQL_context_and_repository()
    {
        var services = new ServiceCollection();
        new SecretsEntityFrameworkCoreFeature
        {
            Provider = "MySql",
            ConnectionString = "Server=localhost;Database=unused;User ID=root;Password=root"
        }.ConfigureServices(services);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        Assert.IsType<SecretsMySqlDbContext>(scope.ServiceProvider.GetRequiredService<SecretsDbContext>());
        Assert.IsType<EfSecretRepository>(scope.ServiceProvider.GetRequiredService<ISecretRepository>());
    }

    [SkippableFact]
    public async Task AutoMigrate_provisions_the_MySQL_schema_for_the_host_lifecycle()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");
        var connectionString = await fixture.CreateIsolatedDatabaseAsync();
        await using var provider = CreateProvider(connectionString, EfMigratePolicy.AutoMigrate);
        var lifecycle = provider.GetRequiredService<SecretsEfMigrationHostedService>();

        await lifecycle.StartAsync(CancellationToken.None);

        await using var scope = provider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<ISecretRepository>();
        Assert.True(await repository.TryAddAsync(Secret("tenant-a", "boot.provisioned", "ready")));
        Assert.Equal("ready", (await repository.FindAsync("tenant-a", "boot.provisioned"))!.LatestActiveVersion!.Payload.Value);
    }

    [SkippableFact]
    public async Task Validate_fails_when_the_MySQL_schema_has_not_been_provisioned()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");
        var connectionString = await fixture.CreateIsolatedDatabaseAsync();
        await using var provider = CreateProvider(connectionString, EfMigratePolicy.Validate);
        var lifecycle = provider.GetRequiredService<SecretsEfMigrationHostedService>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => lifecycle.StartAsync(CancellationToken.None));
        Assert.Contains("pending migrations", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task Validate_accepts_a_MySQL_schema_created_by_AutoMigrate()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");
        var connectionString = await fixture.CreateIsolatedDatabaseAsync();

        await using (var applyProvider = CreateProvider(connectionString, EfMigratePolicy.AutoMigrate))
        {
            var lifecycle = applyProvider.GetRequiredService<SecretsEfMigrationHostedService>();
            await lifecycle.InitializeAsync(CancellationToken.None);
        }

        await using var validateProvider = CreateProvider(connectionString, EfMigratePolicy.Validate);
        var validateLifecycle = validateProvider.GetRequiredService<SecretsEfMigrationHostedService>();
        await validateLifecycle.StartAsync(CancellationToken.None);
    }

    [SkippableFact]
    public async Task EnsureCreated_and_repository_CRUD_normalized_query_transaction_and_concurrency_work()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");
        var connectionString = await fixture.CreateIsolatedDatabaseAsync();
        await using var context = CreateContext(connectionString);
        Assert.True(await context.Database.EnsureCreatedAsync());

        var repository = new EfSecretRepository(context);
        var revisioned = Assert.IsAssignableFrom<IRevisionAwareSecretRepository>(repository);
        var original = Secret("tenant-a", "payments.api", "alpha", "Finance");
        Assert.True(await repository.TryAddAsync(original));
        Assert.False(await repository.TryAddAsync(Secret("tenant-a", "payments.api", "duplicate")));
        Assert.Equal("alpha", (await repository.FindAsync("tenant-a", "payments.api"))!.LatestActiveVersion!.Payload.Value);

        original.DisplayName = "Payments API";
        await repository.SaveAsync(original);
        var page = await repository.ListPageAsync("tenant-a", new SecretRepositoryListRequest(search: "payments", scope: "FINANCE"));
        Assert.Equal("payments.api", Assert.Single(page.Items).Name);
        Assert.Equal("Payments API", page.Items[0].DisplayName);

        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            Assert.True(await repository.TryAddAsync(Secret("tenant-a", "rolled-back", "transient")));
            await transaction.RollbackAsync();
        }
        Assert.Null(await repository.FindAsync("tenant-a", "rolled-back"));

        await using var first = CreateContext(connectionString);
        await using var second = CreateContext(connectionString);
        var firstRevision = await Assert.IsAssignableFrom<IRevisionAwareSecretRepository>(new EfSecretRepository(first))
            .FindWithRevisionAsync("tenant-a", "payments.api");
        var secondRevision = await Assert.IsAssignableFrom<IRevisionAwareSecretRepository>(new EfSecretRepository(second))
            .FindWithRevisionAsync("tenant-a", "payments.api");
        firstRevision!.Secret.DisplayName = "First writer";
        secondRevision!.Secret.DisplayName = "Stale writer";
        var firstResult = await Assert.IsAssignableFrom<IRevisionAwareSecretRepository>(new EfSecretRepository(first))
            .SaveWithRevisionAsync(firstRevision.Secret, firstRevision.Revision);
        var staleResult = await Assert.IsAssignableFrom<IRevisionAwareSecretRepository>(new EfSecretRepository(second))
            .SaveWithRevisionAsync(secondRevision.Secret, secondRevision.Revision);
        Assert.Equal(SecretRevisionSaveStatus.Saved, firstResult.Status);
        Assert.Equal(SecretRevisionSaveStatus.Conflict, staleResult.Status);
        Assert.Equal("First writer", (await repository.FindAsync("tenant-a", "payments.api"))!.DisplayName);
    }

    private static SecretsMySqlDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<SecretsMySqlDbContext>()
            .UseMySQL(connectionString)
            .Options);

    private static ServiceProvider CreateProvider(string connectionString, EfMigratePolicy migratePolicy)
    {
        var services = new ServiceCollection()
            .AddSecretsEntityFrameworkCore(new SecretsEntityFrameworkCoreOptions
            {
                Provider = "MySql",
                ConnectionString = connectionString,
                MigratePolicy = migratePolicy
            });

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static Secret Secret(string tenant, string name, string value, string? scope = null) => new()
    {
        TenantId = tenant,
        Name = name,
        DisplayName = name == "payments.api" ? "Payments API" : name,
        Scope = scope,
        TypeName = SecretTypeNames.Text,
        StoreName = SecretStoreNames.Encrypted,
        Status = SecretStatus.Active,
        CreatedAt = DateTimeOffset.UtcNow,
        Versions = [new SecretVersion { Version = 1, Payload = SecretPayload.FromValue(value) }]
    };
}
