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
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Workflows.Dashboard.Persistence.EntityFrameworkCore.Tests;

public sealed class WorkflowsDashboardPortfolioEntityFrameworkCoreFeatureTests : IDisposable
{
    private const string FeatureName = WorkflowsDashboardPortfolioEntityFrameworkCoreFeature.FeatureName;
    private const string ShellName = "dashboard-ef-activation";
    private readonly string databasePath = Path.Join(Path.GetTempPath(), $"elsa-dashboard-ef-feature-{Guid.NewGuid():N}.db");

    private string ConnectionString => $"Data Source={databasePath};Pooling=False";

    [Fact]
    public void Feature_depends_on_the_EF_Runtime_and_Workflows_Design_features_by_their_declared_names()
    {
        var attribute = Assert.Single(typeof(WorkflowsDashboardPortfolioEntityFrameworkCoreFeature).GetCustomAttributes<ShellFeatureAttribute>(inherit: false));

        Assert.Equal(FeatureName, attribute.Name);
        Assert.Equal(
            [ShellFeatureName<RuntimeArtifactsEntityFrameworkCoreFeature>(), ShellFeatureName<WorkflowsDesignEntityFrameworkCoreFeature>()],
            attribute.DependsOn.Select(dependency => dependency?.ToString()));
    }

    [Fact]
    public void Feature_refuses_to_compose_before_the_EF_lanes_it_reads()
    {
        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(() => new WorkflowsDashboardPortfolioEntityFrameworkCoreFeature().ConfigureServices(services));
    }

    [Fact]
    public async Task Shell_activation_routes_portfolio_to_the_EF_projections()
    {
        await using var host = await StartHostAsync();

        var shell = await host.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);

        await using var scope = shell.ServiceProvider.CreateAsyncScope();
        Assert.IsType<EfWorkflowPortfolioDataSource>(scope.ServiceProvider.GetRequiredService<IWorkflowPortfolioDataSource>());
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<RuntimeDbContext>().Database.GetPendingMigrationsAsync());
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>().Database.GetPendingMigrationsAsync());
    }

    public void Dispose()
    {
        foreach (var file in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
            File.Delete(file);
    }

    private static string? ShellFeatureName<TFeature>() =>
        typeof(TFeature).GetCustomAttribute<ShellFeatureAttribute>(inherit: false)?.Name;

    private async Task<WebApplication> StartHostAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var features = $"CShells:Shells:{ShellName}:Features";
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{features}:{FeatureName}"] = null,
            [$"{features}:{ShellFeatureName<RuntimeArtifactsEntityFrameworkCoreFeature>()}:ConnectionString"] = ConnectionString,
            [$"{features}:{ShellFeatureName<RuntimeArtifactsEntityFrameworkCoreFeature>()}:RecoveryContinuationSigningKey"] = "ef-dashboard-feature-recovery-signing-key-32",
            [$"{features}:{ShellFeatureName<WorkflowsDesignEntityFrameworkCoreFeature>()}:ConnectionString"] = ConnectionString
        });
        builder.Services.AddSingleton<IDistributedLockProvider, ProcessLockProvider>();
        builder.Services.AddSingleton<IPayloadSerializer, TestPayloadSerializer>();
        builder.Services.AddCShellsAspNetCore(shells => shells
            .WithAssemblies(
                typeof(WorkflowsDashboardPortfolioEntityFrameworkCoreFeature).Assembly,
                typeof(RuntimeArtifactsEntityFrameworkCoreFeature).Assembly,
                typeof(WorkflowsDesignEntityFrameworkCoreFeature).Assembly,
                typeof(TasksFeature).Assembly)
            .WithConfigurationProvider(builder.Configuration));

        var app = builder.Build();
        app.MapShells();
        await app.StartAsync();
        return app;
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
