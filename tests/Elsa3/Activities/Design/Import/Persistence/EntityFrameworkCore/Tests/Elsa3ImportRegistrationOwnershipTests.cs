using Elsa.Activities.Design.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa3.Activities.Design.Import.Composition;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Stores;
using Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Tests.Support;
using Elsa3.Activities.Design.Import.Persistence.Groundwork;
using Elsa3.Activities.Design.Import.Persistence.Groundwork.DependencyInjection;
using Elsa3.Activities.Design.Import.Persistence.Groundwork.Services;
using Elsa3.Activities.Design.Import.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Tests.Support.ImportFixtures;

namespace Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Tests;

public sealed class Elsa3ImportRegistrationOwnershipTests
{
    private static readonly Elsa3ImportEntityFrameworkCoreOptions SqliteOptions = new() { Provider = "Sqlite", ConnectionString = "Data Source=import.db" };
    private readonly ServiceCollection services = new();

    [Fact]
    public void Ef_repeat_with_the_same_options_is_a_no_op()
    {
        services.AddElsa3ImportEntityFrameworkCore(SqliteOptions);
        var before = services.ToArray();
        var backend = Elsa3ImportPersistenceBackend.Find(services);

        services.AddElsa3ImportEntityFrameworkCore(new() { Provider = "sqlite", ConnectionString = "Data Source=import.db" });

        Assert.Same(backend, Elsa3ImportPersistenceBackend.Find(services));
        AssertUnchanged(before);
    }

    [Fact]
    public void Ef_conflicting_options_fail_before_any_mutation()
    {
        services.AddElsa3ImportEntityFrameworkCore(SqliteOptions);
        var before = services.ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddElsa3ImportEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = "Data Source=other.db" }));

        AssertUnchanged(before);
    }

    [Fact]
    public void Groundwork_repeat_is_a_no_op_and_declares_each_unit_once()
    {
        services.AddElsa3ImportGroundworkPersistence();
        var before = services.ToArray();
        var registrations = Registry().Registrations.ToArray();

        services.AddElsa3ImportGroundworkPersistence();

        AssertUnchanged(before);
        Assert.Equal(registrations, Registry().Registrations);
        Assert.Equal(3, registrations.Length);
    }

    [Fact]
    public void Groundwork_to_ef_switch_withdraws_the_groundwork_units_and_selects_the_ef_adapter()
    {
        services.AddElsa3ImportGroundworkPersistence();

        services.AddElsa3ImportEntityFrameworkCore(SqliteOptions);

        Assert.Equal(Elsa3ImportPersistenceBackend.EntityFramework, Elsa3ImportPersistenceBackend.Find(services)!.Name);
        Assert.Empty(Registry().Registrations);
        Assert.Equal(typeof(EfReusableActivityImportCommand), Assert.Single(services, x => x.ServiceType == typeof(IReusableActivityImportCommand)).ImplementationType);
        Assert.Equal(typeof(EfReusableActivityImportOperationStore), Assert.Single(services, x => x.ServiceType == typeof(IReusableActivityImportOperationStore)).ImplementationType);
        Assert.Single(services, x => x.ServiceType == typeof(Elsa3ImportDbContext));
    }

    [Fact]
    public void Ef_to_groundwork_switch_removes_every_ef_artifact_and_declares_the_units()
    {
        services.AddElsa3ImportEntityFrameworkCore(SqliteOptions);

        services.AddElsa3ImportGroundworkPersistence();

        Assert.Equal(Elsa3ImportPersistenceBackend.Groundwork, Elsa3ImportPersistenceBackend.Find(services)!.Name);
        Assert.DoesNotContain(services, x => x.ServiceType == typeof(Elsa3ImportEntityFrameworkCoreOptions));
        Assert.DoesNotContain(services, x => typeof(Elsa3ImportDbContext).IsAssignableFrom(x.ServiceType));
        Assert.DoesNotContain(services, x => x.ServiceType == typeof(DbContextOptions<Elsa3ImportSqliteDbContext>));
        Assert.Equal(typeof(GroundworkReusableActivityImportCommand), Assert.Single(services, x => x.ServiceType == typeof(IReusableActivityImportCommand)).ImplementationType);
        Assert.Equal(Elsa3ImportStorageManifest.CreateUnits().Count, Registry().Registrations.Count);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Custom_import_command_or_store_fails_closed_for_both_backends_without_mutation(bool customCommand, bool entityFramework)
    {
        if (customCommand)
            services.AddScoped<IReusableActivityImportCommand, CustomCommand>();
        else
            services.AddScoped<IReusableActivityImportOperationStore, CustomStore>();
        var before = services.ToArray();

        Assert.Throws<InvalidOperationException>(() =>
        {
            if (entityFramework)
                services.AddElsa3ImportEntityFrameworkCore(SqliteOptions);
            else
                services.AddElsa3ImportGroundworkPersistence();
        });

        AssertUnchanged(before);
        Assert.Null(Elsa3ImportPersistenceBackend.Find(services));
    }

    [Fact]
    public void Custom_registration_added_after_a_backend_fails_the_next_switch_without_mutation()
    {
        services.AddElsa3ImportGroundworkPersistence();
        services.AddScoped<IReusableActivityImportCommand, CustomCommand>();
        var before = services.ToArray();
        var registrations = Registry().Registrations.ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddElsa3ImportEntityFrameworkCore(SqliteOptions));

        AssertUnchanged(before);
        Assert.Equal(registrations, Registry().Registrations);
    }

    [Fact]
    public void Failed_groundwork_to_ef_switch_restores_the_withdrawn_units_and_the_groundwork_backend()
    {
        services.AddElsa3ImportGroundworkPersistence();
        var registrations = Registry().Registrations.ToArray();
        // A stray EF artifact is only detected after the Groundwork backend has been withdrawn.
        services.AddSingleton(new Elsa3ImportEntityFrameworkCoreOptions());
        var before = services.ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddElsa3ImportEntityFrameworkCore(SqliteOptions));

        AssertUnchanged(before);
        Assert.Equal(registrations, Registry().Registrations);
        Assert.Equal(Elsa3ImportPersistenceBackend.Groundwork, Elsa3ImportPersistenceBackend.Find(services)!.Name);
    }

    [Fact]
    public void Corrupted_ef_ownership_fails_closed_without_mutating_the_collection()
    {
        services.AddElsa3ImportEntityFrameworkCore(SqliteOptions);
        var backend = Elsa3ImportPersistenceBackend.Find(services)!;
        services.Remove(backend.Descriptors[0]);
        var corrupt = services.ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddElsa3ImportEntityFrameworkCore(SqliteOptions));
        Assert.Throws<InvalidOperationException>(() => services.AddElsa3ImportGroundworkPersistence());

        AssertUnchanged(corrupt);
    }

    [Fact]
    public void Ef_feature_depends_on_both_ef_design_lanes_it_enlists()
    {
        var attribute = typeof(Elsa3ImportActivitiesEntityFrameworkCoreFeature)
            .GetCustomAttributes(inherit: false)
            .Single(x => x.GetType().Name == "ShellFeatureAttribute");
        var dependencies = ((IEnumerable<object>?)attribute.GetType().GetProperty("DependsOn")?.GetValue(attribute) ?? [])
            .Select(x => x.ToString()!)
            .ToArray();

        Assert.Equal(["ActivitiesDesignEntityFrameworkCore", "WorkflowsDesignEntityFrameworkCore"], dependencies);
    }

    [Fact]
    public async Task Composed_ef_host_resolves_the_import_and_applies_it_end_to_end()
    {
        var path = Path.Combine(Path.GetTempPath(), $"elsa3-import-ef-host-{Guid.NewGuid():N}.db");
        var connectionString = SqliteImportHarness.ConnectionStringFor(path);
        services.AddSingleton<IPersistenceAccessContextAccessor>(MutableAccess.Tenant("tenant-a"));
        services.AddSingleton(Serializer());
        services.AddSingleton<TimeProvider>(new MutableTimeProvider(Now));
        services.AddSingleton(ImportFixtures.Options());
        services.AddActivitiesDesignEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = connectionString });
        services.AddWorkflowsDesignEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = connectionString });
        new Elsa3ImportActivitiesEntityFrameworkCoreFeature { ConnectionString = connectionString }.ConfigureServices(services);
        services.AddScoped<IReusableActivityImportMaterializer>(_ => Materializer());
        services.AddScoped<IReusableActivityCollectionAnalyzer, ReusableActivityCollectionAnalyzer>();
        services.AddScoped<IReusableActivityCollectionImporter, ReusableActivityCollectionImporter>();
        services.AddScoped<IReusableActivityImportOperationService, ReusableActivityImportOperationService>();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        try
        {
            await SqliteImportHarness.For(connectionString).CreateSchemaAsync();
            var scope = new ReusableActivityImportAccessScope("tenant-a", "user-a");
            ReusableActivityImportReceipt applied;
            await using (var request = provider.CreateAsyncScope())
            {
                var service = request.ServiceProvider.GetRequiredService<IReusableActivityImportOperationService>();
                Assert.IsType<EfReusableActivityImportCommand>(request.ServiceProvider.GetRequiredService<IReusableActivityImportCommand>());
                var upload = await service.UploadAsync(Json(Workflow("a", "a-v1", 1, true, Leaf("root"))), null, scope);
                var analysis = await service.AnalyzeAsync(upload.CollectionHandle, 0, 10, scope);
                applied = await service.ApplyAsync(upload.CollectionHandle, analysis.PlanId, ["a-v1"], "host-key", scope);
            }

            await using (var restarted = provider.CreateAsyncScope())
            {
                var receipt = await restarted.ServiceProvider.GetRequiredService<IReusableActivityImportOperationService>().GetStatusAsync("host-key", scope);
                Assert.Equal(applied.ReceiptId, receipt.ReceiptId);
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    private GroundworkStorageUnitRegistry Registry() =>
        (GroundworkStorageUnitRegistry)Assert.Single(services, x => x.ServiceType == typeof(GroundworkStorageUnitRegistry)).ImplementationInstance!;

    private void AssertUnchanged(IReadOnlyList<ServiceDescriptor> before)
    {
        Assert.Equal(before.Count, services.Count);
        for (var index = 0; index < before.Count; index++)
            Assert.Same(before[index], services[index]);
    }

    private sealed class CustomCommand : IReusableActivityImportCommand
    {
        public ValueTask<ReusableActivityImportCommitResult> CommitAsync(ReusableActivityImportMutation mutation, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class CustomStore : IReusableActivityImportOperationStore
    {
        public ValueTask<bool> TryCreateCollectionAsync(ReusableActivityImportCollectionHandle collection, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<ReusableActivityImportCollectionHandle?> FindCollectionAsync(string handle, ReusableActivityImportAccessScope accessScope, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<ReusableActivityImportReceipt?> FindReceiptAsync(string idempotencyKey, ReusableActivityImportAccessScope accessScope, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
