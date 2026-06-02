using Elsa.Events.Core.Contracts;
using Elsa.Locking.Core;
using Elsa.Persistence.EFCore.Contracts;
using Elsa.Persistence.EFCore.Sqlite;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Services;
using Elsa.Serialization.Core;
using Elsa.Serialization.Services;
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
/// SQLite-in-memory test host for the Workflows.Design.Persistence.EFCore pipeline. Builds
/// a real <see cref="WorkflowsDesignDbContext"/> + a real <see cref="DraftMutationPipeline"/>
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
        services.AddSingleton<IEventPublisher>(eventPublisher);

        // The DbContext factory bridges to the in-memory connection.
        services.AddSingleton<IDbContextFactory<WorkflowsDesignDbContext>>(sp =>
            new TestDbContextFactory(sp.GetRequiredService<IServiceProvider>(), connection));

        // Pipeline + all 22 command implementations.
        services.AddScoped<DraftMutationPipeline>();
        RegisterCommands(services);

        var provider = services.BuildServiceProvider();

        // Initialise schema.
        var optionsBuilder = new DbContextOptionsBuilder<WorkflowsDesignDbContext>().UseSqlite(connection);
        using (var ctx = new WorkflowsDesignDbContext(optionsBuilder.Options, provider))
            ctx.Database.EnsureCreated();

        return new WorkflowsDesignTestHost(connection, provider, eventPublisher, lockProvider);
    }

    private static void RegisterCommands(IServiceCollection services)
    {
        services
            .AddScoped<Persistence.Core.Contracts.ICreateDraftCommand, CreateDraftCommand>()
            .AddScoped<Persistence.Core.Contracts.ICloneDraftFromVersionCommand, CloneDraftFromVersionCommand>()
            .AddScoped<Persistence.Core.Contracts.IDiscardDraftCommand, DiscardDraftCommand>()
            .AddScoped<Persistence.Core.Contracts.IAddActivityToDraftCommand, AddActivityToDraftCommand>()
            .AddScoped<Persistence.Core.Contracts.IRemoveActivityFromDraftCommand, RemoveActivityFromDraftCommand>()
            .AddScoped<Persistence.Core.Contracts.IMoveActivityInDraftCommand, MoveActivityInDraftCommand>()
            .AddScoped<Persistence.Core.Contracts.IAddActivityInputToDraftCommand, AddActivityInputToDraftCommand>()
            .AddScoped<Persistence.Core.Contracts.IUpdateActivityInputInDraftCommand, UpdateActivityInputInDraftCommand>()
            .AddScoped<Persistence.Core.Contracts.IRemoveActivityInputFromDraftCommand, RemoveActivityInputFromDraftCommand>()
            .AddScoped<Persistence.Core.Contracts.IAddActivityOutputToDraftCommand, AddActivityOutputToDraftCommand>()
            .AddScoped<Persistence.Core.Contracts.IUpdateActivityOutputInDraftCommand, UpdateActivityOutputInDraftCommand>()
            .AddScoped<Persistence.Core.Contracts.IRemoveActivityOutputFromDraftCommand, RemoveActivityOutputFromDraftCommand>()
            .AddScoped<Persistence.Core.Contracts.IAddConnectionToDraftCommand, AddConnectionToDraftCommand>()
            .AddScoped<Persistence.Core.Contracts.IRemoveConnectionFromDraftCommand, RemoveConnectionFromDraftCommand>()
            .AddScoped<Persistence.Core.Contracts.IDeclareVariableInDraftCommand, DeclareVariableInDraftCommand>()
            .AddScoped<Persistence.Core.Contracts.IUpdateVariableInDraftCommand, UpdateVariableInDraftCommand>()
            .AddScoped<Persistence.Core.Contracts.IRemoveVariableFromDraftCommand, RemoveVariableFromDraftCommand>()
            .AddScoped<Persistence.Core.Contracts.IAddWorkflowInputToDraftCommand, AddWorkflowInputToDraftCommand>()
            .AddScoped<Persistence.Core.Contracts.IUpdateWorkflowInputInDraftCommand, UpdateWorkflowInputInDraftCommand>()
            .AddScoped<Persistence.Core.Contracts.IRemoveWorkflowInputFromDraftCommand, RemoveWorkflowInputFromDraftCommand>()
            .AddScoped<Persistence.Core.Contracts.IAddWorkflowOutputToDraftCommand, AddWorkflowOutputToDraftCommand>()
            .AddScoped<Persistence.Core.Contracts.IUpdateWorkflowOutputInDraftCommand, UpdateWorkflowOutputInDraftCommand>()
            .AddScoped<Persistence.Core.Contracts.IRemoveWorkflowOutputFromDraftCommand, RemoveWorkflowOutputFromDraftCommand>();
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
