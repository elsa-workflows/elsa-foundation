using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Services;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Publishing.Persistence.Groundwork.DependencyInjection;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests;

public sealed class EfPublishingLedgerRegistrationTests
{
    private static readonly PublishingEntityFrameworkCoreOptions Options = new() { ConnectionString = "Data Source=:memory:" };

    private static readonly string[] Families =
    [
        PublishingPersistenceFamilyBackend.PublicationRecords,
        PublishingPersistenceFamilyBackend.ActivityPublicationReceipts,
        PublishingPersistenceFamilyBackend.ActivityDraftTestRuns,
        PublishingPersistenceFamilyBackend.ActivityPublicationCommands
    ];

    [Theory]
    [InlineData("ef-gw-ef")]
    [InlineData("gw-ef-gw")]
    public void An_explicit_EF_selection_owns_every_ledger_family_whichever_order_Groundwork_is_composed_in(string order)
    {
        var services = ComposeInMemoryDefaults();
        foreach (var step in order.Split('-'))
        {
            if (step == "ef")
                services.AddPublishingEntityFrameworkCore(Options);
            else
                services.AddGroundworkPublishingStores().AddGroundworkActivityPublicationCommands();
        }

        Assert.All(Families, family => Assert.Equal(PublishingPersistenceFamilyBackend.EntityFramework, PublishingPersistenceFamilyBackend.Find(services, family)!.Name));
        AssertSingleImplementation<IPublicationRecordStore, EfPublicationRecordStore>(services);
        AssertSingleImplementation<IActivityPublicationReceiptStore, EfActivityPublicationReceiptStore>(services);
        AssertSingleImplementation<IActivityDraftTestRunStore, EfActivityDraftTestRunStore>(services);
        Assert.Equal(typeof(EfActivityPublicationCommand), Assert.Single(services, service =>
            service.ServiceType == typeof(ICommitActivityPublicationCommand<ExecutableActivityTemplate, WorkflowExecutableSourceReference, ActivityPublicationReceipt>)).ImplementationType);
        Assert.Equal(typeof(EfSourceActivityPublicationCommand), Assert.Single(services, service =>
            service.ServiceType == typeof(ICommitSourceActivityPublicationCommand<ExecutableActivityTemplate, WorkflowExecutableSourceReference>)).ImplementationType);
        Assert.DoesNotContain(services, service => service.ImplementationType == typeof(InMemoryPublicationRecordStore));
    }

    [Fact]
    public void Groundwork_replaces_the_in_memory_defaults_and_EF_then_replaces_Groundwork()
    {
        var services = ComposeInMemoryDefaults();
        services.AddGroundworkPublishingStores().AddGroundworkActivityPublicationCommands();
        Assert.All(Families, family => Assert.Equal(PublishingPersistenceFamilyBackend.Groundwork, PublishingPersistenceFamilyBackend.Find(services, family)!.Name));
        var groundworkCount = services.Count;
        services.AddGroundworkPublishingStores().AddGroundworkActivityPublicationCommands();
        Assert.Equal(groundworkCount, services.Count);

        services.AddPublishingEntityFrameworkCore(Options);
        var entityFrameworkCount = services.Count;
        services.AddPublishingEntityFrameworkCore(Options);

        Assert.Equal(entityFrameworkCount, services.Count);
        Assert.All(Families, family => Assert.Equal(PublishingPersistenceFamilyBackend.EntityFramework, PublishingPersistenceFamilyBackend.Find(services, family)!.Name));
        Assert.DoesNotContain(services, service => service.ImplementationType?.Namespace?.EndsWith(".Groundwork.Stores", StringComparison.Ordinal) == true &&
                                                   service.ServiceType == typeof(IActivityDraftTestRunStore));
    }

    public static TheoryData<Type> ForeignContracts => new()
    {
        typeof(IPublicationRecordStore),
        typeof(IActivityPublicationReceiptStore),
        typeof(IActivityDraftTestRunStore),
        typeof(ICommitActivityPublicationCommand<ExecutableActivityTemplate, WorkflowExecutableSourceReference, ActivityPublicationReceipt>)
    };

    [Theory]
    [MemberData(nameof(ForeignContracts))]
    public void A_foreign_ledger_registration_is_refused_by_both_backends_without_partial_mutation(Type contract)
    {
        var services = new ServiceCollection();
        services.AddScoped(contract, _ => throw new NotSupportedException("A host-provided implementation."));
        new WorkflowsPublishingFeature().ConfigureServices(services);
        var before = services.ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddPublishingEntityFrameworkCore(Options));
        Assert.Equal(before, services);
        Assert.Throws<InvalidOperationException>(() => contract.IsGenericType
            ? services.AddGroundworkActivityPublicationCommands()
            : services.AddGroundworkPublishingStores());
        Assert.Equal(before, services);
    }

    [Fact]
    public void An_all_EF_composition_resolves_the_ledger_stores_and_the_ordered_publication_commands()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IPersistenceAccessContextAccessor>(TestAccess.Scoped("default"));
        services.Configure<RuntimeRecoveryContinuationOptions>(options => options.SigningKey = "publishing-ef-test-recovery-signing-key-32-bytes");
        // The Runtime and Activities Design backends are selected before the publishing engine adds its
        // in-memory fallbacks, as the feature dependency order composes them.
        services.AddActivitiesDesignEntityFrameworkCore(new ActivitiesDesignEntityFrameworkCoreOptions { ConnectionString = "Data Source=:memory:" });
        services.AddRuntimeArtifactsEntityFrameworkCore(new RuntimeArtifactsEntityFrameworkCoreOptions { ConnectionString = "Data Source=:memory:" });
        new WorkflowsPublishingFeature().ConfigureServices(services);
        services.AddPublishingEntityFrameworkCore(Options);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        Assert.IsType<EfPublicationRecordStore>(scope.ServiceProvider.GetRequiredService<IPublicationRecordStore>());
        Assert.IsType<EfActivityPublicationReceiptStore>(scope.ServiceProvider.GetRequiredService<IActivityPublicationReceiptStore>());
        Assert.IsType<EfActivityDraftTestRunStore>(scope.ServiceProvider.GetRequiredService<IActivityDraftTestRunStore>());
        Assert.IsType<EfActivityPublicationCommand>(scope.ServiceProvider.GetRequiredService<
            ICommitActivityPublicationCommand<ExecutableActivityTemplate, WorkflowExecutableSourceReference, ActivityPublicationReceipt>>());
        Assert.IsType<EfSourceActivityPublicationCommand>(scope.ServiceProvider.GetRequiredService<
            ICommitSourceActivityPublicationCommand<ExecutableActivityTemplate, WorkflowExecutableSourceReference>>());
    }

    [Fact]
    public void The_EF_commands_refuse_to_resolve_when_Activities_Design_is_not_on_EF()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IPersistenceAccessContextAccessor>(TestAccess.Scoped("default"));
        services.Configure<RuntimeRecoveryContinuationOptions>(options => options.SigningKey = "publishing-ef-test-recovery-signing-key-32-bytes");
        services.AddRuntimeArtifactsEntityFrameworkCore(new RuntimeArtifactsEntityFrameworkCoreOptions { ConnectionString = "Data Source=:memory:" });
        new WorkflowsPublishingFeature().ConfigureServices(services);
        services.AddPublishingEntityFrameworkCore(Options);
        services.AddScoped<Elsa.Activities.Design.Persistence.Core.Stores.IActivityDefinitionVersionPublicationStore, NonEfPublicationStore>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var failure = Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<
            ICommitActivityPublicationCommand<ExecutableActivityTemplate, WorkflowExecutableSourceReference, ActivityPublicationReceipt>>());
        Assert.Contains("EF Activities Design", failure.Message, StringComparison.Ordinal);
    }

    private static ServiceCollection ComposeInMemoryDefaults()
    {
        var services = new ServiceCollection();
        new WorkflowsPublishingFeature().ConfigureServices(services);
        // The Publishing API feature adds the in-memory test-run default through the same ownership helper.
        PublishingPersistenceFamilyBackend.TryAddInMemory<IActivityDraftTestRunStore, DefaultTestRunStore>(
            services,
            PublishingPersistenceFamilyBackend.ActivityDraftTestRuns,
            PublishingPersistenceFamilyBackend.ActivityDraftTestRunContracts);
        return services;
    }

    private static void AssertSingleImplementation<TContract, TStore>(IServiceCollection services)
    {
        Assert.Single(services, service => service.ServiceType == typeof(TContract));
        Assert.Single(services, service => service.ServiceType == typeof(TStore));
    }

    private sealed class DefaultTestRunStore : IActivityDraftTestRunStore
    {
        public ValueTask<ActivityDraftTestRunCreateResult> TryCreateAsync(ActivityDraftTestRunReceipt receipt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<ActivityDraftTestRunReceipt?> FindAsync(string testRunId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<ActivityDraftTestRunReceipt?> FindByIdempotencyKeyAsync(string operationScope, string draftId, string idempotencyKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> TryUpdateAsync(ActivityDraftTestRunReceipt receipt, long expectedRevision, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<int> DeleteExpiredAsync(DateTimeOffset asOf, int limit, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NonEfPublicationStore : Elsa.Activities.Design.Persistence.Core.Stores.IActivityDefinitionVersionPublicationStore
    {
        public Task<Elsa.Activities.Design.Persistence.Core.Entities.ActivityDefinitionVersionPublication?> FindAsync(string definitionVersionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Elsa.Activities.Design.Persistence.Core.Entities.ActivityDefinitionVersionPublication>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
