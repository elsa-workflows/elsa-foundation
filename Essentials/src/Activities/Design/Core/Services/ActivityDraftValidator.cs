using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Diagnostics;

namespace Elsa.Activities.Design.Core.Services;

/// <summary>Runs provider-neutral contract checks followed by the exact provider/schema validator.</summary>
public sealed class ActivityDraftValidator(
    IActivityProviderRegistry providers,
    ActivityContractAuthoringValidator contractAuthoringValidator,
    TimeProvider timeProvider) : IActivityDraftValidator
{
    public async ValueTask<ActivityDraftValidation> ValidateAsync(
        ActivityDraftValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        var subject = new ActivityDiagnosticSubject(
            "ActivityDraft",
            request.DraftId,
            request.DefinitionId,
            Revision: request.Revision);
        var diagnostics = ValidateContract(request.State.Contract, subject).ToList();
        diagnostics.AddRange(contractAuthoringValidator.Validate(request.State.Contract, subject));

        try
        {
            var provider = providers.Resolve(request.State.Provider.ProviderKey, request.State.Provider.SchemaVersion);
            if (provider.AuthoringCapabilities.ManifestSchemas.All(x =>
                    !StringComparer.Ordinal.Equals(x.SchemaVersion, request.State.Provider.SchemaVersion) || !x.IsAuthorable))
                diagnostics.Add(new(
                    "activity.provider.schema-not-authorable",
                    ActivityDiagnosticSeverity.Error,
                    "The exact provider schema is retained for historical reads but is not available for mutable authoring or publication.",
                    subject,
                    new(request.State.Provider.ProviderKey),
                    "Migrate the draft to an authorable provider schema before publishing.",
                    new Dictionary<string, string>
                    {
                        ["providerKey"] = request.State.Provider.ProviderKey,
                        ["schemaVersion"] = request.State.Provider.SchemaVersion
                    }));
            else
                diagnostics.AddRange(await provider.ValidateAsync(request.State.Provider, request.State.Contract, cancellationToken));
        }
        catch (InvalidOperationException)
        {
            diagnostics.Add(new(
                ActivityErrorCodes.ProviderUnavailable,
                ActivityDiagnosticSeverity.Error,
                "The requested activity provider or manifest schema is unavailable.",
                subject,
                new(request.State.Provider.ProviderKey),
                "Enable a provider that owns the exact persisted provider key and schema.",
                new Dictionary<string, string>
                {
                    ["providerKey"] = request.State.Provider.ProviderKey,
                    ["schemaVersion"] = request.State.Provider.SchemaVersion
                }));
        }

        var ordered = ActivityDiagnosticOrderer.Order(diagnostics);
        return new(
            request.DraftId,
            request.Revision,
            ordered.All(x => x.Severity != ActivityDiagnosticSeverity.Error),
            timeProvider.GetUtcNow(),
            ordered);
    }

    private static IEnumerable<ActivityDiagnostic> ValidateContract(
        ActivityContract contract,
        ActivityDiagnosticSubject subject)
    {
        if (string.IsNullOrWhiteSpace(contract.ContractSchemaVersion))
            yield return Diagnostic("activity.contract.schema-required", "The contract schema version is required.", subject, "/contract/contractSchemaVersion");

        foreach (var duplicate in contract.Inputs.GroupBy(x => x.ReferenceKey, StringComparer.Ordinal).Where(x => x.Count() > 1))
            yield return Diagnostic("activity.contract.input-reference-duplicate", $"Input reference key '{duplicate.Key}' is not unique.", subject, "/contract/inputs", duplicate.Key);
        foreach (var duplicate in contract.Outputs.GroupBy(x => x.ReferenceKey, StringComparer.Ordinal).Where(x => x.Count() > 1))
            yield return Diagnostic("activity.contract.output-reference-duplicate", $"Output reference key '{duplicate.Key}' is not unique.", subject, "/contract/outputs", duplicate.Key);
        foreach (var duplicate in contract.Outcomes.GroupBy(x => x.ReferenceKey, StringComparer.Ordinal).Where(x => x.Count() > 1))
            yield return Diagnostic("activity.contract.outcome-reference-duplicate", $"Outcome reference key '{duplicate.Key}' is not unique.", subject, "/contract/outcomes", duplicate.Key);

        foreach (var input in contract.Inputs.Where(x => string.IsNullOrWhiteSpace(x.ReferenceKey) || string.IsNullOrWhiteSpace(x.StorageDriverKey)))
            yield return Diagnostic("activity.contract.input-invalid", "Every input requires a reference key and storage driver key.", subject, "/contract/inputs", input.ReferenceKey);
        foreach (var output in contract.Outputs.Where(x => string.IsNullOrWhiteSpace(x.ReferenceKey) || string.IsNullOrWhiteSpace(x.StorageDriverKey)))
            yield return Diagnostic("activity.contract.output-invalid", "Every output requires a reference key and storage driver key.", subject, "/contract/outputs", output.ReferenceKey);
    }

    private static ActivityDiagnostic Diagnostic(
        string code,
        string message,
        ActivityDiagnosticSubject subject,
        string jsonPointer,
        string? referenceKey = null) => new(
        code,
        ActivityDiagnosticSeverity.Error,
        message,
        subject,
        new(JsonPointer: jsonPointer, ReferenceKey: referenceKey),
        Metadata: new Dictionary<string, string>());
}
