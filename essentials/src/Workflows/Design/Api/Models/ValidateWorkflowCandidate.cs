using System.Text.Json.Serialization;

namespace Elsa.Workflows.Design.Api.Models;

/// <summary>A request-only complete authoring proposal. Neither validation nor its result saves a draft.</summary>
public sealed record ValidateWorkflowCandidate(
    [property: JsonIgnore] string DraftId,
    WorkflowDraftView Baseline,
    WorkflowTemplateContentView Candidate);

public sealed record WorkflowCandidateValidationView(string State, IReadOnlyCollection<DraftValidationErrorView> Errors);
