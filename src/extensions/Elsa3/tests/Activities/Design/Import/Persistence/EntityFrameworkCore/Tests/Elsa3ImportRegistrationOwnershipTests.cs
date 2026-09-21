using Elsa.Activities.Design.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa3.Activities.Design.Import.Composition;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Stores;
using Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Tests.Support;
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Custom_import_command_or_store_fails_closed_without_mutation(bool customCommand)
    {
        if (customCommand)
            services.AddScoped<IReusableActivityImportCommand, CustomCommand>();
        else
            services.AddScoped<IReusableActivityImportOperationStore, CustomStore>();
        var before = services.ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddElsa3ImportEntityFrameworkCore(SqliteOptions));

        AssertUnchanged(before);
        Assert.Null(Elsa3ImportPersistenceBackend.Find(services));
    }

    [Fact]
    public void Corrupted_ef_ownership_fails_closed_without_mutating_the_collection()
    {
        services.AddElsa3ImportEntityFrameworkCore(SqliteOptions);
        var backend = Elsa3ImportPersistenceBackend.Find(services)!;
        services.Remove(backend.Descriptors[0]);
        var corrupt = services.ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddElsa3ImportEntityFrameworkCore(SqliteOptions));

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
            await ImportDatabase.Sqlite(connectionString).CreateSchemaAsync();
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
            TemporarySqliteDatabase.ClearPoolAndDeleteFiles(path);
        }
    }

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
