using CShells.Lifecycle;
using CShells.Features;
using Elsa.Persistence.EntityFramework.SchemaBackfill;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// Applies or validates one module context's migrations on both lifecycle hooks: CShells activates and
/// reloads shells through <see cref="IShellInitializer"/>, while plain hosts start <see cref="IHostedService"/>s.
/// The host-wide <see cref="EfMigrateOptions"/> choose between auto-migrate and fail-closed validate, bound from
/// <see cref="EfMigrateOptions.SectionName"/>, so an operator can apply migrations out of process and start hosts
/// in validate mode without a code change.
/// </summary>
/// <remarks>
/// A pending contracting migration whose schema family is not yet finalized at the version its opt-out names refuses
/// the whole pending batch here, under both policies, before any of it runs (spec 185, FR-024): that check is
/// <see cref="EfDatabaseMigrator"/>'s, so the persistence tool's <c>apply</c> makes it too. On a database no host has
/// admitted the module in, the migrator creates each contracted family's record first, naming this host's member.
/// Once the schema is current and its post-migration actions audited, the module's finalization gate admits it
/// (spec 181, FR-015): a family whose finalized version this host cannot read refuses the module here, under both
/// policies, before any shell task, seeder or store touches its tables, exactly as a pending migration under
/// <see cref="EfMigratePolicy.Validate"/> does. The gate then keeps evaluating and refreshing in the background until
/// the shell or host stops, so writers switch versions without a restart or a shell reload (FR-011). Beside it runs the
/// module's post-finalization backfill (spec 186), which upgrades the rows below each family's finalized version and
/// records the family complete. It runs here, in the shell with the shell's services, and not as a post-migration action,
/// whose audit at Prepare would refuse the very module finalization needs active.
/// </remarks>
public sealed class EfModuleMigrator<TContext>(
    IServiceScopeFactory scopes,
    EfModuleMigration<TContext> migration,
    IOptions<EfMigrateOptions> options,
    IServiceProvider services) : IHostedService, IShellInitializer, IAsyncDisposable, IDisposable
    where TContext : DbContext
{
    private readonly SemaphoreSlim _admission = new(1, 1);
    private readonly CancellationTokenSource _stopping = new();
    private EfSchemaModuleGate? _gate;
    private Task? _gateLoop;
    private Task? _backfillLoop;

    /// <summary>The module's finalization gate, once it has admitted the module.</summary>
    public EfSchemaModuleGate? Gate => _gate;

    public Task InitializeAsync(CancellationToken cancellationToken = default) => ApplyAsync(cancellationToken);

    public Task StartAsync(CancellationToken cancellationToken) => ApplyAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => StopGateAsync();

    public async ValueTask DisposeAsync()
    {
        await StopGateAsync();
        _stopping.Dispose();
        _admission.Dispose();
    }

    /// <summary>For a container disposed synchronously: the gate's loop ends as soon as it is cancelled.</summary>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    private async Task ApplyAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        // A later backend switch can replace this module's EF registration; nothing is left to migrate then.
        var context = scope.ServiceProvider.GetService<TContext>();
        if (context is null)
            return;
        // MigrateAsync takes EF's migration lock, and applying is idempotent, so running on both hooks is safe. A record
        // created before a contracting migration names this host's member (#2136).
        await EfDatabaseMigrator.ApplyAsync(
            context,
            migration.ExpectedProviderName,
            options.Value.Policy,
            services.GetService<IEfSchemaFleet>()?.GetLocalStanding().Member,
            cancellationToken);
        // Under both policies (FR-056), and only ever after the schema is known to be current: an audit that
        // read a pre-migration schema would answer a question about a database that no longer exists. Under
        // Validate the line above has already thrown for a pending migration, so reaching here means current.
        await EfPostMigrationActions.EnsureNotRequiredAsync(
            context,
            migration.Module,
            migration.Provider,
            migration.PostMigration,
            cancellationToken);
        await AdmitAsync(context, cancellationToken);
    }

    /// <summary>
    /// Admits the module through its finalization gate, once however many hooks run, and starts the gate's background
    /// evaluation and refresh. A refusal propagates, so the shell or host does not activate the module.
    /// </summary>
    private async Task AdmitAsync(TContext context, CancellationToken cancellationToken)
    {
        await _admission.WaitAsync(cancellationToken);
        try
        {
            if (_gate is not null)
                return;
            var families = EfSchemaModuleFamilies.For(migration.Module, typeof(TContext).Assembly);
            // A context that owns no schema family, such as a test's own, has nothing to finalize.
            if (families.Chains.Count == 0)
                return;

            var gate = new EfSchemaModuleGate(
                families,
                services.GetService<IEfSchemaFleet>(),
                services.GetService<EfSchemaFinalizationObservations>() ?? new EfSchemaFinalizationObservations(),
                services.GetService<IOptions<EfSchemaFinalizationOptions>>()?.Value ?? new EfSchemaFinalizationOptions(),
                services.GetService<TimeProvider>(),
                services.GetService<ILoggerFactory>()?.CreateLogger<EfModuleMigrator<TContext>>(),
                publishBeforeRead: !migration.HostComposed);
            await gate.ActivateAsync(context, cancellationToken);
            // Before the gate is registered, so whatever finds it can have it refresh or read its status on demand
            // (spec 182, FR-014 and FR-022).
            gate.UseContexts(WithContextAsync);
            services.GetService<EfSchemaFinalizationGates>()?.Register(typeof(TContext), gate);
            _gate = gate;
            _gateLoop = Task.Run(() => gate.RunAsync(WithContextAsync, _stopping.Token), CancellationToken.None);
            var backfill = new EfSchemaBackfill(
                gate,
                services.GetService<IEfSchemaFleet>(),
                services.GetService<IOptions<EfSchemaBackfillOptions>>()?.Value ?? new EfSchemaBackfillOptions(),
                services.GetService<TimeProvider>(),
                services.GetService<ILoggerFactory>()?.CreateLogger<EfSchemaBackfill>());
            gate.UseBackfill(backfill);
            _backfillLoop = Task.Run(() => backfill.RunAsync(WithBackfillScopeAsync, _stopping.Token), CancellationToken.None);
        }
        finally
        {
            _admission.Release();
        }
    }

    private async Task WithContextAsync(Func<DbContext, Task> action, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        if (scope.ServiceProvider.GetService<TContext>() is { } context)
            await action(context);
    }

    private async Task WithBackfillScopeAsync(Func<EfSchemaBackfillScope, Task> action, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        if (scope.ServiceProvider.GetService<TContext>() is { } context)
            await action(new EfSchemaBackfillScope(scope.ServiceProvider, context));
    }

    private async Task StopGateAsync()
    {
        if (!_stopping.IsCancellationRequested)
            await _stopping.CancelAsync();
        foreach (var loop in new[] { _gateLoop, _backfillLoop }.OfType<Task>())
        {
            try
            {
                await loop;
            }
            catch (OperationCanceledException)
            {
                // The loop was stopped mid-round; a shell or host that is disposing is not failing.
            }
        }
    }
}

/// <summary>
/// The provider a module context must be bound to before its migrations run, and what its
/// <c>[EfModule]</c> declares for after they have (ADR 0076 D8). Resolved once, at registration, so a
/// declaration this build cannot honour is refused while a host is still wiring itself up rather than
/// mid-migrate.
/// </summary>
/// <param name="HostComposed">
/// True for a module composed once on the host container and migrated before cluster membership joins, the membership
/// module itself. Its finalization gate cannot publish this host's report before reading its records, because the join
/// is that publish, so it is admitted without one and admitted again, publish first, once the member has joined.
/// </param>
public sealed record EfModuleMigration<TContext>(
    string ExpectedProviderName,
    string Module,
    string Provider,
    IReadOnlyList<IEfPostMigrationAction> PostMigration,
    bool HostComposed = false) where TContext : DbContext;

public static class EfModuleMigrationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the migrator for <typeparamref name="TContext"/>, the base context a module resolves, whose
    /// provider-derived registration must use <paramref name="provider"/>. Repeat calls keep one migrator.
    /// </summary>
    public static IServiceCollection AddEfModuleMigrations<TContext>(this IServiceCollection services, string provider)
        where TContext : DbContext =>
        services.AddEfModuleMigrations<TContext>(provider, onShellActivation: true);

    /// <summary>
    /// Registers the migrator for <typeparamref name="TContext"/> as a plain-host hosted service only, for a module
    /// composed once on the host container rather than by a shell feature. CShells copies every root registration into
    /// each shell's container, so the shell hook <see cref="AddEfModuleMigrations{TContext}"/> adds would migrate the
    /// module again on every shell activation, resolving its context from the shell's configuration rather than the
    /// host's. Here the host's start alone applies or validates it, before any later hosted service touches its store.
    /// </summary>
    public static IServiceCollection AddEfModuleHostMigrations<TContext>(this IServiceCollection services, string provider)
        where TContext : DbContext =>
        services.AddEfModuleMigrations<TContext>(provider, onShellActivation: false);

    private static IServiceCollection AddEfModuleMigrations<TContext>(this IServiceCollection services, string provider, bool onShellActivation)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        // The module's own [EfModule], when it has one: a test context or a context registered outside the
        // descriptor declares no post-migration action, so its absence is "nothing to audit", not a fault.
        var descriptor = EfModuleCatalog.Discover([typeof(TContext).Assembly])
            .SingleOrDefault(candidate => candidate.ContextType == typeof(TContext));
        var module = descriptor?.Name ?? typeof(TContext).Name;
        // A schema family whose upcaster chain has a gap, a duplicate, a branch or a cycle, or that does not end at its
        // current version, is refused here, while the host is wiring itself up (spec 180, FR-005).
        EfSchemaChain.EnsureSound(typeof(TContext).Assembly, module);
        var migration = new EfModuleMigration<TContext>(
            EfRelationalProviderBinding.ExpectedProviderName(provider),
            module,
            EfRelationalProviderBinding.Select(provider, module, "Sqlite", "SqlServer", "PostgreSql", "MySql"),
            EfPostMigrationActions.Create(module, descriptor?.PostMigration ?? []),
            HostComposed: !onShellActivation);
        // Registered first so the validator that reports a missing engine starts ahead of every migrator.
        services.AddEfProviderBindingValidation<TContext>(provider);
        foreach (var existing in services.Where(descriptor => descriptor.ServiceType == typeof(EfModuleMigration<TContext>)).ToArray())
            services.Remove(existing);
        services.AddSingleton(migration);
        services.AddOptions<EfMigrateOptions>();
        // One binding per container however many modules register a migrator; a host that configures the policy in
        // code after this call still wins, because IConfigureOptions run in registration order.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<EfMigrateOptions>, EfMigrateOptionsConfigurator>());
        // One registry of finalization gates per container, and the gate's timings from that container's configuration.
        services.TryAddSingleton<EfSchemaFinalizationGates>();
        services.AddOptions<EfSchemaFinalizationOptions>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<EfSchemaFinalizationOptions>, EfSchemaFinalizationOptionsConfigurator>());
        services.AddOptions<EfSchemaBackfillOptions>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<EfSchemaBackfillOptions>, EfSchemaBackfillOptionsConfigurator>());
        if (services.Any(descriptor => descriptor.ServiceType == typeof(EfModuleMigrator<TContext>)))
            return services;
        // CShells runs initializers by lifecycle phase, not registration order, and shell tasks and seeders
        // run at Start. Schema has to exist before any of them touches a store, so migrations run at Prepare.
        if (onShellActivation)
            services.AddShellInitializer<EfModuleMigrator<TContext>>(LifecyclePhase.Prepare, 0);
        // AddShellInitializer registers the initializer transiently; this last-wins registration makes the
        // shell and the hosted-service paths resolve one instance, exactly as AddEfProviderBindingValidation
        // does for the validator above. Ordering matters: a singleton added *before* that call is shadowed by
        // the transient descriptor it appends, which would hand each hook a migrator of its own.
        services.AddSingleton<EfModuleMigrator<TContext>>();
        // Plain hosts have no shell lifecycle; there the hosted service is what applies the migrations.
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<EfModuleMigrator<TContext>>());
        return services;
    }

}
