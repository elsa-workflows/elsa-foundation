using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Models;

namespace Elsa.Workflows.Design.Api.Requests;

public sealed record ListWorkflowFolders(string? ParentId = null, int? PageSize = null, string? ContinuationToken = null) : IRequest<WorkflowFolderListView>;
