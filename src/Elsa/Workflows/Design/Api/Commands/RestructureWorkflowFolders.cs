using System.Text.Json.Serialization;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Models;

namespace Elsa.Workflows.Design.Api.Commands;

public sealed record RenameWorkflowFolder(string FolderId, string Name) : ICommand<WorkflowFolderView>;

/// <summary><see cref="ParentId"/> is required so an explicit null unambiguously means the root.</summary>
public sealed record MoveWorkflowFolder(string FolderId, [property: JsonRequired] string? ParentId) : ICommand<WorkflowFolderView>;

public sealed record DeleteWorkflowFolder(string FolderId) : ICommand;
