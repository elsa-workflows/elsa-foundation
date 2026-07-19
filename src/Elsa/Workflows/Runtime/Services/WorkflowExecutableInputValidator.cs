using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>Default executable-input validator backed by the well-known type registry.</summary>
public sealed class WorkflowExecutableInputValidator(IWellKnownTypeRegistry? wellKnownTypeRegistry = null) : IWorkflowExecutableInputValidator
{
    public WorkflowExecutableInputValidation Validate(
        WorkflowExecutableInputContract contract,
        IEnumerable<KeyValuePair<string, JsonElement>> inputs) =>
        ValidateCore(contract, inputs, requireCompleteInputBag: true);

    public WorkflowExecutableInputValidation ValidateKnownInputs(
        WorkflowExecutableInputContract contract,
        IEnumerable<KeyValuePair<string, JsonElement>> inputs) =>
        ValidateCore(contract, inputs, requireCompleteInputBag: false);

    private WorkflowExecutableInputValidation ValidateCore(
        WorkflowExecutableInputContract contract,
        IEnumerable<KeyValuePair<string, JsonElement>> inputs,
        bool requireCompleteInputBag)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(inputs);

        if (contract.Version != WorkflowExecutableInputContract.CurrentVersion)
        {
            return WorkflowExecutableInputValidation.Failure(
            [
                new WorkflowExecutableInputValidationFinding(
                    WorkflowExecutableInputValidationReasonCodes.UnsupportedContractVersion,
                    "The executable input contract version is not supported by this runtime.")
            ]);
        }

        var supplied = inputs.ToArray();
        if (supplied.Any(input => string.IsNullOrWhiteSpace(input.Key)))
        {
            return WorkflowExecutableInputValidation.Failure(
            [
                new WorkflowExecutableInputValidationFinding(
                    WorkflowExecutableInputValidationReasonCodes.BlankName,
                    "Workflow input names cannot be blank.")
            ]);
        }

        var declaredNames = contract.Inputs.Select(input => input.Name).ToHashSet(StringComparer.Ordinal);
        var unknownInput = supplied.FirstOrDefault(input => !declaredNames.Contains(input.Key));
        if (unknownInput.Key is not null)
        {
            return WorkflowExecutableInputValidation.Failure(
            [
                new WorkflowExecutableInputValidationFinding(
                    WorkflowExecutableInputValidationReasonCodes.UnknownName,
                    $"Workflow input '{unknownInput.Key}' is not declared by the target executable.",
                    unknownInput.Key)
            ]);
        }

        var duplicateName = supplied
            .GroupBy(input => input.Key, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;
        if (duplicateName is not null)
        {
            return WorkflowExecutableInputValidation.Failure(
            [
                new WorkflowExecutableInputValidationFinding(
                    WorkflowExecutableInputValidationReasonCodes.DuplicateName,
                    $"Workflow input '{duplicateName}' was supplied more than once.",
                    duplicateName)
            ]);
        }

        var suppliedNames = supplied.Select(input => input.Key).ToHashSet(StringComparer.Ordinal);
        var missingRequired = requireCompleteInputBag
            ? contract.Inputs.FirstOrDefault(
                declaration => declaration.IsRequired && !declaration.DefaultValue.HasValue && !suppliedNames.Contains(declaration.Name))
            : null;
        if (missingRequired is not null)
        {
            return WorkflowExecutableInputValidation.Failure(
            [
                new WorkflowExecutableInputValidationFinding(
                    WorkflowExecutableInputValidationReasonCodes.MissingRequired,
                    $"Required workflow input '{missingRequired.Name}' was not supplied.",
                    missingRequired.Name)
            ]);
        }

        var normalized = supplied.ToList();
        normalized.AddRange(
            contract.Inputs
                .Where(declaration => !suppliedNames.Contains(declaration.Name) && declaration.DefaultValue.HasValue)
                .Select(declaration => new KeyValuePair<string, JsonElement>(declaration.Name, declaration.DefaultValue!.Value)));

        foreach (var declaration in contract.Inputs)
        {
            if (wellKnownTypeRegistry is null || !wellKnownTypeRegistry.TryGetTypeOrDefault(declaration.Type.Alias, out var elementType))
            {
                return WorkflowExecutableInputValidation.Failure(
                [
                    new WorkflowExecutableInputValidationFinding(
                        WorkflowExecutableInputValidationReasonCodes.UnknownTypeAlias,
                        $"Workflow input '{declaration.Name}' declares an unknown type alias.",
                        declaration.Name)
                ]);
            }

            var targetType = TypeReferenceFactory.Close(elementType, declaration.Type.CollectionKind);
            foreach (var input in normalized.Where(input => StringComparer.Ordinal.Equals(input.Key, declaration.Name)))
            {
                try
                {
                    _ = input.Value.Deserialize(targetType);
                }
                catch (JsonException)
                {
                    return WorkflowExecutableInputValidation.Failure(
                    [
                        new WorkflowExecutableInputValidationFinding(
                            WorkflowExecutableInputValidationReasonCodes.IncompatibleValue,
                            $"Workflow input '{declaration.Name}' is incompatible with its declared type.",
                            declaration.Name)
                    ]);
                }
            }
        }

        return WorkflowExecutableInputValidation.Success(normalized);
    }
}
