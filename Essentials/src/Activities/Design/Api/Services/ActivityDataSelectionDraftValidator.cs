using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Diagnostics;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Activities.Design.Api.Services;

/// <summary>Runs the shared scoped read authority after the existing contract/provider validator.</summary>
internal sealed class ActivityDataSelectionDraftValidator(
    IActivityDraftValidator inner,
    IEnumerable<IActivityDataSelectionDocumentProjector> projectors,
    IEnumerable<IActivityDataSelectionAnalyzer> analyzers) : IActivityDraftValidator
{
    public async ValueTask<ActivityDraftValidation> ValidateAsync(ActivityDraftValidationRequest request, CancellationToken cancellationToken = default)
    {
        var existing = await inner.ValidateAsync(request, cancellationToken);
        // The original provider has already refused this candidate; retain its precise diagnostics.
        if (!existing.IsValid) return existing;
        var authorities = analyzers.ToArray();
        if (authorities.Length != 1)
            throw new InvalidOperationException("Activity draft validation requires exactly one shared data selection analyzer.");
        var revision = ActivityDataSelectionSnapshot.Hash(request.State);
        ActivityDataSelectionDocument? document = null;
        try
        {
            foreach (var projector in projectors)
            {
                var projected = await projector.TryProjectAsync(request.State.Provider, request.State.Contract, request.DraftId, revision, cancellationToken);
                if (projected is null) continue;
                if (document is not null) throw new InvalidOperationException("Multiple providers claimed the activity data selection document.");
                if (projected.ResourceId != request.DraftId || projected.ContractRevision != revision)
                    throw new InvalidOperationException("The provider data selection document does not match the actual candidate.");
                document = projected;
            }
            // Native/opaque providers without authored graphs keep their own diagnostics. Executable
            // admission independently checks their typed compiled reads and requires graph ownership.
            if (document is null) return existing;
            var analysis = await authorities[0].AnalyzeAsync(document, cancellationToken: cancellationToken);
            if (analysis.ContractRevision != revision)
                throw new InvalidOperationException("Activity data selection analysis does not match the actual candidate.");
            return Merge(analysis.Findings);
        }
        catch (ActivityDataSelectionRejectedException rejected) { return Merge(rejected.Findings); }

        ActivityDraftValidation Merge(IEnumerable<ValidationError> findings)
        {
            var subject = new ActivityDiagnosticSubject(request.SubjectKind, request.DraftId, request.DefinitionId, Revision: request.Revision);
            var diagnostics = ActivityDiagnosticOrderer.Order(existing.Diagnostics.Concat(findings.Select(finding => new ActivityDiagnostic(
                "activity.data-selection.invalid", finding.Severity == ValidationSeverities.Warning ? ActivityDiagnosticSeverity.Warning : ActivityDiagnosticSeverity.Error,
                finding.Message, subject, Location(finding.Path),
                Metadata: new Dictionary<string, string>(StringComparer.Ordinal) { ["validationPath"] = finding.Path, ["validationType"] = finding.Type }))));
            return existing with { IsValid = diagnostics.All(x => x.Severity != ActivityDiagnosticSeverity.Error), Diagnostics = diagnostics };
        }

        ActivityDiagnosticLocation Location(string path)
        {
            const string marker = "/inputs/";
            var input = path.LastIndexOf(marker, StringComparison.Ordinal);
            return input < 0 ? new(ProviderKey: request.State.Provider.ProviderKey) :
                new(ProviderKey: request.State.Provider.ProviderKey, ReferenceKey: path[(input + marker.Length)..],
                    NodeOrigin: [new("AuthoredNode", path[..input])]);
        }
    }
}
