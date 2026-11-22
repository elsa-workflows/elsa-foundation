using Elsa.Workflows.Design.Core.Contracts;

namespace Elsa.Workflows.Design.Core.Models;

/// <summary>Persistence-agnostic <see cref="IWorkflowDefinition"/> produced by the factory.</summary>
public sealed record WorkflowDefinitionModel(
    string Id,
    string Name,
    string? Description,
    DateTimeOffset CreatedAt = default,
    DateTimeOffset LastModifiedAt = default,
    DateTimeOffset? DeletedAt = null
) : IWorkflowDefinition
{
    public IWorkflowDefinition ShallowClone() => this with { };
}

/// <summary>Persistence-agnostic <see cref="IWorkflowDefinitionVersion"/> produced by the factory.</summary>
public sealed record WorkflowDefinitionVersionModel(
    string Id,
    string Version,
    string DefinitionId,
    IWorkflowDefinition Definition,
    WorkflowDefinitionState State,
    DateTimeOffset? SourceCreatedAt = null,
    DateTimeOffset CreatedAt = default,
    string? SourceDraftId = null
) : IWorkflowDefinitionVersion;

/// <summary>Persistence-agnostic <see cref="IWorkflowDefinitionDraft"/> produced by the factory.</summary>
public sealed record WorkflowDefinitionDraftModel(
    string Id,
    string WorkflowDefinitionId,
    WorkflowDefinitionState State,
    DateTimeOffset CreatedAt = default,
    DateTimeOffset LastModifiedAt = default
) : IWorkflowDefinitionDraft;
