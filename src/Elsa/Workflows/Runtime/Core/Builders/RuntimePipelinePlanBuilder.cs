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
    public IReadOnlyList<RuntimePipelineMiddlewareRegistration> Registrations => _registrations.AsReadOnly();

    /// <summary>The middleware contract every registration on this pipeline must implement.</summary>
    protected abstract Type MiddlewareInterfaceType { get; }

    /// <summary>
    /// Registers a middleware type (resolved at composition time) into a named slot. Used to apply module contributions
    /// whose type is only known as a <see cref="Type"/>; the generic <c>Use&lt;T&gt;</c> overloads are preferred in code.
    /// </summary>
    public void Use(Type middlewareType, string slot, int order = 0, string? name = null)
    {
        ValidateMiddlewareType(middlewareType);
        AddRegistration(middlewareType, slot, order, name, isBuiltIn: false);
    }

    public RuntimePipelinePlan BuildPlan()
    {
        var ordered = _registrations
            .Select(registration => new RuntimePipelinePlanStep(
                registration.PipelineKind,
                registration.MiddlewareType,
                registration.Name,
                GetSlot(registration.SlotName),
                registration.Order,
                registration.RegistrationIndex,
                registration.IsBuiltIn))
            // Deterministic order, independent of registration/module-load order: slot, then coarse order, then
            // built-ins before modules at the same order (so a module at the built-in's order 0 runs *after* it), then
            // a stable type key. Genuine ties between distinct module middleware are rejected below, not silently resolved.
            .OrderBy(step => step.Slot.SortOrder)
            .ThenBy(step => step.Order)
            .ThenByDescending(step => step.IsBuiltIn)
            .ThenBy(step => step.MiddlewareType.FullName, StringComparer.Ordinal)
            .ToArray();

        var steps = DedupAndGuard(ordered);

        return new RuntimePipelinePlan(PipelineKind, Array.AsReadOnly(steps));
    }

    /// <summary>Replaces every registration of <paramref name="oldType"/> with <paramref name="newType"/>, preserving its slot/order/name.</summary>
    protected void ReplaceRegistration(Type oldType, Type newType)
    {
        ArgumentNullException.ThrowIfNull(oldType);
        ValidateMiddlewareType(newType);

        var replaced = false;
        for (var index = 0; index < _registrations.Count; index++)
        {
            if (_registrations[index].MiddlewareType != oldType)
                continue;

            _registrations[index] = _registrations[index] with { MiddlewareType = newType };
            replaced = true;
        }

        if (!replaced)
            throw new InvalidOperationException($"Cannot replace {PipelineKind} runtime middleware '{oldType.Name}': it is not registered on this pipeline.");
    }

    /// <summary>
    /// Removes every registration of <paramref name="middlewareType"/>. Idempotent: removing an absent middleware is a
    /// no-op, so independent modules can each disable it without depending on composition order.
    /// </summary>
    protected void RemoveRegistration(Type middlewareType)
    {
        ArgumentNullException.ThrowIfNull(middlewareType);

        _registrations.RemoveAll(registration => registration.MiddlewareType == middlewareType);
    }

    protected void AddRegistration(
        Type middlewareType,
        string slotName,
        int order,
        string? name,
        bool isBuiltIn)
    {
        if (string.IsNullOrWhiteSpace(slotName))
            throw new ArgumentException("A runtime pipeline slot name is required.", nameof(slotName));

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

    private void ValidateMiddlewareType(Type middlewareType)
    {
        ArgumentNullException.ThrowIfNull(middlewareType);

        if (!MiddlewareInterfaceType.IsAssignableFrom(middlewareType))
            throw new ArgumentException(
                $"Middleware type '{middlewareType.Name}' must implement '{MiddlewareInterfaceType.Name}' to register on the {PipelineKind} runtime pipeline.",
                nameof(middlewareType));
    }

    private RuntimePipelinePlanStep[] DedupAndGuard(IReadOnlyList<RuntimePipelinePlanStep> ordered)
    {
        // Idempotent registration: an identical (slot, order, type) registered more than once collapses to a single
        // step so it runs once, not once per registration. `ordered` is already sorted and equal keys are adjacent,
        // so first-occurrence grouping preserves the sort order.
        var deduped = ordered
            .GroupBy(step => (step.Slot.Name, step.Order, step.MiddlewareType))
            .Select(group => group.First())
            .ToArray();

        // Ambiguous ordering is only a problem between distinct *module* middleware: built-ins deterministically run
        // first at a given order, so a module sharing that order simply runs after the built-in. Two distinct modules
        // at the same (slot, order) have no defined relative order → reject and make the author disambiguate.
        foreach (var group in deduped.GroupBy(step => (step.Slot.Name, step.Order)))
        {
            var modules = group.Where(step => !step.IsBuiltIn).ToArray();
            if (modules.Length <= 1)
                continue;

            var types = string.Join(", ", modules.Select(step => step.MiddlewareType.Name));

            throw new InvalidOperationException(
                $"Ambiguous {PipelineKind} runtime pipeline ordering: slot '{group.Key.Name}' order {group.Key.Order} is claimed by multiple middleware ({types}). Give each a distinct order.");
        }

        return deduped;
    }

    private RuntimePipelineSlotDefinition GetSlot(string slotName) => _slotsByName[slotName];
}
