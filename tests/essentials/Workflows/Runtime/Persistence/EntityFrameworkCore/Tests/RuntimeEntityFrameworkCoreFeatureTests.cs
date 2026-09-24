using System.Reflection;
using CShells.AspNetCore.Configuration;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Locking.Core;
using Elsa.Persistence.EntityFramework;
using Elsa.Tasks;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Resumption;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// <c>WorkflowsRuntimeEntityFrameworkCore</c> is the shell-level durable workflow runtime
/// feature, selected by name from shell configuration.
/// </summary>
public sealed class RuntimeEntityFrameworkCoreFeatureTests : IDisposable
{
    private const string FeatureName = "WorkflowsRuntimeEntityFrameworkCore";
    private const string ShellName = "runtime-ef-activation";
    private const string RecoverySigningKey = "ef-runtime-feature-recovery-signing-key-32-bytes";
    private const string HierarchySigningKey = "ef-runtime-feature-hierarchy-signing-key-32-bytes";
    private readonly string databasePath = Path.Join(Path.GetTempPath(), $"elsa-runtime-ef-feature-{Guid.NewGuid():N}.db");

    private string ConnectionString => $"Data Source={databasePath};Pooling=False";

    [Fact]
    public void Feature_declares_its_shell_name_dependency_defaults_and_manifest_settings()
    {
        var attribute = Assert.Single(typeof(RuntimeEntityFrameworkCoreFeature).GetCustomAttributes<ShellFeatureAttribute>(inherit: false));
        Assert.Equal(FeatureName, attribute.Name);
        Assert.Equal(["WorkflowsRuntimeResumption"], attribute.DependsOn.Select(dependency => dependency?.ToString()));

        var feature = new RuntimeEntityFrameworkCoreFeature();
        Assert.Equal("Sqlite", feature.Provider);
        Assert.True(feature.CacheWorkflowExecutables);
        Assert.Equal(WorkflowExecutableCacheOptions.DefaultCapacity, feature.WorkflowExecutableCacheCapacity);

        string[] secrets = [nameof(feature.ConnectionString), nameof(feature.RecoveryContinuationSigningKey), nameof(feature.HierarchyCursorSigningKey)];
        foreach (var property in typeof(RuntimeEntityFrameworkCoreFeature).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var setting = Assert.Single(property.GetCustomAttributesData(), data =>
                data.AttributeType.FullName == "Elsa.Specifications.PackageManifest.Generator.Hints.ManifestSettingAttribute");
            Assert.Equal(secrets.Contains(property.Name), setting.NamedArguments.Any(argument => argument.MemberName == "Secret" && argument.TypedValue.Value is true));
        }
    }

    [Fact]
    public void Feature_threads_its_settings_and_registers_the_runtime_migrator()
    {
        var services = new ServiceCollection();
        NewFeature(capacity: 29, cache: false).ConfigureServices(services);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var cache = provider.GetRequiredService<WorkflowExecutableCacheOptions>();
        Assert.False(cache.Enabled);
        Assert.Equal(29, cache.Capacity);
        var recovery = provider.GetRequiredService<IOptions<RuntimeRecoveryContinuationOptions>>().Value;
        Assert.Equal(RecoverySigningKey, recovery.SigningKey);
        Assert.False(recovery.AllowEphemeralDevelopmentKey);
        Assert.Equal(HierarchySigningKey, provider.GetRequiredService<IOptions<ActivityExecutionHierarchyCursorOptions>>().Value.SigningKey);
        Assert.Single(provider.GetServices<IShellInitializer>().OfType<EfModuleMigrator<RuntimeDbContext>>());
        Assert.Equal(EfProviderNames.Sqlite, provider.GetRequiredService<EfModuleMigration<RuntimeDbContext>>().ExpectedProviderName);
    }

    /// <summary>
    /// The runtime API feature assigns its own hierarchy key even when it has none, and a shell may compose it after
    /// this feature; the key configured here must still be the one the codec signs with.
    /// </summary>
    [Fact]
    public void The_hierarchy_key_survives_a_runtime_api_feature_composed_afterwards()
    {
        var services = new ServiceCollection();
        NewFeature().ConfigureServices(services);
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        Assert.Equal(HierarchySigningKey, provider.GetRequiredService<IOptions<ActivityExecutionHierarchyCursorOptions>>().Value.SigningKey);
        Assert.IsType<HmacActivityExecutionHierarchyCursorCodec>(provider.GetRequiredService<IActivityExecutionHierarchyCursorCodec>());
    }

    [Fact]
    public async Task Shell_activation_resolves_the_feature_by_name_applies_the_runtime_migrations_and_selects_EF()
    {
        await using var host = await StartHostAsync(new()
        {
            [nameof(RuntimeEntityFrameworkCoreFeature.Provider)] = "Sqlite",
            [nameof(RuntimeEntityFrameworkCoreFeature.ConnectionString)] = ConnectionString,
            [nameof(RuntimeEntityFrameworkCoreFeature.WorkflowExecutableCacheCapacity)] = "17",
            [nameof(RuntimeEntityFrameworkCoreFeature.RecoveryContinuationSigningKey)] = RecoverySigningKey,
            [nameof(RuntimeEntityFrameworkCoreFeature.HierarchyCursorSigningKey)] = HierarchySigningKey
        });

        var shell = await host.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);

        await using var scope = shell.ServiceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<RuntimeDbContext>();
        Assert.NotEmpty(await context.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.Equal(1, await CountTablesAsync(RuntimeEfModule.HistoryTableName));
        Assert.IsType<EfWorkflowExecutionStateStore>(scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>());
        Assert.IsType<EfRuntimeCheckpointCommitStore>(scope.ServiceProvider.GetRequiredService<IRuntimeCheckpointCommitStore>());
        Assert.IsType<EfWorkflowActivationAuthority>(scope.ServiceProvider.GetRequiredService<IWorkflowActivationAuthority>());
        Assert.Equal(17, shell.ServiceProvider.GetRequiredService<WorkflowExecutableCacheOptions>().Capacity);
    }

    [Fact]
    public async Task Shell_activation_fails_closed_without_a_recovery_signing_key()
    {
        await using var host = await StartHostAsync(new()
        {
            [nameof(RuntimeEntityFrameworkCoreFeature.Provider)] = "Sqlite",
            [nameof(RuntimeEntityFrameworkCoreFeature.ConnectionString)] = ConnectionString,
            [nameof(RuntimeEntityFrameworkCoreFeature.HierarchyCursorSigningKey)] = HierarchySigningKey
        });

        var exception = await Assert.ThrowsAnyAsync<Exception>(() => host.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName));

        Assert.Contains("signing key", Flatten(exception), StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
            File.Delete(file);
    }

    private static RuntimeEntityFrameworkCoreFeature NewFeature(int capacity = WorkflowExecutableCacheOptions.DefaultCapacity, bool cache = true) => new()
    {
        Provider = "Sqlite",
        ConnectionString = "Data Source=:memory:",
        CacheWorkflowExecutables = cache,
        WorkflowExecutableCacheCapacity = capacity,
        RecoveryContinuationSigningKey = RecoverySigningKey,
        HierarchyCursorSigningKey = HierarchySigningKey
    };

    /// <summary>Composes the shell from configuration, as <c>shells.json</c> does, so the feature is found by name.</summary>
    private static async Task<WebApplication> StartHostAsync(Dictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(settings
            .Select(setting => KeyValuePair.Create($"CShells:Shells:{ShellName}:Features:{FeatureName}:{setting.Key}", setting.Value))
            .Append(KeyValuePair.Create($"CShells:Shells:{ShellName}:Features:{RuntimeRootFeature.Name}", (string?)null)));
        // The Tasks feature the runtime depends on runs its startup tasks under a distributed lock.
        builder.Services.AddSingleton<IDistributedLockProvider, ProcessLockProvider>();
        builder.Services.AddCShellsAspNetCore(shells => shells
            .WithAssemblies(
                typeof(RuntimeEntityFrameworkCoreFeature).Assembly,
                typeof(WorkflowsRuntimeResumptionFeature).Assembly,
                typeof(TasksFeature).Assembly,
                typeof(RuntimeRootFeature).Assembly)
            .WithConfigurationProvider(builder.Configuration));

        var app = builder.Build();
        app.MapShells();
        await app.StartAsync();
        return app;
    }

    private async Task<long> CountTablesAsync(string table)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
        command.Parameters.AddWithValue("$name", table);
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    private static string Flatten(Exception exception)
    {
        var messages = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
            messages.Add(current.Message);
        return string.Join(" | ", messages);
    }

    /// <summary>
    /// Composes the runtime root as the runtime API feature does in a real shell. It declares no ordering against the
    /// persistence feature, so the shell may configure either one first.
    /// </summary>
    [ShellFeature(Name)]
    public sealed class RuntimeRootFeature : IShellFeature
    {
        public const string Name = "RuntimeEntityFrameworkCoreTestsRuntimeRoot";

        public void ConfigureServices(IServiceCollection services) => services.AddWorkflowRuntime();
    }

    internal sealed class ProcessLockProvider : IDistributedLockProvider
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
