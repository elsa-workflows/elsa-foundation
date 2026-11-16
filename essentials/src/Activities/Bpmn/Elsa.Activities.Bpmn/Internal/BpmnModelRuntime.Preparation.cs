using System.Text.Json;
using Elsa.Activities.Bpmn.Core;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Bpmn.Internal;

internal sealed partial class BpmnModelRuntime
{
    public async ValueTask<RuntimeExecutionMaterial> PreparePublicationAsync(BpmnModelDefinition definition,
        WorkflowExecutableInputContract inputContract, IReadOnlyList<BpmnWorkerInputMapping> workerInputMappings,
        BpmnModelCaller caller, CancellationToken cancellationToken = default)
    {
        ValidateCaller(caller);
        ArgumentNullException.ThrowIfNull(definition); ArgumentNullException.ThrowIfNull(inputContract);
        ArgumentNullException.ThrowIfNull(workerInputMappings);
        if (!materials.SupportsModelMaterial || !sources.CanWriteRootReferences(materials))
            throw new NotSupportedException("The selected artifact/source providers cannot prepare model material safely.");
        var useInputs = inputContract.Inputs.Count != 0 || workerInputMappings.Count != 0;
        var mappings = new List<BpmnWorkerInputMapping>();
        foreach (var binding in definition.ServiceBindings ?? [])
        {
            var worker = await ValidatePublishedWorkerAsync(binding.BindingId, binding.Executable, caller, cancellationToken, allowInputs: useInputs);
            if (useInputs)
            {
                var mapping = workerInputMappings.SingleOrDefault(item => item.BindingId == binding.BindingId)
                    ?? new BpmnWorkerInputMapping(binding.BindingId, new Dictionary<string, BpmnModelInputSource>());
                mappings.Add(new(binding.BindingId, mapping.Inputs, worker.InputContract));
            }
        }
        if (workerInputMappings.Any(mapping => !mappings.Any(actual => actual.BindingId == mapping.BindingId)))
            throw new BpmnModelValidationException("An input mapping does not name a supported service binding.");
        foreach (var binding in definition.NativeEventBindings ?? [])
            _ = await ValidatePublishedWorkerAsync(binding.BindingId, binding.Executable, caller, cancellationToken);
        var envelope = useInputs ? new BpmnInputModelDefinition(1, definition, mappings) : null;
        if (envelope is not null) BpmnInputSupport.Validate(inputContract, envelope,
            inputValidator ?? throw new NotSupportedException("BPMN inputs require the shared declared-input validator."));
        var dependencies = (definition.ServiceBindings ?? []).Select(binding => new RuntimeModelDependency(binding.BindingId, binding.Executable))
            .Concat((definition.NativeEventBindings ?? []).Select(binding => new RuntimeModelDependency(binding.BindingId, binding.Executable))).ToArray();
        var seed = new WorkflowExecutableIdentity("pending", "pending", "pending", "1.0.0", "pending");
        var model = new RuntimeModelMaterial(1, seed, new(BpmnModelProtocol.Key, BpmnModelProtocol.Version),
            envelope is null ? BpmnInputSupport.BaseProfile(definition) : BpmnModelProtocol.InputsProfile,
            envelope is null ? JsonSerializer.SerializeToElement(definition, Web) : JsonSerializer.SerializeToElement(envelope, Web),
            inputContract, [], dependencies, []);
        await driver.ValidateMaterialAsync(model, cancellationToken);
        BpmnDirectExecutionDriver.ValidateTimerDeadlines(model, clock.GetUtcNow());
        var hash = model.ComputeContentHash();
        model = new(1, new(hash, "bpmn-model:" + hash, hash, "1.0.0", hash), model.Protocol, model.Profile, model.Definition,
            model.InputContract, [], dependencies, []);
        await materials.SaveModelAsync(model, cancellationToken);
        return await materials.FindMaterialAsync(hash, cancellationToken)
            ?? throw new BpmnModelValidationException("The prepared model could not be read back.");
    }
}
