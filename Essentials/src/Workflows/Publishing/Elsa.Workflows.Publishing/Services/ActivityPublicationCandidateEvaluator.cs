using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>
/// The one publication verdict for a candidate, whatever saved it: compile it through the registered provider compiler,
/// add storage and runtime readiness against the candidate's own subject, and refuse it when validation refused it or any
/// error remains. Studio drafts and source-owned versions are judged by the same rules.
/// </summary>
public sealed class ActivityPublicationCandidateEvaluator(IActivityTemplateCompiler compiler, IActivityPublicationReadiness reviewPolicy) : IActivityPublicationCandidateEvaluator
{
    public async Task<EvaluatedPublicationCandidate> EvaluateAsync(
        ActivityTemplateCompilerRequest request,
        ActivityDraftValidation validation,
        CancellationToken cancellationToken)
    {
        var compilation = await compiler.CompileAsync(request, cancellationToken);
        var subject = new ActivityDiagnosticSubject(request.Candidate.SubjectKind, request.Candidate.Id, request.Definition.Id,
            Revision: request.Candidate.Revision);
        var diagnostics = ActivityDiagnosticOrderer.Order(validation.Diagnostics
            .Concat(compilation.Diagnostics)
            .Concat(reviewPolicy.ReadinessDiagnostics(subject, compilation.Template?.RuntimeRequirements ?? [], compilation.Template?.StorageDriverRequirements ?? [])));
        // The validator's own verdict counts even without an error-severity diagnostic, so a candidate validation refused
        // is never publishable.
        var isPublishable =
            compilation.IsSuccessful &&
            validation.IsValid &&
            diagnostics.All(x => x.Severity != ActivityDiagnosticSeverity.Error);
        return new(compilation, diagnostics, isPublishable);
    }
}
