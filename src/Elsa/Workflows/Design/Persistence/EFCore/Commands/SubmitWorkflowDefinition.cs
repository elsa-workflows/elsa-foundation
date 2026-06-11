using Elsa.Primitives.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EFCore.Commands;

public sealed class SubmitWorkflowDefinition(
    IIdentityGenerator identityGenerator,
    IDbContextFactory<WorkflowsDesignDbContext> contextFactory)
    : ISubmitWorkflowDefinitionCommand
{
    private const string InitialVersion = "1.0.0";

    public async Task<SubmittedWorkflowDefinition> Execute(
        string name,
        string? description,
        WorkflowDefinitionState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(state);
        ValidateActivityTree(state.RootActivity);

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

        var draftLayout = new WorkflowDefinitionDraftLayout
        {
            Id = identityGenerator.Generate(),
            WorkflowDefinitionDraftId = draftId,
            Records = [],
        };

        var draftValidation = new WorkflowDefinitionDraftValidation
        {
            Id = identityGenerator.Generate(),
            WorkflowDefinitionDraftId = draftId,
            Errors = [],
        };

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
        await dbContext.WorkflowDefinitionDraftValidations.AddAsync(draftValidation, cancellationToken);
        await dbContext.WorkflowDefinitionVersions.AddAsync(version, cancellationToken);
        await dbContext.WorkflowDefinitionVersionLayouts.AddAsync(versionLayout, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new SubmittedWorkflowDefinition(definitionId, draftId, versionId);
    }

    private static void ValidateActivityTree(ActivityNode? rootActivity)
    {
        if (rootActivity is null)
            throw new ArgumentException("Workflow definition state must specify a root activity.", nameof(rootActivity));

        var stack = new Stack<ActivityNode>();
        stack.Push(rootActivity);

        while (stack.Count > 0)
        {
            var node = stack.Pop();

            if (string.IsNullOrWhiteSpace(node.NodeId))
                throw new ArgumentException("Activity node id cannot be empty.", nameof(rootActivity));

            if (string.IsNullOrWhiteSpace(node.ActivityVersionId))
                throw new ArgumentException($"Activity node '{node.NodeId}' must specify an activity version id.", nameof(rootActivity));

            foreach (var child in node.ChildSlots?.SelectMany(slot => slot.Activities) ?? [])
                stack.Push(child);
        }
    }
}
