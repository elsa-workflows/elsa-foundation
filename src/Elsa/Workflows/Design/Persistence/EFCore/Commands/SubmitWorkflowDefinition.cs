using Elsa.Primitives.Contracts;
using Elsa.Persistence.Core.Design;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Services;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EFCore.Commands;

public sealed class SubmitWorkflowDefinition(
    IIdentityGenerator identityGenerator,
    IDbContextFactory<WorkflowsDesignDbContext> contextFactory,
    IActivityStructureService activityStructureService)
    : ISubmitWorkflowDefinitionCommand
{
    private const string InitialVersion = "1.0.0";

    public async Task<SubmittedWorkflowDefinition> Execute(
        DesignOperationKey operationKey,
        string name,
        string? description,
        WorkflowDefinitionState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operationKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(state);
        SubmittedActivityTreeValidator.Validate(state.RootActivity, activityStructureService);

        var definitionId = identityGenerator.Generate();
        var draftId = identityGenerator.Generate();
        var versionId = identityGenerator.Generate();

        var definition = new WorkflowDefinition
        {
            Id = definitionId,
            Name = name,
            Description = description,
        };

        var draft = new WorkflowDefinitionDraft
        {
            Id = draftId,
            WorkflowDefinitionId = definitionId,
            State = state,
        };

        var draftLayout = WorkflowDefinitionDraftLayout.CreateFor(identityGenerator, draftId);

        var version = new WorkflowDefinitionVersion(definitionId, InitialVersion)
        {
            Id = versionId,
            State = state,
        };

        var versionLayout = new WorkflowDefinitionVersionLayout
        {
            Id = identityGenerator.Generate(),
            WorkflowDefinitionVersionId = versionId,
            Records = [],
        };

        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.WorkflowDefinitions.AddAsync(definition, cancellationToken);
        await dbContext.WorkflowDefinitionDrafts.AddAsync(draft, cancellationToken);
        await dbContext.WorkflowDefinitionDraftLayouts.AddAsync(draftLayout, cancellationToken);
        await dbContext.WorkflowDefinitionVersions.AddAsync(version, cancellationToken);
        await dbContext.WorkflowDefinitionVersionLayouts.AddAsync(versionLayout, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new SubmittedWorkflowDefinition(definitionId, draftId, versionId);
    }
}
