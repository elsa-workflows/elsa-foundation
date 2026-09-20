using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Locking.Core;
using Elsa.Persistence.EntityFramework;
using Elsa.Primitives.Contracts;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Services;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.DependencyInjection;

public static class PublishingEntityFrameworkCoreRegistration
{
    private static readonly EfModuleBinding Binding = EfModuleBinding.For(typeof(PublishingSnapshotReviewDbContext));

    /// <summary>
    /// The P01, P05 and P06 families and the reusable-activity publication commands. The commands write the P05
    /// receipt as their last phase, so they are selected together with it: a receipt read from one backend and
    /// written by another's command would silently lose idempotency.
    /// </summary>
    private static readonly LedgerFamily[] LedgerFamilies =
    [
        new(
            PublishingPersistenceFamilyBackend.PublicationRecords,
            PublishingPersistenceFamilyBackend.PublicationRecordContracts,
            services => AddStore<IPublicationRecordStore, EfPublicationRecordStore>(services)),
        new(
            PublishingPersistenceFamilyBackend.ActivityPublicationReceipts,
            PublishingPersistenceFamilyBackend.ActivityPublicationReceiptContracts,
            services => AddStore<IActivityPublicationReceiptStore, EfActivityPublicationReceiptStore>(services)),
        new(
            PublishingPersistenceFamilyBackend.ActivityDraftTestRuns,
            PublishingPersistenceFamilyBackend.ActivityDraftTestRunContracts,
            services => AddStore<IActivityDraftTestRunStore, EfActivityDraftTestRunStore>(services)),
        new(
            PublishingPersistenceFamilyBackend.ActivityPublicationCommands,
            [
                typeof(ICommitActivityPublicationCommand<ExecutableActivityTemplate, WorkflowExecutableSourceReference, ActivityPublicationReceipt>),
                typeof(ICommitSourceActivityPublicationCommand<ExecutableActivityTemplate, WorkflowExecutableSourceReference>)
            ],
            services =>
            {
                services.AddScoped<ICommitActivityPublicationCommand<ExecutableActivityTemplate, WorkflowExecutableSourceReference, ActivityPublicationReceipt>, EfActivityPublicationCommand>();
                services.AddScoped<ICommitSourceActivityPublicationCommand<ExecutableActivityTemplate, WorkflowExecutableSourceReference>, EfSourceActivityPublicationCommand>();
            }),
        new(
            PublishingPersistenceFamilyBackend.ActivityUpgradeMutation,
            PublishingPersistenceFamilyBackend.ActivityUpgradeMutationContracts,
            AddActivityUpgrade)
    ];

    /// <summary>
    /// The A12/A13 upgrade bridge. Discovery, the atomic mutation and the published-draft resolver are one
    /// object because an apply rechecks its own discovery inside the transaction it commits in; the rebuild
    /// coordinator converges the same derived view after an ordinary Design edit. Both reach the Activities
    /// Design and Workflows Design EF contexts directly, so they are constructed explicitly and refuse,
    /// with a message naming the missing lane, a host that did not put both catalogs on EF.
    /// </summary>
    private static void AddActivityUpgrade(IServiceCollection services)
    {
        services.AddScoped(provider => new EfActivityUpgradePlanStore(
            RequiredContext<ActivitiesDesignDbContext>(provider, "Activities Design"),
            RequiredContext<WorkflowsDesignDbContext>(provider, "Workflows Design"),
            provider.GetRequiredService<IPersistenceAccessContextAccessor>(),
            provider.GetRequiredService<IPayloadSerializer>(),
            provider.GetRequiredService<IActivityStructureService>(),
            provider.GetRequiredService<IActivityProviderRegistry>(),
            provider.GetRequiredService<ActivityContractAuthoringValidator>(),
            provider.GetServices<IActivityProviderReferenceRewriter>(),
            provider.GetRequiredService<IIdentityGenerator>(),
            provider.GetRequiredService<IDistributedLockProvider>(),
            provider.GetService<TimeProvider>()));
        services.AddScoped<IActivityUpgradeDiscoverySource>(provider => provider.GetRequiredService<EfActivityUpgradePlanStore>());
        services.AddScoped<IActivityUpgradePlanMutationStore>(provider => provider.GetRequiredService<EfActivityUpgradePlanStore>());
        services.AddScoped<IActivityUpgradePublishedDraftResolver>(provider => provider.GetRequiredService<EfActivityUpgradePlanStore>());
        services.AddScoped<IActivityDependencyProjectionRebuildCoordinator>(provider => new EfActivityDependencyProjectionRebuildCoordinator(
            RequiredContext<ActivitiesDesignDbContext>(provider, "Activities Design"),
            RequiredContext<WorkflowsDesignDbContext>(provider, "Workflows Design"),
            provider.GetRequiredService<IPersistenceAccessContextAccessor>(),
            provider.GetRequiredService<IPayloadSerializer>(),
            provider.GetRequiredService<IActivityDefinitionVersionPublicationStore>(),
            provider.GetRequiredService<IActivityTemplateDependencyDiscovererRegistry>(),
            provider.GetRequiredService<IActivityStructureService>(),
            provider.GetRequiredService<IActivityDependencyProjectionRebuilder>(),
            provider.GetRequiredService<IIdentityGenerator>(),
            provider.GetService<TimeProvider>() ?? TimeProvider.System));
    }

    private static TContext RequiredContext<TContext>(IServiceProvider provider, string lane) where TContext : DbContext =>
        provider.GetService<TContext>()
        ?? throw new InvalidOperationException(
            $"Publishing EF activity-upgrade persistence requires the EF {lane} module; select it before composing the Publishing EF feature.");

    public static IServiceCollection AddPublishingEntityFrameworkCore(
        this IServiceCollection services,
        PublishingEntityFrameworkCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        var snapshot = services.ToArray();
        try
        {
            _ = EfRelationalProviderBinding.ExpectedProviderName(options.Provider);
            var reviewBackend = PublicationSnapshotReviewStoreBackend.Find(services);
            var policyProjectionBackend = PublicationPolicyProjectionStoreBackend.Find(services);
            var reviewIsEntityFramework = reviewBackend?.Name == PublicationSnapshotReviewStoreBackend.EntityFramework;

            if (reviewIsEntityFramework)
                reviewBackend!.EnsureOwnsRegisteredContract(services);
            if (policyProjectionBackend?.Name == PublicationPolicyProjectionStoreBackend.EntityFramework)
            {
                policyProjectionBackend.EnsureOwnsRegisteredContracts(services);
                if (!reviewIsEntityFramework)
                    throw new InvalidOperationException("Publication policy/projection-intent EF persistence requires the P04 snapshot-review EF context to be selected as well.");
            }
            foreach (var family in LedgerFamilies)
            {
                if (PublishingPersistenceFamilyBackend.Find(services, family.Name) is not { Name: PublishingPersistenceFamilyBackend.EntityFramework } ledgerBackend)
                    continue;
                ledgerBackend.EnsureOwnsRegisteredContracts(services);
                if (!reviewIsEntityFramework)
                    throw new InvalidOperationException($"Publishing EF persistence for '{family.Name}' requires the P04 snapshot-review EF context to be selected as well.");
            }

            var existingOptions = services.Select(service => service.ImplementationInstance)
                .OfType<PublishingEntityFrameworkCoreOptions>()
                .SingleOrDefault();
            if (existingOptions is not null && (!reviewIsEntityFramework || !OptionsEqual(existingOptions, options)))
                throw new InvalidOperationException("Publishing EF persistence is already registered with different provider options.");

            if (!reviewIsEntityFramework)
            {
                if (reviewBackend is not null)
                    reviewBackend.RemoveOwnedArtifacts(services);
                else
                    PublicationSnapshotReviewStoreBackend.EnsureNoUnownedRegistrations(services);
                AddSnapshotReview(services, options);
            }
            if (policyProjectionBackend?.Name != PublicationPolicyProjectionStoreBackend.EntityFramework)
            {
                if (policyProjectionBackend is not null)
                    policyProjectionBackend.RemoveOwnedArtifacts(services);
                else
                    PublicationPolicyProjectionStoreBackend.EnsureNoUnownedRegistrations(services);
                AddPolicyAndProjectionIntent(services);
            }

            foreach (var family in LedgerFamilies)
            {
                // Idempotent for an EF owner; replaces the in-memory owner; refuses a foreign one.
                if (!PublishingPersistenceFamilyBackend.PrepareSelection(services, family.Name, family.Contracts, PublishingPersistenceFamilyBackend.EntityFramework))
                    continue;
                var firstAdded = services.Count;
                family.Register(services);
                PublishingPersistenceFamilyBackend.RegisterAdded(
                    services,
                    family.Name,
                    PublishingPersistenceFamilyBackend.EntityFramework,
                    family.Contracts,
                    firstAdded);
            }

            return services;
        }
        catch
        {
            services.Clear();
            foreach (var descriptor in snapshot)
                services.Add(descriptor);
            throw;
        }
    }

    private static void AddSnapshotReview(IServiceCollection services, PublishingEntityFrameworkCoreOptions options)
    {
        var configured = new PublishingEntityFrameworkCoreOptions
        {
            Provider = options.Provider,
            ConnectionString = options.ConnectionString,
            ConnectionName = options.ConnectionName,
            Schema = options.Schema,
            Pooling = options.Pooling
        };
        var optionsDescriptor = ServiceDescriptor.Singleton(configured);
        services.Add(optionsDescriptor);
        var reviewOwned = new List<ServiceDescriptor> { optionsDescriptor };
        reviewOwned.AddRange(AddContext(services, configured));

        var firstStore = services.Count;
        AddStore<IPublicationSnapshotReviewStore, EfPublicationSnapshotReviewStore>(services);
        reviewOwned.AddRange(services.Skip(firstStore));
        PublicationSnapshotReviewStoreBackend.Register(
            services,
            new PublicationSnapshotReviewStoreBackend(PublicationSnapshotReviewStoreBackend.EntityFramework, reviewOwned));
    }

    private static void AddPolicyAndProjectionIntent(IServiceCollection services)
    {
        var firstStore = services.Count;
        AddStore<IPublicationPolicyStore, EfPublicationPolicyStore>(services);
        AddStore<IPublicationProjectionIntentStore, EfPublicationProjectionIntentStore>(services);
        PublicationPolicyProjectionStoreBackend.Register(
            services,
            new PublicationPolicyProjectionStoreBackend(
                PublicationPolicyProjectionStoreBackend.EntityFramework,
                services.Skip(firstStore).ToArray()));
    }

    /// <summary>Registers the scoped EF store and resolves its contract through it.</summary>
    private static void AddStore<TContract, TStore>(IServiceCollection services)
        where TContract : class
        where TStore : class, TContract
    {
        services.Add(new ServiceDescriptor(typeof(TStore), typeof(TStore), ServiceLifetime.Scoped));
        services.Add(ServiceDescriptor.Scoped<TContract>(provider => provider.GetRequiredService<TStore>()));
    }

    private static IReadOnlyCollection<ServiceDescriptor> AddContext(
        IServiceCollection services,
        PublishingEntityFrameworkCoreOptions options)
    {
        var addContext = Binding.Select<Func<IServiceCollection, PublishingEntityFrameworkCoreOptions, IReadOnlyCollection<ServiceDescriptor>>>(
            options.Provider,
            AddContext<PublishingSnapshotReviewSqliteDbContext>,
            AddContext<PublishingSnapshotReviewSqlServerDbContext>,
            AddContext<PublishingSnapshotReviewPostgreSqlDbContext>,
            AddContext<PublishingSnapshotReviewMySqlDbContext>);
        return addContext(services, options);
    }

    private static IReadOnlyCollection<ServiceDescriptor> AddContext<TContext>(
        IServiceCollection services,
        PublishingEntityFrameworkCoreOptions options)
        where TContext : PublishingSnapshotReviewDbContext
    {
        var start = services.Count;
        Binding.AddContext<TContext>(services, options.Pooling, (provider, builder) =>
            Binding.Apply(builder, provider, options.Provider, options.ConnectionString, options.ConnectionName, options.Schema));
        services.AddScoped<PublishingSnapshotReviewDbContext>(provider => provider.GetRequiredService<TContext>());
        return services.Skip(start).ToArray();
    }

    private static bool OptionsEqual(PublishingEntityFrameworkCoreOptions left, PublishingEntityFrameworkCoreOptions right) =>
        StringComparer.Ordinal.Equals(EfRelationalProviderBinding.Normalize(left.Provider), EfRelationalProviderBinding.Normalize(right.Provider)) &&
        left.ConnectionString == right.ConnectionString && left.ConnectionName == right.ConnectionName &&
        left.Schema == right.Schema && left.Pooling == right.Pooling;

    private sealed record LedgerFamily(string Name, IReadOnlyCollection<Type> Contracts, Action<IServiceCollection> Register);
}

public sealed class PublishingEntityFrameworkCoreOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }

    /// <summary>
    /// Optional database schema for this module's tables and its own migrations history table. Falls back to
    /// <see cref="EfSchema.ConfigurationKey"/>, then to the provider's own default. Ignored on SQLite and refused
    /// on MySQL, where a schema is a database.
    /// </summary>
    public string? Schema { get; set; }

    /// <summary>Reuse contexts from a pool instead of constructing one per scope.</summary>
    public bool Pooling { get; set; }
}
