using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Builders;

public abstract class RuntimePipelinePlanBuilder
{
    private readonly List<RuntimePipelineMiddlewareRegistration> _registrations = [];
    private readonly IReadOnlyDictionary<string, RuntimePipelineSlotDefinition> _slotsByName;

    protected RuntimePipelinePlanBuilder(
        RuntimePipelineKind pipelineKind,
        IReadOnlyList<RuntimePipelineSlotDefinition> slots)
    {
        PipelineKind = pipelineKind;
        Slots = slots;
        _slotsByName = slots.ToDictionary(slot => slot.Name, StringComparer.Ordinal);
    }

    public RuntimePipelineKind PipelineKind { get; }
    public IReadOnlyList<RuntimePipelineSlotDefinition> Slots { get; }
    public IReadOnlyList<RuntimePipelineMiddlewareRegistration> Registrations => _registrations;

    public RuntimePipelinePlan BuildPlan()
    {
        var steps = _registrations
            .Select(registration => new RuntimePipelinePlanStep(
                registration.PipelineKind,
                registration.MiddlewareType,
                registration.Name,
                GetSlot(registration.SlotName),
                registration.Order,
                registration.RegistrationIndex,
                registration.IsBuiltIn))
            .OrderBy(step => step.Slot.SortOrder)
            .ThenBy(step => step.Order)
            .ThenBy(step => step.RegistrationIndex)
            .ToArray();

        return new RuntimePipelinePlan(PipelineKind, steps);
    }

    protected void AddRegistration(
        Type middlewareType,
        string slotName,
        int order,
        string? name,
        bool isBuiltIn)
    {
        if (!_slotsByName.ContainsKey(slotName))
            throw new ArgumentException($"Unknown {PipelineKind} runtime pipeline slot '{slotName}'.", nameof(slotName));

        _registrations.Add(new RuntimePipelineMiddlewareRegistration(
            PipelineKind,
            middlewareType,
            name ?? middlewareType.Name,
            slotName,
            order,
            _registrations.Count,
            isBuiltIn));
    }

    private RuntimePipelineSlotDefinition GetSlot(string slotName) => _slotsByName[slotName];
}
