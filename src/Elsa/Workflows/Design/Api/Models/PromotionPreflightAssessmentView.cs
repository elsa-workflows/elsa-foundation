namespace Elsa.Workflows.Design.Api.Models;

/// <summary>Authoritative for the observed state, but advisory and non-reserving for a later promotion.</summary>
public sealed record PromotionPreflightAssessmentView(
    bool IsReady,
    string AssignmentMode,
    string? RequestedVersion,
    string? ResolvedVersion,
    string? LatestVersion,
    IReadOnlyCollection<PromotionPreflightIssueView> Issues);

public sealed record PromotionPreflightIssueView(string Code, string Message, string? Path = null);
