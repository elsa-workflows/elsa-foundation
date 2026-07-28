using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Persistence.Groundwork.DesignConformance.Tests;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.DesignConformance.Sqlite.Tests;

/// <summary>
/// Makes the target-profile atomicity contract executable as individually reported SQLite tests.
/// The baseline catalog consumes the same fixture, while this suite isolates a failed fault window.
/// </summary>
public sealed class SqliteAtomicityContractSuite : DesignAtomicityContractSuite
{
    private readonly GroundworkBaselineTelemetry _telemetry = new();

    protected override DesignPersistenceContractProfile ContractProfile => DesignPersistenceContractProfiles.Target;

    protected override async Task<IDesignPersistenceContractFixture> CreateFixtureAsync(
        CancellationToken cancellationToken = default) =>
        await SqliteDesignPersistenceContractFixture.CreateAsync(_telemetry, cancellationToken);

    [Fact]
    public async Task Public_create_draft_command_publishes_OnDraftCreated_only_after_the_draft_is_durable()
    {
        await using var fixture = await SqliteDesignPersistenceContractFixture.CreateAsync(_telemetry);
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
