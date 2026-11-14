using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Activities.Bpmn.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Bpmn.Internal;

internal static class BpmnInputSupport
{
    private static readonly JsonSerializerOptions Strict = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    internal static BpmnInputModelDefinition? Envelope(RuntimeModelMaterial material) => material.Profile == BpmnModelProtocol.InputsProfile
        ? material.Definition.Deserialize<BpmnInputModelDefinition>(Strict) ?? throw Invalid("The BPMN input model is missing.") : null;

    internal static string BaseProfile(BpmnModelDefinition definition) => definition.FormatVersion switch
    {
        1 => BpmnModelProtocol.Profile,
        6 => BpmnModelProtocol.ClaimsProfile,
        _ => BpmnModelProtocol.NativeWorkProfile
    };

    internal static void Validate(WorkflowExecutableInputContract modelContract, BpmnInputModelDefinition envelope,
        IWorkflowExecutableInputValidator validator)
    {
        Check(validator.ValidateKnownInputs(modelContract, []));
        var bindings = envelope.BaseDefinition.ServiceBindings ?? [];
        if (!bindings.Select(binding => binding.BindingId).ToHashSet(StringComparer.Ordinal)
            .SetEquals(envelope.WorkerInputMappings.Select(mapping => mapping.BindingId)))
            throw Invalid("The model worker-input inventory must match its exact service bindings.");
        foreach (var mapping in envelope.WorkerInputMappings)
        {
            var contract = mapping.WorkerInputContract ?? throw Invalid("A model worker mapping lacks its pinned input contract.");
            Check(validator.ValidateKnownInputs(contract, mapping.Inputs.Where(item => item.Value.Kind == "Literal")
                .Select(item => KeyValuePair.Create(item.Key, item.Value.Value!.Value))));
            foreach (var pair in mapping.Inputs)
            {
                var target = contract.Inputs.SingleOrDefault(input => input.Name == pair.Key)
                    ?? throw Invalid($"Worker input '{pair.Key}' is not declared.");
                if (pair.Value.Kind != "Input") continue;
                var source = modelContract.Inputs.SingleOrDefault(input => input.Name == pair.Value.Name)
                    ?? throw Invalid($"Model input '{pair.Value.Name}' is not declared.");
                if (source.Type != target.Type || target.IsRequired && !target.DefaultValue.HasValue && !source.IsRequired && !source.DefaultValue.HasValue)
                    throw Invalid($"Model input '{source.Name}' cannot satisfy worker input '{target.Name}'.");
            }
            if (contract.Inputs.Any(input => input.IsRequired && !input.DefaultValue.HasValue && !mapping.Inputs.ContainsKey(input.Name)))
                throw Invalid("Every required worker input needs an explicit mapping or default.");
        }
    }

    internal static IReadOnlyDictionary<string, JsonElement> Realize(BpmnWorkerInputMapping mapping,
        IReadOnlyDictionary<string, JsonElement> modelInputs, IWorkflowExecutableInputValidator validator)
    {
        var values = new List<KeyValuePair<string, JsonElement>>();
        foreach (var pair in mapping.Inputs)
        {
            if (pair.Value.Kind == "Literal") values.Add(new(pair.Key, pair.Value.Value!.Value));
            else if (modelInputs.TryGetValue(pair.Value.Name!, out var value)) values.Add(new(pair.Key, value));
        }
        var result = validator.Validate(mapping.WorkerInputContract ?? throw Invalid("The worker contract is missing."), values);
        Check(result);
        return result.NormalizedInputs;
    }

    // Reuse the existing exact-string canonical model hash without changing the artifact format or global serializer.
    internal static string Fingerprint(WorkflowExecutableInputContract contract) => new RuntimeModelMaterial(1,
        new("contract", "contract", "contract", "1", "contract"), new("elsa.bpmn.worker-input-contract", 1), "v1",
        JsonSerializer.SerializeToElement(new { }), contract, [], [], []).ComputeContentHash();

    internal static void Check(WorkflowExecutableInputValidation result)
    {
        if (!result.IsValid) throw Invalid(string.Join(" ", result.Findings.Select(item => item.Message)));
    }
    private static BpmnModelValidationException Invalid(string message) => new(message);
}
