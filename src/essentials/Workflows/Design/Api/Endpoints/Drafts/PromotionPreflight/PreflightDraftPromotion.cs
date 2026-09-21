using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Models;

namespace Elsa.Workflows.Design.Api.Endpoints.Drafts.PromotionPreflight;

/// <summary>Non-mutating, current-state assessment of automatic or exact draft promotion.</summary>
public sealed record PreflightDraftPromotion(string DraftId, string? RequestedVersion = null)
    : IRequest<PromotionPreflightAssessmentView>;
