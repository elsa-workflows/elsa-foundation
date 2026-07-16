using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit;

public sealed class WorkflowDefinitionListProjectionStoreTests
{
    [Fact]
    public async Task Ef_core_projects_current_draft_latest_version_and_count_for_the_definition_batch()
    {
        using var host = WorkflowsDesignTestHost.Create();
        await host.EnsureDefinition("definition-1");
        await host.EnsureDefinition("definition-2");
        await using (var dbContext = host.CreateContext())
        {
            dbContext.WorkflowDefinitionDrafts.AddRange(
                Draft("draft-1", "definition-1", 1),
                Draft("draft-2", "definition-1", 2));
            dbContext.WorkflowDefinitionVersions.AddRange(
                Version("version-1", "definition-1", "1.0.0"),
                Version("version-2", "definition-1", "2.0.0"));
            await dbContext.SaveChangesAsync();
        }

        using var scope = host.Services.CreateScope();
        var projections = await scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionListProjectionStore>()
            .ListByDefinitionIdsAsync(["definition-1", "definition-2"]);

        var populated = Assert.Single(projections, x => x.WorkflowDefinitionId == "definition-1");
        Assert.Equal("draft-2", populated.DraftId);
        Assert.Equal("version-2", populated.LatestVersionId);
        Assert.Equal("2.0.0", populated.LatestVersion);
        Assert.Equal(2, populated.VersionCount);

        var empty = Assert.Single(projections, x => x.WorkflowDefinitionId == "definition-2");
        Assert.Null(empty.DraftId);
        Assert.Null(empty.LatestVersionId);
        Assert.Null(empty.LatestVersion);
        Assert.Equal(0, empty.VersionCount);
    }

    private static WorkflowDefinitionDraft Draft(string id, string definitionId, int day) => new()
    {
        Id = id,
        WorkflowDefinitionId = definitionId,
        CreatedAt = DateTimeOffset.UnixEpoch.AddDays(day),
        LastModifiedAt = DateTimeOffset.UnixEpoch.AddDays(day),
        State = EmptyState()
    };

    private static WorkflowDefinitionVersion Version(string id, string definitionId, string version) =>
        new(definitionId, version) { Id = id, State = EmptyState() };

    private static WorkflowDefinitionState EmptyState() => WorkflowDefinitionState.Empty;
}
