using Elsa.Events.Core.Contracts;
using Elsa.Locking.Core;
using Elsa.Persistence.EFCore.Contracts;
using Elsa.Persistence.EFCore.Events;
using Elsa.Persistence.EFCore.Extensions;
using Elsa.Persistence.EFCore.Handlers;
using Elsa.Persistence.EFCore.Sqlite;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Hosting.Services;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Persistence.EFCore.Commands;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Elsa.Workflows.Design.Persistence.EFCore.EntityHandlers;
using Elsa.Workflows.Design.Persistence.EFCore.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Workflows.Design.Tests.Infrastructure;

/// <summary>
/// SQLite-in-memory test host for the Workflows.Design.Persistence.EFCore commands. Builds
/// a real <see cref="WorkflowsDesignDbContext"/> + the real Draft commands
/// composed against an <see cref="InMemoryDistributedLockProvider"/> and a single
/// <see cref="CapturingEventPublisher"/> (for the synchronous <c>OnDraftValidating</c> gate
/// AND the FR-018/FR-018a lifecycle events + <c>OnDraftValidated</c>) so command behaviour can
/// be asserted end-to-end.
/// </summary>
internal sealed class WorkflowsDesignTestHost : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _services;

    public IServiceProvider Services => _services;
    public CapturingEventPublisher EventPublisher { get; }
    public InMemoryDistributedLockProvider LockProvider { get; }

    private WorkflowsDesignTestHost(
        SqliteConnection connection,
        ServiceProvider services,
        CapturingEventPublisher eventPublisher,
        InMemoryDistributedLockProvider lockProvider
    )
    {
        _connection = connection;
        _services = services;
        EventPublisher = eventPublisher;
        LockProvider = lockProvider;
    }

    public WorkflowsDesignDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<WorkflowsDesignDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new WorkflowsDesignDbContext(options, _services);
    }

    /// <summary>
    /// Idempotently seeds a <see cref="Persistence.Core.Entities.WorkflowDefinition"/> with the
    /// supplied id. Required before any test that calls <c>ICreateDraftCommand</c> —
    /// <c>WorkflowDefinitionDraft.WorkflowDefinitionId</c> is a non-null FK to this row.
    /// </summary>
    public async Task EnsureDefinition(string workflowDefinitionId, string? name = null)
    {
        await using var ctx = CreateContext();
        if (await ctx.WorkflowDefinitions.AnyAsync(d => d.Id == workflowDefinitionId))
            return;

        ctx.WorkflowDefinitions.Add(new Persistence.Core.Entities.WorkflowDefinition
        {
            Id = workflowDefinitionId,
            Name = name ?? workflowDefinitionId,
        });
        await ctx.SaveChangesAsync();
    }

    public static WorkflowsDesignTestHost Create()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var lockProvider = new InMemoryDistributedLockProvider();
        var eventPublisher = new CapturingEventPublisher();

        var services = new ServiceCollection();
        services.AddSingleton<ISystemClock, SystemClock>();
        services.AddSingleton<IIdentityGenerator, GuidIdentityGenerator>();
        services.AddScoped<IEntityModelCreatingHandler, SqliteEntityModelCreatingHandler>();
        services.AddSingleton<IActivityStructureHandler, TestActivityStructureHandler>();
        services.AddScoped<IActivityStructureService, DefaultActivityStructureService>();

        // Serializer + entity handlers — the Draft saving handler re-serializes
        // State → StateSource on every SaveChanges (and the loading handler hydrates on read).
        services.AddSingleton<JsonPayloadConverterRegistry>();
        services.AddSingleton<IPayloadSerializer, JsonPayloadSerializer>();
        services.AddScoped<IEntitySavingHandler<WorkflowsDesignDbContext, Persistence.Core.Entities.WorkflowDefinitionDraft>, WorkflowDefinitionDraftSavingHandler>();
        services.AddScoped<IEntityLoadingHandler<WorkflowsDesignDbContext, Persistence.Core.Entities.WorkflowDefinitionDraft>, WorkflowDefinitionDraftLoadingHandler>();
        services.AddScoped<IEntitySavingHandler<WorkflowsDesignDbContext, Persistence.Core.Entities.WorkflowDefinitionVersion>, WorkflowDefinitionVersionSavingHandler>();
        services.AddScoped<IEntityLoadingHandler<WorkflowsDesignDbContext, Persistence.Core.Entities.WorkflowDefinitionVersion>, WorkflowDefinitionVersionLoadingHandler>();

        // Logging (entity handlers need ILogger<>)
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));

        // Lock + event publisher stubs (capturing variant — see CapturingEventPublisher for
        // the bypass-the-pipeline rationale).
        services.AddSingleton<IDistributedLockProvider>(lockProvider);
        // One capturing instance registered under both delivery faces — the commands depend on
        // IInlineEventPublisher (gate + hydration) and IDeferredEventPublisher (notifications).
        services.AddSingleton<IInlineEventPublisher>(eventPublisher);
        services.AddSingleton<IDeferredEventPublisher>(eventPublisher);

        // The DbContext factory bridges to the in-memory connection.
        services.AddSingleton<IDbContextFactory<WorkflowsDesignDbContext>>(sp =>
            new TestDbContextFactory(sp.GetRequiredService<IServiceProvider>(), connection));

        // Named read ports over the closed query spec. CloneDraftFromVersionCommand reads the source
        // Version + layout through these; production registers them under UseQueries.
        services.AddScoped<Persistence.Core.Stores.IWorkflowDefinitionStore, Persistence.EFCore.Services.EFCoreWorkflowDefinitionStore>();
        services.AddScoped<Persistence.Core.Stores.IWorkflowDefinitionVersionStore, Persistence.EFCore.Services.EFCoreWorkflowDefinitionVersionStore>();
        services.AddScoped<Persistence.Core.Stores.IWorkflowDefinitionDraftStore, Persistence.EFCore.Services.EFCoreWorkflowDefinitionDraftStore>();
        services.AddScoped<Persistence.Core.Stores.IWorkflowDefinitionVersionLayoutStore, Persistence.EFCore.Services.EFCoreWorkflowDefinitionVersionLayoutStore>();

        // All command implementations.
        RegisterCommands(services);

        var provider = services.BuildServiceProvider();

        // Wire the single saving/loading aggregators onto the capturing publisher. Production
        // resolves these as IEventHandler<OnEntitySaving>/<OnEntityLoading> through the event
        // pipeline; the stub publisher bypasses that pipeline, so we subscribe the aggregators
        // explicitly. Each runs in its own scope (the typed IEntity*Handler<,> contributors are
        // scoped) and reflects over the registered handlers exactly as the real aggregator does —
        // this is what re-serialises State → StateSource on save and hydrates it back on load.
        eventPublisher.Subscribe<OnEntitySaving>(async e =>
        {
            using var scope = provider.CreateScope();
            await new ApplyEntitySavingHandlers(scope.ServiceProvider).Handle(e, CancellationToken.None);
        });
        eventPublisher.Subscribe<OnEntityLoading>(async e =>
        {
            using var scope = provider.CreateScope();
            await new ApplyEntityLoadingHandlers(scope.ServiceProvider).Handle(e, CancellationToken.None);
        });

        // Initialise schema.
        var optionsBuilder = new DbContextOptionsBuilder<WorkflowsDesignDbContext>().UseSqlite(connection);
        using (var ctx = new WorkflowsDesignDbContext(optionsBuilder.Options, provider))
            ctx.Database.EnsureCreated();

        return new WorkflowsDesignTestHost(connection, provider, eventPublisher, lockProvider);
    }

    private static void RegisterCommands(IServiceCollection services)
    {
        services
            .AddScoped<Persistence.Core.Contracts.IAddWorkflowDefinitionCommand, AddWorkflowDefinition>()
            .AddScoped<Persistence.Core.Contracts.ISaveWorkflowDefinitionCommand, SaveWorkflowDefinition>()
            .AddScoped<Persistence.Core.Contracts.ISubmitWorkflowDefinitionCommand, SubmitWorkflowDefinition>()
            // Lifecycle commands (NOT mutations — kept distinct, FR-003).
            .AddScoped<Persistence.Core.Contracts.ICreateDraftCommand, CreateDraft>()
            .AddScoped<Persistence.Core.Contracts.ICloneDraftFromVersionCommand, CloneDraftFromVersion>()
            .AddScoped<Persistence.Core.Contracts.IPromoteDraftToVersionCommand, PromoteDraftToVersion>()
            .AddScoped<Persistence.Core.Contracts.IDiscardDraftCommand, DiscardDraft>()
            // The single coarse Draft-mutation command (Unit 2). IDraftStateDiffEngine is deliberately
            // NOT registered here — mirroring production, per-diff mutation-event publication is retired
            // until an event-sourcing consumer exists, so the command no longer depends on the engine.
            .AddScoped<Persistence.Core.Contracts.IUpdateDraftCommand, UpdateDraft>();
    }

    public void Dispose()
    {
        _services.Dispose();
        _connection.Dispose();
    }

    private sealed class TestDbContextFactory(IServiceProvider services, SqliteConnection connection) : IDbContextFactory<WorkflowsDesignDbContext>
    {
        public WorkflowsDesignDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<WorkflowsDesignDbContext>().UseSqlite(connection).Options;
            return new WorkflowsDesignDbContext(options, services);
        }

        public Task<WorkflowsDesignDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }

    private sealed class GuidIdentityGenerator : IIdentityGenerator
    {
        public string Generate() => Guid.NewGuid().ToString("N");
    }
}
