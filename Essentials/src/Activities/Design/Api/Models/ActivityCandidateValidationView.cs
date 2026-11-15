namespace Elsa.Activities.Design.Api.Models;

public sealed record ActivityCandidateValidationView(string State, ActivityDraftValidationView? Validation = null);
