using System.Text.Json;
using Elsa.Workflows.Runtime.Attention;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// The EF Runtime aggregate as a first-class composition: every Runtime participant
/// is selected at once, and a refusal from any participant leaves the service collection exactly as it was.
/// </summary>
public sealed class RuntimeEntityFrameworkCoreStandaloneTests
{
    private const string RecoverySigningKey = "ef-runtime-standalone-recovery-signing-key-32";
    private const string HierarchySigningKey = "ef-runtime-standalone-hierarchy-signing-key-32";

    /// <summary>
    /// Every Runtime contract with its EF implementation. R25 (run health) and R29 (publication projection state) have
    /// no Runtime contract of their own: the EF checkpoint writer stages run health and the dashboard reads it, and
    /// the trigger-binding and recurring-schedule stores own their projection state.
    /// </summary>
    private static readonly (string Row, Type Contract, Type Implementation)[] RuntimeContracts =
    [
        ("R01", typeof(IBookmarkStateStore), typeof(EfBookmarkStateStore)),
        ("R01", typeof(IBookmarkStimulusIndex), typeof(EfBookmarkStateStore)),
        ("R02-R03", typeof(IWorkflowExecutableStore), typeof(CachingWorkflowExecutableStore)),
        ("R04-R05", typeof(IExecutableActivityTemplateStore), typeof(EfExecutableActivityTemplateStore)),
        ("R04-R05", typeof(IExecutableActivityTemplateReader), typeof(EfExecutableActivityTemplateStore)),
        ("R04-R05", typeof(IExecutableActivityTemplateWriter), typeof(EfExecutableActivityTemplateStore)),
        ("R06", typeof(IWorkflowExecutableSourceReferenceStore), typeof(EfWorkflowExecutableSourceReferenceStore)),
        ("R06", typeof(IWorkflowExecutableSourceReferenceReader), typeof(EfWorkflowExecutableSourceReferenceStore)),
        ("R06", typeof(IWorkflowExecutableSourceReferenceWriter), typeof(EfWorkflowExecutableSourceReferenceStore)),
        ("R07", typeof(IActivityExecutionStateStore), typeof(EfActivityExecutionStateStore)),
        ("R08", typeof(IActivityExecutionInspectionStore), typeof(EfActivityExecutionInspectionStore)),
        ("R08", typeof(IActivityExecutionInspectionWriter), typeof(EfActivityExecutionInspectionStore)),
        ("R09", typeof(IActivityExecutionHierarchyStore), typeof(EfActivityExecutionHierarchyStore)),
        ("R09", typeof(IActivityExecutionHierarchyReader), typeof(EfActivityExecutionHierarchyStore)),
        ("R09", typeof(IActivityExecutionHierarchyWriter), typeof(EfActivityExecutionHierarchyStore)),
        ("R10", typeof(IWorkflowExecutionStateStore), typeof(EfWorkflowExecutionStateStore)),
        ("R11-R12", typeof(IWorkflowAlterationStore), typeof(EfWorkflowAlterationStore)),
        ("R13", typeof(IWorkflowTestScopeStore), typeof(EfWorkflowTestScopeStore)),
        ("R13", typeof(IWorkflowTestScopeAdmissionStore), typeof(EfWorkflowTestScopeStore)),
        ("R13", typeof(IWorkflowTestScopeCleanupStore), typeof(EfWorkflowTestScopeCleanupStore)),
        ("R14", typeof(IDurableValueStateStore), typeof(EfDurableValueStateStore)),
        ("R15", typeof(ISchedulerStateStore), typeof(EfSchedulerStateStore)),
        ("R16", typeof(IExecutionLivenessStateStore), typeof(EfExecutionLivenessStateStore)),
        ("R16", typeof(IRuntimeRecoveryScanner), typeof(InMemoryRuntimeRecoveryScanner)),
        ("R17", typeof(IWorkflowHoldStateStore), typeof(EfWorkflowHoldStateStore)),
        ("R18", typeof(IIncidentStateStore), typeof(EfIncidentStateStore)),
        ("R18", typeof(IWorkflowRuntimeAttentionQuery), typeof(EfWorkflowRuntimeAttentionQuery)),
        ("R19", typeof(IRuntimeCheckpointCommitStore), typeof(EfRuntimeCheckpointCommitStore)),
        ("R20", typeof(IRuntimePostCommitOutboxStore), typeof(EfRuntimePostCommitOutboxStore)),
        ("R20", typeof(IPostCommitOutboxLookupStore), typeof(EfRuntimePostCommitOutboxStore)),
        ("R20", typeof(IRuntimePostCommitOutboxClaimStore), typeof(EfRuntimePostCommitOutboxStore)),
        ("R20", typeof(IRuntimePostCommitOutboxClaimCompletionStore), typeof(EfRuntimePostCommitOutboxStore)),
        ("R20", typeof(IWorkflowDispatchRedriveStore), typeof(EfRuntimePostCommitOutboxStore)),
        ("R21", typeof(IWorkflowDispatchStore), typeof(EfWorkflowDispatchStore)),
        ("R21", typeof(IWorkflowDispatchQueryStore), typeof(EfWorkflowDispatchStore)),
        ("R21", typeof(IWorkflowDispatchDeleteStore), typeof(EfWorkflowDispatchStore)),
        ("R21", typeof(IWorkflowDispatchRetentionRootStore), typeof(EfWorkflowDispatchStore)),
        ("R21", typeof(IWorkflowDispatchAdmissionStore), typeof(EfWorkflowDispatchStore)),
        ("R21", typeof(IWorkflowDispatchCancellationStore), typeof(EfWorkflowDispatchStore)),
        ("R22", typeof(IWorkflowSchedulerWorkQueue), typeof(EfSchedulerWorkQueueStore)),
        ("R22", typeof(IWorkflowSchedulerWorkClaimInspection), typeof(EfSchedulerWorkQueueStore)),
        ("R23", typeof(IWorkflowSchedulerPoisonStore), typeof(EfWorkflowSchedulerPoisonStore)),
        ("R24", typeof(IDurableTimerStore), typeof(EfDurableTimerStore)),
        ("R26", typeof(IWorkflowTriggerBindingStore), typeof(EfWorkflowTriggerBindingStore)),
        ("R27", typeof(IRecurringTriggerScheduleStore), typeof(EfRecurringTriggerScheduleStore)),
        ("R28", typeof(IWorkflowActivationAuthority), typeof(EfWorkflowActivationAuthority))
    ];

    public static TheoryData<string> Compositions => ["aggregate only", "runtime root first", "runtime root after"];

    [Theory]
    [MemberData(nameof(Compositions))]
    public void Every_runtime_contract_resolves_to_its_EF_implementation(string composition)
    {
        var services = Compose(composition);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var mismatches = RuntimeContracts
            .Select(entry => (entry.Row, entry.Contract, entry.Implementation,
                Registrations: services.Count(descriptor => descriptor.ServiceType == entry.Contract && !descriptor.IsKeyedService),
                Resolved: scope.ServiceProvider.GetService(entry.Contract)?.GetType()))
            .Where(entry => entry.Registrations != 1 || entry.Resolved != entry.Implementation)
            .Select(entry => $"{entry.Row} {entry.Contract.Name}: {entry.Registrations} registration(s), resolved {entry.Resolved?.Name ?? "nothing"}, expected {entry.Implementation.Name}")
            .ToArray();

        Assert.True(mismatches.Length == 0, string.Join(Environment.NewLine, mismatches));
        Assert.True(Assert.IsType<InMemoryRuntimeRecoveryScanner>(scope.ServiceProvider.GetRequiredService<IRuntimeRecoveryScanner>()).SupportsPaging);
    }

    [Fact]
    public void Every_runtime_participant_backend_is_EF()
    {
        var services = Compose("runtime root first");

        Assert.Equal(RuntimeOperationalStateStoreBackend.EntityFramework, RuntimeOperationalStateStoreBackend.Find(services)?.Name);
        Assert.Equal(RuntimeArtifactStoreBackend.EntityFramework, RuntimeArtifactStoreBackend.Find(services)?.Name);
        Assert.Equal(WorkflowExecutionStateStoreBackend.EntityFramework, WorkflowExecutionStateStoreBackend.Find(services)?.Name);
        Assert.Equal(RuntimeActivityExecutionStoreBackend.EntityFramework, RuntimeActivityExecutionStoreBackend.Find(services)?.Name);
        Assert.Equal(BookmarkStateStoreBackend.EntityFramework, BookmarkStateStoreBackend.Find(services)?.Name);
        Assert.Equal(RuntimeWorkflowAlterationStoreBackend.EntityFramework, RuntimeWorkflowAlterationStoreBackend.Find(services)?.Name);
        Assert.Equal(WorkflowTestScopeStoreBackend.EntityFramework, WorkflowTestScopeStoreBackend.Find(services)?.Name);
        Assert.Equal(SchedulerWorkQueueStoreBackend.EntityFramework, SchedulerWorkQueueStoreBackend.Find(services)?.Name);
        Assert.Equal(WorkflowSchedulerPoisonStoreBackend.EntityFramework, WorkflowSchedulerPoisonStoreBackend.Find(services)?.Name);
        Assert.Equal(DurableTimerStoreBackend.EntityFramework, DurableTimerStoreBackend.Find(services)?.Name);
        Assert.Equal(RuntimeWorkflowDispatchStoreBackend.EntityFramework, RuntimeWorkflowDispatchStoreBackend.Find(services)?.Name);
        Assert.Equal(RuntimePostCommitOutboxStoreBackend.EntityFramework, RuntimePostCommitOutboxStoreBackend.Find(services)?.Name);
        Assert.Equal(RuntimeCheckpointCommitStoreBackend.EntityFramework, RuntimeCheckpointCommitStoreBackend.Find(services)?.Name);
        Assert.Equal(WorkflowTriggerBindingStoreBackend.EntityFramework, WorkflowTriggerBindingStoreBackend.Find(services)?.Name);
        Assert.Equal(RecurringTriggerScheduleStoreBackend.EntityFramework, RecurringTriggerScheduleStoreBackend.Find(services)?.Name);
        Assert.Equal(WorkflowActivationAuthorityBackend.EntityFramework, WorkflowActivationAuthorityBackend.Find(services)?.Name);
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(RuntimeDbContext));
    }

    [Fact]
    public void Repeating_the_aggregate_with_equal_options_changes_nothing()
    {
        var services = Compose("runtime root first");
        var before = services.ToArray();

        services.AddRuntimeEntityFrameworkCore(Options());

        Assert.Equal(before, services);
    }

    public static TheoryData<string> ConflictingOptions =>
        ["connection string", "connection name", "provider", "recovery key", "hierarchy key", "cache capacity", "cache disabled", "schema", "pooling"];

    [Theory]
    [MemberData(nameof(ConflictingOptions))]
    public void Repeating_the_aggregate_with_conflicting_options_fails_without_partial_mutation(string conflict)
    {
        var services = Compose("runtime root first");
        var before = services.ToArray();
        var conflicting = Options();
        switch (conflict)
        {
            case "connection string": conflicting.ConnectionString = "Data Source=other-runtime.db"; break;
            case "connection name": conflicting.ConnectionString = null; conflicting.ConnectionName = "Runtime"; break;
            case "provider": conflicting.Provider = "PostgreSql"; break;
            case "recovery key": conflicting.RecoveryContinuationSigningKey = "ef-runtime-standalone-other-recovery-key-32"; break;
            case "hierarchy key": conflicting.HierarchyCursorSigningKey = "ef-runtime-standalone-other-hierarchy-key-32"; break;
            case "cache capacity": conflicting.WorkflowExecutableCacheCapacity = 7; break;
            case "cache disabled": conflicting.CacheWorkflowExecutables = false; break;
            // Runtime's participants share one context, so a second one asking for another schema or for pooling
            // would be silently ignored in favour of whichever registered first.
            case "schema": conflicting.Schema = "elsa_alt"; break;
            case "pooling": conflicting.Pooling = true; break;
        }

        Assert.Throws<InvalidOperationException>(() => services.AddRuntimeEntityFrameworkCore(conflicting));

        Assert.Equal(before, services);
    }

    [Fact]
    public void A_refusal_from_the_last_participant_rolls_back_every_earlier_participant()
    {
        var services = new ServiceCollection().AddWorkflowRuntime();
        services.AddScoped<IWorkflowActivationAuthority>(_ => throw new InvalidOperationException("foreign activation authority"));
        var before = services.ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddRuntimeEntityFrameworkCore(Options()));

        Assert.Equal(before, services);
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(RuntimeDbContext));
        Assert.NotEqual(RuntimeOperationalStateStoreBackend.EntityFramework, RuntimeOperationalStateStoreBackend.Find(services)?.Name);
        Assert.Null(RuntimeCheckpointCommitStoreBackend.Find(services));
    }

    [Fact]
    public void An_invalid_cache_capacity_is_rejected_before_anything_is_registered()
    {
        var services = new ServiceCollection().AddWorkflowRuntime();
        var before = services.ToArray();
        var options = Options();
        options.WorkflowExecutableCacheCapacity = 0;

        Assert.Throws<ArgumentOutOfRangeException>(() => services.AddRuntimeEntityFrameworkCore(options));

        Assert.Equal(before, services);
    }

    public static TheoryData<string?, string> DefaultConnections => new()
    {
        { null, RuntimeEfModule.DefaultSqliteConnectionString },
        { "Data Source=configured-runtime.db", "Data Source=configured-runtime.db" }
    };

    [Theory]
    [MemberData(nameof(DefaultConnections))]
    public void An_unconfigured_aggregate_resolves_the_single_runtime_default_connection(string? configuredElsaConnection, string expected)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"ConnectionStrings:{RuntimeEfModule.DefaultConnectionName}"] = configuredElsaConnection })
            .Build());
        var options = Options();
        options.ConnectionString = null;

        services.AddRuntimeEntityFrameworkCore(options);

        Assert.Equal(expected, ResolvedConnectionString(services));
    }

    public static TheoryData<string> Participants =>
    [
        "operational state", "artifacts", "workflow executions", "activity executions", "bookmarks",
        "alterations", "test scopes", "scheduler work", "scheduler poison", "durable timers"
    ];

    /// <summary>
    /// Only the first participant to register the shared context chooses its connection, so each one on its own must
    /// resolve the same database that any other would have chosen.
    /// </summary>
    [Theory]
    [MemberData(nameof(Participants))]
    public void Each_participant_alone_resolves_the_same_default_connection(string participant)
    {
        var services = new ServiceCollection();
        _ = participant switch
        {
            "operational state" => services.AddRuntimeOperationalStateEntityFrameworkCore(new()),
            "artifacts" => services.AddRuntimeArtifactsEntityFrameworkCore(new()),
            "workflow executions" => services.AddRuntimeWorkflowExecutionEntityFrameworkCore(new()),
            "activity executions" => services.AddRuntimeActivityExecutionEntityFrameworkCore(new()),
            "bookmarks" => services.AddRuntimeBookmarksEntityFrameworkCore(new()),
            "alterations" => services.AddRuntimeWorkflowAlterationEntityFrameworkCore(new()),
            "test scopes" => services.AddRuntimeWorkflowTestScopeEntityFrameworkCore(new()),
            "scheduler work" => services.AddRuntimeSchedulerWorkQueueEntityFrameworkCore(new()),
            "scheduler poison" => services.AddRuntimeSchedulerPoisonEntityFrameworkCore(new()),
            "durable timers" => services.AddRuntimeDurableTimerEntityFrameworkCore(new()),
            _ => throw new ArgumentOutOfRangeException(nameof(participant))
        };

        Assert.Equal(RuntimeEfModule.DefaultSqliteConnectionString, ResolvedConnectionString(services));
    }

    [Fact]
    public void A_missing_named_connection_fails_when_the_context_is_resolved()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        var options = Options();
        options.ConnectionString = null;
        options.ConnectionName = "Missing";
        services.AddRuntimeEntityFrameworkCore(options);

        var exception = Assert.Throws<InvalidOperationException>(() => ResolvedConnectionString(services));
        Assert.Contains("'Missing' was not found or was empty", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Data Source=:memory:", WorkflowDispatchDurabilityLevel.ProcessLocal)]
    [InlineData("Data Source=file:runtime-ef-standalone;Mode=Memory;Cache=Shared", WorkflowDispatchDurabilityLevel.ProcessLocal)]
    [InlineData("Data Source=runtime-ef-standalone.db", WorkflowDispatchDurabilityLevel.Durable)]
    public async Task Dispatch_durability_evidence_reports_the_actual_database_lifetime(string connectionString, WorkflowDispatchDurabilityLevel expected)
    {
        var services = new ServiceCollection().AddWorkflowRuntime();
        var options = Options();
        options.ConnectionString = connectionString;
        services.AddRuntimeEntityFrameworkCore(options);
        services.AddSingleton<IWorkflowDispatchDurabilityEvidence>(
            new WorkflowDispatchDurabilityEvidence(WorkflowDispatchDurabilityComponents.Resumption, WorkflowDispatchDurabilityLevel.Durable));

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var report = await scope.ServiceProvider.GetRequiredService<IWorkflowDispatchReadinessAssessor>().AssessAsync();

        Assert.Equal(expected == WorkflowDispatchDurabilityLevel.Durable, report.Ready);
        foreach (var component in new[]
                 {
                     WorkflowDispatchDurabilityComponents.Checkpoint, WorkflowDispatchDurabilityComponents.DispatchStore,
                     WorkflowDispatchDurabilityComponents.Outbox, WorkflowDispatchDurabilityComponents.Scheduler
                 })
            Assert.Equal(expected, Assert.Single(report.Components, item => item.Component == component).Level);
    }

    [Fact]
    public async Task The_executable_cache_serves_ordinary_reads_and_never_crosses_persistence_scopes()
    {
        await using var database = await SharedMemoryDatabase.CreateAsync();
        await using var provider = database.Compose(Options()).BuildServiceProvider();
        await database.CreateSchemaAsync(provider);
        var executable = Executable("cached-artifact");

        await using (var scope = provider.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>();
            Assert.IsType<CachingWorkflowExecutableStore>(store);
            await store.SaveAsync(executable);
            Assert.NotNull(await store.FindAsync(executable.Identity.ArtifactId));
        }

        await database.DeleteExecutableRowsAsync(provider);

        // The rows are gone, so only the cache can still answer in the same persistence scope.
        await using (var scope = provider.CreateAsyncScope())
            Assert.NotNull(await scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>().FindAsync(executable.Identity.ArtifactId));

        await using var otherTenant = await provider.GetRequiredService<IPersistenceOperationScopeFactory>().CreateAsync(new PersistenceScope("other-tenant"));
        Assert.Null(await otherTenant.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>().FindAsync(executable.Identity.ArtifactId));
    }

    [Fact]
    public async Task Privileged_access_bypasses_the_executable_cache_and_the_cache_adapter_refuses_it()
    {
        await using var database = await SharedMemoryDatabase.CreateAsync();
        await using var provider = database.Compose(Options()).BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(
            PersistenceAccessContext.PrivilegedScoped(new PersistenceScope("tenant-a"), new PersistenceAccessPurpose("maintenance")));

        Assert.IsType<InvalidatingWorkflowExecutableStore>(scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>());
        Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<CachingWorkflowExecutableStore>());
    }

    [Fact]
    public async Task A_disabled_executable_cache_reads_straight_from_the_database()
    {
        await using var database = await SharedMemoryDatabase.CreateAsync();
        var options = Options();
        options.CacheWorkflowExecutables = false;
        await using var provider = database.Compose(options).BuildServiceProvider();
        await database.CreateSchemaAsync(provider);
        var executable = Executable("uncached-artifact");

        await using (var scope = provider.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>();
            Assert.IsType<EfWorkflowExecutableStore>(store);
            await store.SaveAsync(executable);
            Assert.NotNull(await store.FindAsync(executable.Identity.ArtifactId));
        }

        await database.DeleteExecutableRowsAsync(provider);

        await using (var scope = provider.CreateAsyncScope())
            Assert.Null(await scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>().FindAsync(executable.Identity.ArtifactId));
        Assert.False(provider.GetRequiredService<WorkflowExecutableCacheOptions>().Enabled);
    }

    private static IServiceCollection Compose(string composition)
    {
        var services = new ServiceCollection();
        if (composition == "runtime root first")
            services.AddWorkflowRuntime();
        services.AddRuntimeEntityFrameworkCore(Options());
        if (composition == "runtime root after")
            services.AddWorkflowRuntime();
        return services;
    }

    private static RuntimeEntityFrameworkCoreOptions Options() => new()
    {
        Provider = "Sqlite",
        ConnectionString = "Data Source=:memory:",
        RecoveryContinuationSigningKey = RecoverySigningKey,
        HierarchyCursorSigningKey = HierarchySigningKey
    };

    private static string? ResolvedConnectionString(IServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<RuntimeDbContext>().Database.GetConnectionString();
    }

    private static WorkflowExecutable Executable(string artifactId) => new(
        new WorkflowExecutableIdentity(artifactId, "definition", "version", "1", $"hash-{artifactId}"),
        new ExecutableNode("node", "node", "test", "1", "consumer", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), outputCaptures: new Dictionary<string, RuntimeOutputCapture>()),
        new Dictionary<string, WorkflowExecutableResumeTarget>(),
        DateTimeOffset.UtcNow,
        new Dictionary<string, string>(),
        IncidentStrategyBuiltIns.FaultReference);

    /// <summary>A named shared in-memory SQLite database that lives as long as its keeper connection.</summary>
    private sealed class SharedMemoryDatabase(SqliteConnection keeper) : IAsyncDisposable
    {
        public static async Task<SharedMemoryDatabase> CreateAsync()
        {
            var keeper = new SqliteConnection($"Data Source=file:runtime-ef-standalone-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
            await keeper.OpenAsync();
            return new(keeper);
        }

        public IServiceCollection Compose(RuntimeEntityFrameworkCoreOptions options)
        {
            options.ConnectionString = keeper.ConnectionString;
            return new ServiceCollection().AddWorkflowRuntime().AddRuntimeEntityFrameworkCore(options);
        }

        public async Task CreateSchemaAsync(IServiceProvider provider)
        {
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<RuntimeDbContext>().Database.EnsureCreatedAsync();
        }

        public async Task DeleteExecutableRowsAsync(IServiceProvider provider)
        {
            await using var scope = provider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<RuntimeDbContext>();
            Assert.Equal(1, await context.WorkflowExecutables.ExecuteDeleteAsync());
            await context.WorkflowExecutableCoordinations.ExecuteDeleteAsync();
        }

        public ValueTask DisposeAsync() => keeper.DisposeAsync();
    }
}
