using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Models;

namespace Elsa.Workflows.Design.Api.Endpoints.Drafts.Get;

public sealed record GetDraft(string DraftId) : IRequest<WorkflowDraftView>;
