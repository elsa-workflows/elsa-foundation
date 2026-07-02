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
        var steps = _registrations
            .Select(registration => new RuntimePipelinePlanStep(
                registration.PipelineKind,
                registration.MiddlewareType,
                registration.Name,
                GetSlot(registration.SlotName),
                registration.Order,
                registration.RegistrationIndex,
                registration.IsBuiltIn))
            // Deterministic order: slot, then coarse order, then a stable key (type name) so the resolved plan is
            // independent of registration/module-load order. True (slot, order) ties between distinct middleware are
            // rejected below rather than silently resolved.
            .OrderBy(step => step.Slot.SortOrder)
            .ThenBy(step => step.Order)
            .ThenBy(step => step.MiddlewareType.FullName, StringComparer.Ordinal)
            .ToArray();

        GuardAgainstSlotOrderCollisions(steps);

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

    /// <summary>Removes every registration of <paramref name="middlewareType"/>.</summary>
    protected void RemoveRegistration(Type middlewareType)
    {
        ArgumentNullException.ThrowIfNull(middlewareType);

        if (_registrations.RemoveAll(registration => registration.MiddlewareType == middlewareType) == 0)
            throw new InvalidOperationException($"Cannot remove {PipelineKind} runtime middleware '{middlewareType.Name}': it is not registered on this pipeline.");
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

    private void GuardAgainstSlotOrderCollisions(IReadOnlyList<RuntimePipelinePlanStep> steps)
    {
        foreach (var collision in steps
                     .GroupBy(step => (step.Slot.Name, step.Order))
                     .Where(group => group.Select(step => step.MiddlewareType).Distinct().Count() > 1))
        {
            var builtIn = collision.FirstOrDefault(step => step.IsBuiltIn);
            var types = string.Join(", ", collision.Select(step => step.MiddlewareType.Name).Distinct());
            var placement = $"slot '{collision.Key.Name}' order {collision.Key.Order}";

            var guidance = builtIn is not null
                ? $"'{builtIn.MiddlewareType.Name}' is the built-in at order 0; choose a negative order to run before it or a positive order to run after it."
                : "give each an explicit, distinct Order.";

            throw new InvalidOperationException(
                $"Ambiguous {PipelineKind} runtime pipeline ordering: {placement} is claimed by multiple middleware ({types}). {guidance}");
        }
    }

    private RuntimePipelineSlotDefinition GetSlot(string slotName) => _slotsByName[slotName];
}
