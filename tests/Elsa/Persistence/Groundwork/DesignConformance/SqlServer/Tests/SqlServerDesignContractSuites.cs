using Elsa.Persistence.Groundwork.DesignConformance.Tests;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.DesignConformance.SqlServer.Tests;

/// <summary>Executes the provider-neutral workflow-design contract on the composed SQL Server target.</summary>
[Collection(SqlServerDesignProviderCollection.Name)]
public sealed class SqlServerWorkflowDesignContractSuite(SqlServerDesignProviderFixture container)
    : WorkflowDesignContractSuite
{
    private readonly GroundworkBaselineTelemetry _telemetry = new();

    protected override async Task<IDesignPersistenceContractFixture> CreateFixtureAsync(
        CancellationToken cancellationToken = default) =>
        await SqlServerDesignPersistenceContractFixture.CreateAsync(container, _telemetry, cancellationToken);
}

/// <summary>Executes the provider-neutral activity-design contract on the composed SQL Server target.</summary>
[Collection(SqlServerDesignProviderCollection.Name)]
public sealed class SqlServerActivityDesignContractSuite(SqlServerDesignProviderFixture container)
    : ActivityDesignContractSuite
{
    private readonly GroundworkBaselineTelemetry _telemetry = new();

    protected override async Task<IDesignPersistenceContractFixture> CreateFixtureAsync(
        CancellationToken cancellationToken = default) =>
        await SqlServerDesignPersistenceContractFixture.CreateAsync(container, _telemetry, cancellationToken);
}

/// <summary>
/// Executes the target-profile atomicity contract on the composed SQL Server target (SQL Server supports
/// <c>CrossUnitAtomic</c>), and isolates the composed lifecycle-event durability proof as its own test.
/// </summary>
[Collection(SqlServerDesignProviderCollection.Name)]
public sealed class SqlServerAtomicityContractSuite(SqlServerDesignProviderFixture container)
    : DesignAtomicityContractSuite
{
    private readonly GroundworkBaselineTelemetry _telemetry = new();

    protected override DesignPersistenceContractProfile ContractProfile => DesignPersistenceContractProfiles.Target;

    protected override async Task<IDesignPersistenceContractFixture> CreateFixtureAsync(
        CancellationToken cancellationToken = default) =>
        await SqlServerDesignPersistenceContractFixture.CreateAsync(container, _telemetry, cancellationToken);

    [Fact]
    public async Task Public_create_draft_command_publishes_OnDraftCreated_only_after_the_draft_is_durable()
    {
        await using var fixture = await SqlServerDesignPersistenceContractFixture.CreateAsync(container, _telemetry);
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

/// <summary>Executes the target-profile isolation and restart contract on the composed SQL Server target.</summary>
[Collection(SqlServerDesignProviderCollection.Name)]
public sealed class SqlServerIsolationAndRestartContractSuite(SqlServerDesignProviderFixture container)
    : DesignIsolationAndRestartContractSuite
{
    private readonly GroundworkBaselineTelemetry _telemetry = new();

    protected override DesignPersistenceContractProfile ContractProfile => DesignPersistenceContractProfiles.Target;

    protected override async Task<IDesignPersistenceContractFixture> CreateFixtureAsync(
        CancellationToken cancellationToken = default) =>
        await SqlServerDesignPersistenceContractFixture.CreateAsync(container, _telemetry, cancellationToken);
}

/// <summary>Executes the T037 workflow query-shape parity contract on the composed SQL Server target.</summary>
[Collection(SqlServerDesignProviderCollection.Name)]
public sealed class SqlServerWorkflowDesignQueryContractSuite(SqlServerDesignProviderFixture container)
    : WorkflowDesignQueryContractSuite
{
    private readonly GroundworkBaselineTelemetry _telemetry = new();

    protected override async Task<IDesignPersistenceContractFixture> CreateFixtureAsync(
        CancellationToken cancellationToken = default) =>
        await SqlServerDesignPersistenceContractFixture.CreateAsync(container, _telemetry, cancellationToken);
}

/// <summary>Executes the T038 activity query-shape parity contract on the composed SQL Server target.</summary>
[Collection(SqlServerDesignProviderCollection.Name)]
public sealed class SqlServerActivityDesignQueryContractSuite(SqlServerDesignProviderFixture container)
    : ActivityDesignQueryContractSuite
{
    private readonly GroundworkBaselineTelemetry _telemetry = new();

    protected override async Task<IDesignPersistenceContractFixture> CreateFixtureAsync(
        CancellationToken cancellationToken = default) =>
        await SqlServerDesignPersistenceContractFixture.CreateAsync(container, _telemetry, cancellationToken);
}

/// <summary>Executes the T039 scale/batching contract on the composed SQL Server target.</summary>
[Collection(SqlServerDesignProviderCollection.Name)]
public sealed class SqlServerDesignQueryScaleContractSuite(SqlServerDesignProviderFixture container)
    : DesignQueryScaleContractSuite
{
    private readonly GroundworkBaselineTelemetry _telemetry = new();

    protected override DesignPersistenceContractProfile ContractProfile => DesignPersistenceContractProfiles.Target;

    protected override async Task<IDesignPersistenceContractFixture> CreateFixtureAsync(
        CancellationToken cancellationToken = default) =>
        await SqlServerDesignPersistenceContractFixture.CreateAsync(container, _telemetry, cancellationToken);
}
