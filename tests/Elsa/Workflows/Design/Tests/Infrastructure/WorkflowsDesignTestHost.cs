using CShells.Lifecycle;
using Elsa.Activities.Design.Reconciliation;
using Elsa.Events;
using Elsa.Events.Core.Contracts;
using Elsa.Locking.Core;
using Elsa.Persistence.Core.Design;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Sqlite.Unified.DependencyInjection;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Hosting.Services;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Validations;
using Groundwork.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Workflows.Design.Tests.Infrastructure;

/// <summary>
/// SQLite Groundwork test host for the workflows-design commands and stores. Composes the same
/// production SQLite Groundwork unified persistence the design-conformance SQLite fixture uses
/// (schema applied in-process to a temp file database, then production store/command registrations),
/// wired to an <see cref="InMemoryDistributedLockProvider"/> and a single
/// <see cref="CapturingEventPublisher"/> (for the synchronous <c>DraftValidating</c> gate AND the
/// FR-018/FR-018a lifecycle events + <c>DraftValidated</c>) so command behaviour can be asserted
/// end-to-end without Entity Framework.
/// </summary>
/// <remarks>
/// Spec 093 T072 rehost: this host previously composed the workflows-design Entity Framework context
/// over an in-memory SQLite connection. It now composes the real Groundwork document store so the
/// surviving provider-neutral behavioural tests exercise the shipping persistence path. Raw context
/// reads are replaced by <see cref="GetDraftAsync"/>/<see cref="GetVersionAsync"/> over the public read
/// stores, which hydrate the authored <c>State</c> exactly as an application consumer sees it.
/// </remarks>
internal sealed class WorkflowsDesignTestHost : IDisposable
{
    private readonly string _directory;
    private readonly ServiceProvider _services;

    public IServiceProvider Services => _services;
    public static DesignOperationKey TestOperationKey { get; } = new("workflow-design-test");
    public CapturingEventPublisher EventPublisher { get; }
    public InMemoryDistributedLockProvider LockProvider { get; }

    private WorkflowsDesignTestHost(
        string directory,
        ServiceProvider services,
        CapturingEventPublisher eventPublisher,
        InMemoryDistributedLockProvider lockProvider)
    {
        _directory = directory;
        _services = services;
        EventPublisher = eventPublisher;
        LockProvider = lockProvider;
    }

    public static async Task<WorkflowsDesignTestHost> CreateAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.Join(Path.GetTempPath(), $"elsa-workflows-design-groundwork-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var connectionString = $"Data Source={Path.Join(directory, "design.db")}";

        var lockProvider = new InMemoryDistributedLockProvider();
        var eventPublisher = new CapturingEventPublisher();

        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<ISystemClock, SystemClock>();
        services.AddSingleton<IIdentityGenerator, GuidIdentityGenerator>();

        // Real activity structure projection (flattening) — the command-driven tests depend on it.
        services.AddSingleton<IActivityStructureHandler, TestActivityStructureHandler>();

        // Serializer used by the Groundwork stores to (de)serialize the authored WorkflowDefinitionState.
        services.AddSingleton<JsonPayloadConverterRegistry>();
        services.AddSingleton<IPayloadSerializer, JsonPayloadSerializer>();

        // The design store families compose their capability declarations against the events + design
        // feature services, so mirror the design-conformance fixture's feature set.
        new EventsFeature().ConfigureServices(services);

        // Production SQLite Groundwork unified persistence: registers the workflows-design (and
        // activities-design) stores + commands over one physical document store. autoApplyOnStartup
        // applies the pending schema in-process during IShellInitializer, so no external CLI is needed.
        services.AddGroundworkSqliteUnifiedPersistence(connectionString, autoApplyOnStartup: true);
        // The partially cut-over feature graph also declares public-v2 units. Give those units an
        // explicit test provider so initialization preserves the shipping fail-fast invariant.
        services.AddGroundworkStorageProviderConnection(_ =>
            new InMemoryProviderFactory().Create($"workflows-design-tests:{Guid.NewGuid():N}"));
        new WorkflowDesignValidationsFeature().ConfigureServices(services);
        new ActivitiesDesignReconciliationFeature().ConfigureServices(services);

        // Lock + event publisher + real structure service — registered AFTER the unified/feature
        // composition so the capturing publisher wins as the single IInlineEventPublisher/
        // IDeferredEventPublisher the commands resolve, and the real flattening structure service is used.
        services.AddSingleton<IDistributedLockProvider>(lockProvider);
        services.AddSingleton<IInlineEventPublisher>(eventPublisher);
        services.AddSingleton<IDeferredEventPublisher>(eventPublisher);
        services.AddScoped<IActivityStructureService, DefaultActivityStructureService>();

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        // The Groundwork storage-composition factory publishes GroundworkStorageComposing through the
        // registered IInlineEventPublisher to gather each manifest source's schema declaration. The
        // capturing publisher only records events, so route this one composition event to the real
        // GroundworkStorageCompositionHandler (the production pipeline would dispatch it the same way).
        eventPublisher.Subscribe<GroundworkStorageComposing>(async composing =>
        {
            using var scope = provider.CreateScope();
            await scope.ServiceProvider
                .GetRequiredService<GroundworkStorageCompositionHandler>()
                .Handle(composing, CancellationToken.None);
        });

        foreach (var initializer in provider.GetServices<IShellInitializer>())
            await initializer.InitializeAsync(cancellationToken);

        return new WorkflowsDesignTestHost(directory, provider, eventPublisher, lockProvider);
    }

    /// <summary>
    /// Idempotently materialises a <see cref="WorkflowDefinition"/> with the supplied id. Required
    /// before any test that calls <c>ICreateDraftCommand</c>.
    /// </summary>
    public async Task EnsureDefinition(string workflowDefinitionId, string? name = null)
    {
        using var scope = _services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>();
        if (await store.FindByIdAsync(workflowDefinitionId) is not null)
            return;

        var materialize = scope.ServiceProvider.GetRequiredService<IMaterializeWorkflowDefinitionCommand>();
        await materialize.Execute(
            TestOperationKey,
            new WorkflowDefinition { Id = workflowDefinitionId, Name = name ?? workflowDefinitionId });
    }

    /// <summary>Loads a persisted draft (with hydrated <c>State</c>) through the public read store.</summary>
    public async Task<WorkflowDefinitionDraft?> GetDraftAsync(string draftId)
    {
        using var scope = _services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionDraftStore>()
            .FindByIdAsync(draftId);
    }

    /// <summary>Loads a persisted version (with hydrated <c>State</c>) through the public read store.</summary>
    public async Task<WorkflowDefinitionVersion?> GetVersionAsync(string versionId)
    {
        using var scope = _services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionVersionStore>()
            .FindByIdAsync(versionId);
    }

    public void Dispose()
    {
        // The Groundwork store session source is IAsyncDisposable-only, so drain the provider through
        // the async path (tests keep a synchronous `using var host`).
        _services.DisposeAsync().AsTask().GetAwaiter().GetResult();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A uniquely named temp directory is harmless if SQLite releases a sidecar late.
        }
    }

    private sealed class GuidIdentityGenerator : IIdentityGenerator
    {
        public string Generate() => Guid.NewGuid().ToString("N");
    }
}
