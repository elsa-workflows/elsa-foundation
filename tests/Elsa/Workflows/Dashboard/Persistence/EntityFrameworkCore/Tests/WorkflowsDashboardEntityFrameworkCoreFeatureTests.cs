using System.Reflection;
using CShells.AspNetCore.Configuration;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Locking.Core;
using Elsa.Serialization.Core;
using Elsa.Tasks;
using Elsa.Workflows.Dashboard.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Resumption;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Workflows.Dashboard.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// <c>WorkflowsDashboardEntityFrameworkCore</c> is the durable workflow dashboard feature: without
/// it the dashboard answers from its unavailable sources even though the EF projections exist.
/// </summary>
public sealed class WorkflowsDashboardEntityFrameworkCoreFeatureTests : IDisposable
{
    private const string FeatureName = "WorkflowsDashboardEntityFrameworkCore";
    private const string ShellName = "dashboard-ef-activation";
    private readonly string databasePath = Path.Join(Path.GetTempPath(), $"elsa-dashboard-ef-feature-{Guid.NewGuid():N}.db");

    private string ConnectionString => $"Data Source={databasePath};Pooling=False";

    [Fact]
    public void Feature_depends_on_the_EF_Runtime_and_Workflows_Design_features_by_their_declared_names()
    {
        var attribute = Assert.Single(typeof(WorkflowsDashboardEntityFrameworkCoreFeature).GetCustomAttributes<ShellFeatureAttribute>(inherit: false));

        Assert.Equal(FeatureName, attribute.Name);
        Assert.Equal(
            [ShellFeatureName<RuntimeEntityFrameworkCoreFeature>(), ShellFeatureName<WorkflowsDesignEntityFrameworkCoreFeature>()],
            attribute.DependsOn.Select(dependency => dependency?.ToString()));
    }

    [Fact]
    public void Feature_refuses_to_compose_before_the_EF_lanes_it_reads()
    {
        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(() => new WorkflowsDashboardEntityFrameworkCoreFeature().ConfigureServices(services));
    }

    [Fact]
    public async Task Shell_activation_routes_run_health_and_portfolio_to_the_EF_projections()
    {
        await using var host = await StartHostAsync();

        var shell = await host.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);

        await using var scope = shell.ServiceProvider.CreateAsyncScope();
        Assert.IsType<EfWorkflowRunHealthDataSource>(scope.ServiceProvider.GetRequiredService<IWorkflowRunHealthDataSource>());
        Assert.IsType<EfWorkflowPortfolioDataSource>(scope.ServiceProvider.GetRequiredService<IWorkflowPortfolioDataSource>());
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<BookmarkStateDbContext>().Database.GetPendingMigrationsAsync());
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>().Database.GetPendingMigrationsAsync());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
            File.Delete(file);
    }

    private static string? ShellFeatureName<TFeature>() =>
        typeof(TFeature).GetCustomAttribute<ShellFeatureAttribute>(inherit: false)?.Name;

    /// <summary>
    /// Composes the shell from configuration, as <c>shells.json</c> does. The EF lanes the dashboard depends on carry
    /// their own settings; their own dependencies (resumption and tasks) are enabled only through DependsOn.
    /// </summary>
    private async Task<WebApplication> StartHostAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var features = $"CShells:Shells:{ShellName}:Features";
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{features}:{FeatureName}"] = null,
            [$"{features}:{RuntimeRootFeature.Name}"] = null,
            [$"{features}:{ShellFeatureName<RuntimeEntityFrameworkCoreFeature>()}:ConnectionString"] = ConnectionString,
            [$"{features}:{ShellFeatureName<RuntimeEntityFrameworkCoreFeature>()}:RecoveryContinuationSigningKey"] = "ef-dashboard-feature-recovery-signing-key-32",
            [$"{features}:{ShellFeatureName<RuntimeEntityFrameworkCoreFeature>()}:HierarchyCursorSigningKey"] = "ef-dashboard-feature-hierarchy-signing-key-32",
            [$"{features}:{ShellFeatureName<WorkflowsDesignEntityFrameworkCoreFeature>()}:ConnectionString"] = ConnectionString
        });
        builder.Services.AddSingleton<IDistributedLockProvider, ProcessLockProvider>();
        builder.Services.AddSingleton<IPayloadSerializer, TestPayloadSerializer>();
        builder.Services.AddCShellsAspNetCore(shells => shells
            .WithAssemblies(
                typeof(WorkflowsDashboardEntityFrameworkCoreFeature).Assembly,
                typeof(RuntimeEntityFrameworkCoreFeature).Assembly,
                typeof(WorkflowsDesignEntityFrameworkCoreFeature).Assembly,
                typeof(WorkflowsRuntimeResumptionFeature).Assembly,
                typeof(TasksFeature).Assembly,
                typeof(RuntimeRootFeature).Assembly)
            .WithConfigurationProvider(builder.Configuration));

        var app = builder.Build();
        app.MapShells();
        await app.StartAsync();
        return app;
    }

    /// <summary>Composes the runtime root as the runtime API feature does in a real shell.</summary>
    [ShellFeature(Name)]
    public sealed class RuntimeRootFeature : IShellFeature
    {
        public const string Name = "DashboardEntityFrameworkCoreTestsRuntimeRoot";

        public void ConfigureServices(IServiceCollection services) => services.AddWorkflowRuntime();
    }

    private sealed class ProcessLockProvider : IDistributedLockProvider
    {
        public IDistributedSynchronizationHandle? TryAcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => new Handle();
        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => ValueTask.FromResult<IDistributedSynchronizationHandle?>(new Handle());
        public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => ValueTask.FromResult<IDistributedSynchronizationHandle>(new Handle());

        private sealed class Handle : IDistributedSynchronizationHandle
        {
            public CancellationToken HandleLostToken => CancellationToken.None;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
