using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Models;

namespace Elsa.Workflows.Design.Api.Endpoints.Drafts.Validations;

/// <summary>
/// Derives the FR-024 validation error set for the current state of the draft with the given id.
/// Backs <c>GET design/workflows/drafts/{draftId}/validations</c> so the Studio can render the
/// violations proactively (editor gutter + publish dialog) instead of learning about them only from
/// a promotion 409.
/// </summary>
public sealed record GetDraftValidations(string DraftId) : IRequest<DraftValidationsView>;
