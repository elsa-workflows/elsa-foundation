using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Core.Contracts;

public interface IWorkflowDefinitionVersion
{
    string Id { get; }

    int Version { get; }

    IWorkflowDefinition Definition { get; }

    WorkflowDefinitionState State { get; }

    /// <summary>
    /// UTC timestamp when this draft was created
    /// </summary>
    DateTimeOffset CreatedAt { get; }

    DateTimeOffset? SourceCreatedAt { get; }
}
