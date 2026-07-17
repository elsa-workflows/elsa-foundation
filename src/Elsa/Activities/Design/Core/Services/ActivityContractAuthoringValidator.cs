using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Core.Services;

/// <summary>Admits mutable public contracts only through the activated capability catalog.</summary>
public sealed class ActivityContractAuthoringValidator(IActivityContractCapabilityCatalog catalog)
{
    public IReadOnlyList<ActivityDiagnostic> Validate(
        ActivityContract contract,
        ActivityDiagnosticSubject subject)
    {
        var capabilities = catalog.Types.ToDictionary(x => x.Alias, StringComparer.Ordinal);
        var diagnostics = new List<ActivityDiagnostic>();
        for (var index = 0; index < contract.Inputs.Count; index++)
        {
            var input = contract.Inputs[index];
            ValidateMember(input.ReferenceKey, input.Type, input.IsNullable, input.StorageDriverKey, $"/contract/inputs/{index}", subject, capabilities, diagnostics);
            if (input.Default?.Value.ValueKind == System.Text.Json.JsonValueKind.Null && !input.IsNullable)
                diagnostics.Add(Diagnostic(
                    "activity.contract.null-default-not-allowed",
                    "A null default requires the contract member to allow null.",
                    subject,
                    $"/contract/inputs/{index}/default/value",
                    input.ReferenceKey));
        }

        for (var index = 0; index < contract.Outputs.Count; index++)
        {
            var output = contract.Outputs[index];
            ValidateMember(output.ReferenceKey, output.Type, output.IsNullable, output.StorageDriverKey, $"/contract/outputs/{index}", subject, capabilities, diagnostics);
        }

        return ActivityDiagnosticOrderer.Order(diagnostics);
    }

    public IReadOnlyList<ActivityDiagnostic> ValidateTypeReference(
        Elsa.Primitives.Models.TypeReference type,
        string storageDriverKey,
        ActivityDiagnosticSubject subject,
        string jsonPointer,
        string referenceKey,
        bool hasNullValue = false)
    {
        var capabilities = catalog.Types.ToDictionary(x => x.Alias, StringComparer.Ordinal);
        var diagnostics = new List<ActivityDiagnostic>();
        ValidateMember(referenceKey, type, false, storageDriverKey, jsonPointer, subject, capabilities, diagnostics);
        if (hasNullValue && capabilities.GetValueOrDefault(type.Alias) is { SupportsNull: false })
            diagnostics.Add(Diagnostic(
                "activity.contract.null-default-unsupported",
                "The selected type does not support a null value.",
                subject,
                $"{jsonPointer}/initialValue/value",
                referenceKey));
        return ActivityDiagnosticOrderer.Order(diagnostics);
    }

    private static void ValidateMember(
        string referenceKey,
        Elsa.Primitives.Models.TypeReference type,
        bool isNullable,
        string storageDriverKey,
        string pointer,
        ActivityDiagnosticSubject subject,
        IReadOnlyDictionary<string, ActivityContractTypeCapability> capabilities,
        ICollection<ActivityDiagnostic> diagnostics)
    {
        if (!capabilities.TryGetValue(type.Alias, out var capability))
        {
            diagnostics.Add(Diagnostic(
                "activity.contract.type-unavailable",
                "The selected type alias is not available for contract authoring.",
                subject,
                $"{pointer}/type/alias",
                referenceKey));
            return;
        }

        if (!capability.SupportedCollectionKinds.Contains(type.CollectionKind))
            diagnostics.Add(Diagnostic(
                "activity.contract.collection-kind-unavailable",
                "The selected collection kind is not supported for this type.",
                subject,
                $"{pointer}/type/collectionKind",
                referenceKey));
        if (isNullable && !capability.SupportsNull)
            diagnostics.Add(Diagnostic(
                "activity.contract.nullability-unavailable",
                "The selected type does not support nullable contract members.",
                subject,
                $"{pointer}/isNullable",
                referenceKey));
        if (!capability.SupportsDurability || !capability.CompatibleStorageDriverKeys.Contains(storageDriverKey))
            diagnostics.Add(Diagnostic(
                "activity.contract.storage-driver-unavailable",
                "The selected durable storage driver is not compatible with this type.",
                subject,
                $"{pointer}/storageDriverKey",
                referenceKey));
    }

    private static ActivityDiagnostic Diagnostic(
        string code,
        string message,
        ActivityDiagnosticSubject subject,
        string jsonPointer,
        string referenceKey) => new(
        code,
        ActivityDiagnosticSeverity.Error,
        message,
        subject,
        new(JsonPointer: jsonPointer, ReferenceKey: referenceKey),
        Metadata: new Dictionary<string, string>());
}
