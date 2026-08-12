using Elsa.Persistence.Groundwork.DesignConformance.Tests;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.DesignConformance.MongoDb.Tests;

/// <summary>Executes the provider-neutral workflow-design contract on the composed MongoDB target.</summary>
[Collection(MongoDbDesignProviderCollection.Name)]
public sealed class MongoDbWorkflowDesignContractSuite(MongoDbDesignProviderFixture container)
    : WorkflowDesignContractSuite
{
    private readonly GroundworkBaselineTelemetry _telemetry = new();

    protected override async Task<IDesignPersistenceContractFixture> CreateFixtureAsync(
        CancellationToken cancellationToken = default) =>
        await MongoDbDesignPersistenceContractFixture.CreateAsync(container, _telemetry, cancellationToken);
}

/// <summary>Executes the provider-neutral activity-design contract on the composed MongoDB target.</summary>
[Collection(MongoDbDesignProviderCollection.Name)]
public sealed class MongoDbActivityDesignContractSuite(MongoDbDesignProviderFixture container)
    : ActivityDesignContractSuite
{
    private readonly GroundworkBaselineTelemetry _telemetry = new();

    protected override async Task<IDesignPersistenceContractFixture> CreateFixtureAsync(
        CancellationToken cancellationToken = default) =>
        await MongoDbDesignPersistenceContractFixture.CreateAsync(container, _telemetry, cancellationToken);
}

/// <summary>
/// Executes the target-profile atomicity contract on the composed MongoDB target (a transaction-capable
/// replica set supports <c>CrossUnitAtomic</c>), and isolates the composed lifecycle-event durability
/// proof as its own test.
/// </summary>
[Collection(MongoDbDesignProviderCollection.Name)]
public sealed class MongoDbAtomicityContractSuite(MongoDbDesignProviderFixture container)
    : DesignAtomicityContractSuite
{
    private readonly GroundworkBaselineTelemetry _telemetry = new();

    protected override DesignPersistenceContractProfile ContractProfile => DesignPersistenceContractProfiles.Target;

    protected override async Task<IDesignPersistenceContractFixture> CreateFixtureAsync(
        CancellationToken cancellationToken = default) =>
        await MongoDbDesignPersistenceContractFixture.CreateAsync(container, _telemetry, cancellationToken);

    [Fact]
    public async Task Public_create_draft_command_publishes_DraftCreated_only_after_the_draft_is_durable()
    {
        await using var fixture = await MongoDbDesignPersistenceContractFixture.CreateAsync(container, _telemetry);
        await fixture.ValidateReadinessAsync();

        using var scope = fixture.CreateScope(DesignPersistenceFixtureData.ScopeA);
        var services = scope.ServiceProvider;
        await services.GetRequiredService<IAddWorkflowDefinitionCommand>().Execute(
            DesignPersistenceFixtureData.OperationKey("lifecycle-event-create-definition"),
            DesignPersistenceFixtureData.WorkflowDefinition(),
            DesignPersistenceFixtureData.WorkflowDraft(),
            CancellationToken.None);

        fixture.ClearObservedEvents();
        var draftId = await services.GetRequiredService<ICreateDraftCommand>().Execute(
            DesignPersistenceFixtureData.OperationKey("lifecycle-event-create-draft"),
            DesignPersistenceFixtureData.WorkflowDefinitionId,
            cancellationToken: CancellationToken.None);

        using var eventTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var published = await fixture.WaitForPublishedDraftCreatedAsync(draftId, eventTimeout.Token);
        Assert.Equal(draftId, published.DraftId);
        Assert.Equal(DesignPersistenceFixtureData.WorkflowDefinitionId, published.WorkflowDefinitionId);
        Assert.Null(published.SourceVersionId);
        Assert.NotNull(await services.GetRequiredService<IWorkflowDefinitionDraftStore>().FindWithLayoutByIdAsync(draftId));
    }
}

/// <summary>Executes the target-profile isolation and restart contract on the composed MongoDB target.</summary>
[Collection(MongoDbDesignProviderCollection.Name)]
public sealed class MongoDbIsolationAndRestartContractSuite(MongoDbDesignProviderFixture container)
    : DesignIsolationAndRestartContractSuite
{
    private readonly GroundworkBaselineTelemetry _telemetry = new();

    protected override DesignPersistenceContractProfile ContractProfile => DesignPersistenceContractProfiles.Target;

    protected override async Task<IDesignPersistenceContractFixture> CreateFixtureAsync(
        CancellationToken cancellationToken = default) =>
        await MongoDbDesignPersistenceContractFixture.CreateAsync(container, _telemetry, cancellationToken);
}

/// <summary>Executes the T037 workflow query-shape parity contract on the composed MongoDB target.</summary>
[Collection(MongoDbDesignProviderCollection.Name)]
public sealed class MongoDbWorkflowDesignQueryContractSuite(MongoDbDesignProviderFixture container)
    : WorkflowDesignQueryContractSuite
{
    private readonly GroundworkBaselineTelemetry _telemetry = new();

    protected override async Task<IDesignPersistenceContractFixture> CreateFixtureAsync(
        CancellationToken cancellationToken = default) =>
        await MongoDbDesignPersistenceContractFixture.CreateAsync(container, _telemetry, cancellationToken);
}

/// <summary>Executes the T038 activity query-shape parity contract on the composed MongoDB target.</summary>
[Collection(MongoDbDesignProviderCollection.Name)]
public sealed class MongoDbActivityDesignQueryContractSuite(MongoDbDesignProviderFixture container)
    : ActivityDesignQueryContractSuite
{
    private readonly GroundworkBaselineTelemetry _telemetry = new();

    protected override async Task<IDesignPersistenceContractFixture> CreateFixtureAsync(
        CancellationToken cancellationToken = default) =>
        await MongoDbDesignPersistenceContractFixture.CreateAsync(container, _telemetry, cancellationToken);
}

/// <summary>Executes the T039 scale/batching contract on the composed MongoDB target.</summary>
[Collection(MongoDbDesignProviderCollection.Name)]
public sealed class MongoDbDesignQueryScaleContractSuite(MongoDbDesignProviderFixture container)
    : DesignQueryScaleContractSuite
{
    private readonly GroundworkBaselineTelemetry _telemetry = new();

    protected override DesignPersistenceContractProfile ContractProfile => DesignPersistenceContractProfiles.Target;

    protected override async Task<IDesignPersistenceContractFixture> CreateFixtureAsync(
        CancellationToken cancellationToken = default) =>
        await MongoDbDesignPersistenceContractFixture.CreateAsync(container, _telemetry, cancellationToken);
}
