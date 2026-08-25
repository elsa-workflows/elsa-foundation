using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Models;

namespace Elsa.Workflows.Design.Api.Endpoints.Drafts.Promote;

public sealed record PromoteDraft(string? OperationKey, string DraftId, string? RequestedVersion = null) : ICommand<WorkflowDefinitionVersionDetailsView>;
