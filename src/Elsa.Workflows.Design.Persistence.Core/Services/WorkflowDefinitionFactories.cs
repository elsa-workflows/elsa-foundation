using Elsa.Primitives.Contracts;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Persistence.Core.Services;

/// <summary>Default <see cref="IWorkflowDefinitionFactory"/> — generates the id, returns a read-model.</summary>
public sealed class WorkflowDefinitionFactory(IIdentityGenerator identityGenerator) : IWorkflowDefinitionFactory
{
    public IWorkflowDefinition Create(string name, string? description = null, string? id = null) =>
        new WorkflowDefinitionModel(
            Id: string.IsNullOrWhiteSpace(id) ? identityGenerator.Generate() : id,
            Name: name,
            Description: description);
}

/// <summary>Default <see cref="IWorkflowDefinitionVersionFactory"/> — generates the id, returns a read-model.</summary>
public sealed class WorkflowDefinitionVersionFactory(IIdentityGenerator identityGenerator) : IWorkflowDefinitionVersionFactory
{
    public IWorkflowDefinitionVersion Create(
        IWorkflowDefinition definition,
        string version,
        WorkflowDefinitionState state,
        DateTimeOffset? sourceCreatedAt = null,
        string? id = null) =>
        new WorkflowDefinitionVersionModel(
            Id: string.IsNullOrWhiteSpace(id) ? identityGenerator.Generate() : id,
            Version: version,
            DefinitionId: definition.Id,
            Definition: definition,
            State: state,
            SourceCreatedAt: sourceCreatedAt);
}

/// <summary>Default <see cref="IWorkflowDefinitionDraftFactory"/> — generates the id, returns a read-model.</summary>
public sealed class WorkflowDefinitionDraftFactory(IIdentityGenerator identityGenerator) : IWorkflowDefinitionDraftFactory
{
    public IWorkflowDefinitionDraft Create(string workflowDefinitionId, WorkflowDefinitionState state, string? id = null) =>
        new WorkflowDefinitionDraftModel(
            Id: string.IsNullOrWhiteSpace(id) ? identityGenerator.Generate() : id,
            WorkflowDefinitionId: workflowDefinitionId,
            State: state);
}
