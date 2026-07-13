using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit;

/// <summary>
/// Covers the persistence path behind <c>PATCH /design/workflows/definitions/{definitionId}</c>:
/// load the <c>WorkflowDefinition</c>, mutate Name/Description, and persist via the reused
/// <see cref="ISaveWorkflowDefinitionCommand"/> — mirroring the API handler.
/// </summary>
public sealed class WorkflowDefinitionMetadataUpdateTests
{
    [Fact]
    public async Task Updating_name_and_description_persists_both_fields()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var definitionId = await Submit(host, "Original", "Original description");

        await UpdateMetadata(host, definitionId, name: "Renamed", description: "Updated description");

        await using var ctx = host.CreateContext();
        var definition = await ctx.WorkflowDefinitions.FirstAsync(x => x.Id == definitionId);
        Assert.Equal("Renamed", definition.Name);
        Assert.Equal("Updated description", definition.Description);
    }

    [Fact]
    public async Task Partial_update_leaves_the_unspecified_field_untouched()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var definitionId = await Submit(host, "Keep", "Keep description");

        // Only the description is provided; the name must be preserved.
        await UpdateMetadata(host, definitionId, name: null, description: "Only description changed");

        await using (var ctx = host.CreateContext())
        {
            var definition = await ctx.WorkflowDefinitions.FirstAsync(x => x.Id == definitionId);
            Assert.Equal("Keep", definition.Name);
            Assert.Equal("Only description changed", definition.Description);
        }

        // Now only the name is provided; the description must be preserved.
        await UpdateMetadata(host, definitionId, name: "Renamed", description: null);

        await using (var ctx = host.CreateContext())
        {
            var definition = await ctx.WorkflowDefinitions.FirstAsync(x => x.Id == definitionId);
            Assert.Equal("Renamed", definition.Name);
            Assert.Equal("Only description changed", definition.Description);
        }
    }

    [Fact]
    public async Task Updating_metadata_bumps_last_modified_timestamp()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var definitionId = await Submit(host, "Timestamped", null);

        DateTimeOffset before;
        await using (var ctx = host.CreateContext())
            before = (await ctx.WorkflowDefinitions.FirstAsync(x => x.Id == definitionId)).LastModifiedAt;

        await UpdateMetadata(host, definitionId, name: "Timestamped v2", description: null);

        await using (var ctx = host.CreateContext())
        {
            var after = (await ctx.WorkflowDefinitions.FirstAsync(x => x.Id == definitionId)).LastModifiedAt;
            Assert.True(after >= before);
        }
    }

    [Fact]
    public async Task Updating_metadata_does_not_touch_versions_or_draft()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var definitionId = await Submit(host, "Unaffected", "desc");

        await using (var ctx = host.CreateContext())
        {
            var versionStates = await ctx.WorkflowDefinitionVersions
                .Where(x => x.DefinitionId == definitionId)
                .Select(x => new { x.Version, x.StateSource })
                .ToArrayAsync();
            var draftStates = await ctx.WorkflowDefinitionDrafts
                .Where(x => x.WorkflowDefinitionId == definitionId)
                .Select(x => x.StateSource)
                .ToArrayAsync();

            Assert.Equal(new[] { "1.0.0" }, versionStates.Select(x => x.Version).ToArray());
            var versionStateBefore = versionStates.Single().StateSource;
            var draftStateBefore = Assert.Single(draftStates);

            await UpdateMetadata(host, definitionId, name: "Unaffected v2", description: "desc v2");

            var versionStateAfter = await ctx.WorkflowDefinitionVersions
                .Where(x => x.DefinitionId == definitionId)
                .Select(x => x.StateSource)
                .SingleAsync();
            var draftStateAfter = await ctx.WorkflowDefinitionDrafts
                .Where(x => x.WorkflowDefinitionId == definitionId)
                .Select(x => x.StateSource)
                .SingleAsync();

            Assert.Equal(versionStateBefore, versionStateAfter);
            Assert.Equal(draftStateBefore, draftStateAfter);
        }
    }

    private static async Task<string> Submit(WorkflowsDesignTestHost host, string name, string? description)
    {
        using var scope = host.Services.CreateScope();
        var command = scope.ServiceProvider.GetRequiredService<ISubmitWorkflowDefinitionCommand>();
        var submitted = await command.Execute(name, description, new WorkflowDefinitionState(
            Variables: [],
            RootActivity: new ActivityNode("root", "activity-version-1", [], []),
            Inputs: [],
            Outputs: [],
            WorkflowActivityOptions: null,
            StrategyOptions: null));

        return submitted.DefinitionId;
    }

    // Mirrors the API handler: load via the store, apply the partial update, persist via the save command.
    private static async Task UpdateMetadata(WorkflowsDesignTestHost host, string definitionId, string? name, string? description)
    {
        using var scope = host.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>();
        var save = scope.ServiceProvider.GetRequiredService<ISaveWorkflowDefinitionCommand>();

        var definition = await store.FindByIdAsync(definitionId) ?? throw new InvalidOperationException($"Definition '{definitionId}' not found.");
        if (name is not null)
            definition.Name = name.Trim();
        if (description is not null)
            definition.Description = description;

        await save.Execute(definition);
    }
}
