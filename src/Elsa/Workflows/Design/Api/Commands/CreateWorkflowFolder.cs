using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Models;

namespace Elsa.Workflows.Design.Api.Commands;

public sealed record CreateWorkflowFolder(string Name, string? ParentId = null) : ICommand<WorkflowFolderView>;
