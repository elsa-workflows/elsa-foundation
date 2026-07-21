using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Activities.Design.Persistence.EFCore.DbContext;
using Elsa.Activities.Design.Persistence.EFCore.Sqlite;
using Elsa.Activities.Design.Reconciliation;
using Elsa.DesignPersistence.Benchmarks.Workload;
using Elsa.Events;
using Elsa.Events.Core.Contracts;
using Elsa.Events.Strategies;
using Elsa.Locking.Core;
using Elsa.Persistence.Groundwork.ReferenceComposition;
using Elsa.Persistence.Groundwork.Sqlite.Unified.DependencyInjection;
using Elsa.Primitives.Contracts;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Elsa.Workflows.Design.Persistence.EFCore.Sqlite;
using Elsa.Workflows.Design.Validations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.DesignPersistence.Benchmarks.Stores;

/// <summary>
/// Minimal duplicated production composition for the temporary same-provider EF oracle and the
/// Groundwork SQLite unified store. It intentionally mirrors the
/// <c>{EFCore,Sqlite}DesignPersistenceContractFixture</c> compositions (minus their test-only fault
/// injection and event capture) rather than referencing the test projects, per the repo convention
/// that benchmarks do not depend on test assemblies.
/// </summary>
public static class DesignComposition
{
    // --- EF oracle --------------------------------------------------------------------------

    public static ServiceProvider BuildEf(string connectionString)
    {
        var services = new ServiceCollection();
        AddSharedDesignServices(services);
        services.AddSingleton<JsonPayloadConverterRegistry>();
        services.AddSingleton<IPayloadSerializer, JsonPayloadSerializer>();
        new EventsFeature().ConfigureServices(services);
        services.AddScoped<IDeferredEventPublisher>(sp => new BackgroundDeferredEventPublisher(sp.GetRequiredService<IEventPublisher>()));

        new BenchmarkWorkflowsEfFeature(connectionString).ConfigureServices(services);
        new BenchmarkActivitiesEfFeature(connectionString).ConfigureServices(services);
        new WorkflowDesignValidationsFeature().ConfigureServices(services);
        new ActivitiesDesignReconciliationFeature().ConfigureServices(services);

        return services.BuildServiceProvider(validateScopes: true);
    }

    public static async Task MigrateEfAsync(ServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var workflows = scope.ServiceProvider.GetRequiredService<IDbContextFactory<WorkflowsDesignDbContext>>();
        var activities = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ActivitiesDesignDbContext>>();
        await using var workflowContext = await workflows.CreateDbContextAsync(cancellationToken);
        await using var activityContext = await activities.CreateDbContextAsync(cancellationToken);
        await workflowContext.Database.MigrateAsync(cancellationToken);
        await activityContext.Database.MigrateAsync(cancellationToken);
    }

    private sealed class BenchmarkWorkflowsEfFeature : SqliteWorkflowsDesignPersistenceShellFeature
    {
        public BenchmarkWorkflowsEfFeature(string connectionString)
        {
            ConnectionString = connectionString;
            RunMigrations = false;
        }

        protected override Assembly GetMigrationsAssembly() => typeof(SqliteWorkflowsDesignPersistenceShellFeature).Assembly;
    }

    private sealed class BenchmarkActivitiesEfFeature : SqliteActivitiesDesignPersistenceShellFeature
    {
        public BenchmarkActivitiesEfFeature(string connectionString)
        {
            ConnectionString = connectionString;
            RunMigrations = false;
        }

        protected override Assembly GetMigrationsAssembly() => typeof(SqliteActivitiesDesignPersistenceShellFeature).Assembly;
    }

    // --- Groundwork SQLite unified (physical-entity production form) ------------------------

    public static ServiceProvider BuildGroundwork(string connectionString)
    {
        var services = new ServiceCollection();
        AddSharedDesignServices(services);
        services.AddSingleton<IPayloadSerializer, BenchmarkPayloadSerializer>();
        services.AddSingleton<IActivityStructureService, EmptyActivityStructureService>();
        new EventsFeature().ConfigureServices(services);
        services.AddScoped<IDeferredEventPublisher>(sp => new BackgroundDeferredEventPublisher(sp.GetRequiredService<IEventPublisher>()));

        services.AddGroundworkSqliteUnifiedPersistence(connectionString, autoApplyOnStartup: false);
        new WorkflowDesignValidationsFeature().ConfigureServices(services);
        new ActivitiesDesignReconciliationFeature().ConfigureServices(services);

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static void AddSharedDesignServices(IServiceCollection services)
    {
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<ISystemClock>(new FixedSystemClock(DesignWorkload.Epoch));
        services.AddSingleton<IIdentityGenerator, SequentialIdentityGenerator>();
        services.AddSingleton<IDistributedLockProvider, ImmediateLockProvider>();
    }

    // --- Shared support types ---------------------------------------------------------------

    private sealed class FixedSystemClock(DateTimeOffset utcNow) : ISystemClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class SequentialIdentityGenerator : IIdentityGenerator
    {
        private long _next;
        public string Generate() => $"bench-id-{Interlocked.Increment(ref _next):D9}";
    }

    private sealed class ImmediateLockProvider : IDistributedLockProvider
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

    private sealed class BackgroundDeferredEventPublisher(IEventPublisher eventPublisher) : IDeferredEventPublisher
    {
        public Task Publish(IEvent @event, CancellationToken cancellationToken = default) =>
            eventPublisher.Publish(@event, EventPublishingStrategy.Background, cancellationToken);
    }

    private sealed class BenchmarkPayloadSerializer : IPayloadSerializer
    {
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = false };
        public string Serialize(object payload) => JsonSerializer.Serialize(payload, Options);
        public JsonElement SerializeToElement(object payload) => JsonSerializer.SerializeToElement(payload, Options);
        public object Deserialize(string serializedData) => JsonSerializer.Deserialize<object>(serializedData, Options)!;
        public object Deserialize(string serializedData, Type type) => JsonSerializer.Deserialize(serializedData, type, Options)!;
        public object Deserialize(JsonElement serializedData) => serializedData.Deserialize<object>(Options)!;
        public T Deserialize<T>(string serializedData) => JsonSerializer.Deserialize<T>(serializedData, Options)!;
        public T Deserialize<T>(JsonElement serializedData) => serializedData.Deserialize<T>(Options)!;
        public JsonSerializerOptions GetOptions() => new(Options);
    }

    private sealed class EmptyActivityStructureService : IActivityStructureService
    {
        public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity) => [];
        public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections) => activity;
        public ActivityNodeStructure? CompileExecutableStructure(ActivityNode activity) => null;
        public IReadOnlyCollection<Elsa.Expressions.Core.Models.VariableDefinition> ProjectScopedVariables(ActivityNode activity) => [];
        public bool SupportsScopedVariables(ActivityNode activity) => false;
    }

    // --- Groundwork schema CLI (duplicated from the SQLite fixture) --------------------------

    public static class GroundworkSchemaCli
    {
        private const string ConnectionEnvironmentVariable = "ELSA_DESIGN_GROUNDWORK_SQLITE_CONNECTION";
        private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(3);

        public static async Task ApplyFreshAsync(string connectionString, CancellationToken cancellationToken)
        {
            var offline = await RunAsync("validate", null, cancellationToken, "--offline");
            if (offline.ExitCode != 0)
                throw offline.ToException("Groundwork offline schema validation failed");

            var apply = await RunAsync("apply", connectionString, cancellationToken, "--safe");
            if (apply.ExitCode != 0)
                throw apply.ToException("Groundwork safe schema apply failed");
        }

        private static async Task<CliResult> RunAsync(string command, string? connectionString, CancellationToken cancellationToken, params string[] extraArguments)
        {
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = FindRepositoryRoot(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (var argument in new[]
                     {
                         "tool", "run", "groundwork", "--", command,
                         "--manifest-assembly", typeof(GroundworkAllFeaturesDeploymentSchema).Assembly.Location,
                         "--manifest-type", typeof(GroundworkAllFeaturesDeploymentSchema).FullName!,
                         "--provider", "sqlite",
                         "--output", "json"
                     })
                start.ArgumentList.Add(argument);
            if (connectionString is not null)
            {
                start.Environment[ConnectionEnvironmentVariable] = connectionString;
                start.ArgumentList.Add("--connection-env");
                start.ArgumentList.Add(ConnectionEnvironmentVariable);
            }

            foreach (var argument in extraArguments)
                start.ArgumentList.Add(argument);

            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the Groundwork schema tool.");
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(CommandTimeout);
            await process.WaitForExitAsync(timeout.Token);
            var error = await errorTask;
            await outputTask;
            return new CliResult(process.ExitCode, error);
        }

        private static string FindRepositoryRoot()
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current is not null && !File.Exists(Path.Join(current.FullName, "Elsa.Server.slnx")))
                current = current.Parent;
            return current?.FullName ?? throw new InvalidOperationException("Could not locate the repository root for Groundwork.Tool.");
        }

        private sealed record CliResult(int ExitCode, string StandardError)
        {
            public Exception ToException(string message) =>
                new InvalidOperationException($"{message} (exit {ExitCode}; stderr digest {Digest(StandardError)}).");

            private static string Digest(string value) =>
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
        }
    }
}
