using Kernel.Native;

namespace Elsa.Workflows.Runtime.KernelHost;

/// <summary>What one activation slot serves on a kernel host: the Native workflow its activation maps to.</summary>
/// <param name="SlotId">The slot (<c>WorkflowActivationSlotIdentity</c>).</param>
/// <param name="Revision">The slot's revision when its workflow began to serve; schedules are set at it.</param>
public sealed record KernelServedSlot(string SlotId, string DefinitionId, string SlotName, string ActivationId, Workflow Workflow, long Revision);

/// <summary>
/// What a kernel host serves, a host singleton the shells share (definitions on the kernel, "The serving set and versions"):
/// <list type="bullet">
/// <item><see cref="Registry"/>: every workflow that may still run, the ones the slots serve and the ones instances started
/// on. A republish adds; nothing is removed here, since instances keep the artifact they started on.</item>
/// <item>The serving set (<see cref="Slots"/>): the workflow each active slot serves. Routes and schedules are built over it
/// only, so a republished definition replaces its route and its schedule's run instead of conflicting with its previous
/// version.</item>
/// <item><see cref="Routes"/> and <see cref="Schedules"/>: the route table and the recurring start triggers of the serving
/// set, rebuilt whenever it changes.</item>
/// </list>
/// The kernel coordinator changes it, one activation at a time; turns and the HTTP edge read it.
/// </summary>
public sealed class KernelServing
{
    /// <summary>The slot a publication serves unless it names another: the one a Dispatch reaches first.</summary>
    public const string DefaultSlotName = "default";

    private readonly Func<string, string?>? _cronError;
    private readonly Lock _lock = new();
    private ServingState _state;

    /// <param name="cronError">The host's check of a cron expression (<see cref="NativeSchedules"/>): null when it reads.</param>
    public KernelServing(Func<string, string?>? cronError = null)
    {
        _cronError = cronError;
        _state = Build(new Dictionary<string, KernelServedSlot>(StringComparer.Ordinal));
    }

    public WorkflowRegistry Registry { get; } = new();

    public HttpRoutes Routes => _state.Routes;

    public NativeSchedules Schedules => _state.Schedules;

    public IReadOnlyCollection<KernelServedSlot> Slots => [.. _state.Slots.Values];

    public KernelServedSlot? Find(string slotId) => _state.Slots.GetValueOrDefault(slotId);

    /// <summary>
    /// The workflow that serves a definition now, for a Dispatch that names it (<see cref="NativeExecutionOptions.ServedWorkflow"/>):
    /// its default slot's, else the one slot's that serves it; null when no slot, or several others, serve it.
    /// </summary>
    public string? ServedWorkflow(string definition)
    {
        var serving = _state.Slots.Values.Where(slot => slot.DefinitionId == definition).ToArray();
        return (serving.FirstOrDefault(slot => slot.SlotName == DefaultSlotName) ?? (serving.Length == 1 ? serving[0] : null))?.Workflow.Id;
    }

    /// <summary>
    /// The serving state with <paramref name="slotId"/> serving <paramref name="served"/>, or nothing when it is null. Building it
    /// checks its routes and schedules: a route conflict (<see cref="HttpRouteConflictException"/>), a route template the model
    /// refuses, or a recurring trigger it refuses (<see cref="NativeScheduleException"/>) throws, and nothing changes.
    /// </summary>
    internal KernelServingPlan Plan(string slotId, KernelServedSlot? served)
    {
        var slots = new Dictionary<string, KernelServedSlot>(_state.Slots, StringComparer.Ordinal);
        var previous = slots.GetValueOrDefault(slotId);
        if (served is null) slots.Remove(slotId);
        else slots[slotId] = served;
        return new(slotId, previous, served, Build(slots));
    }

    /// <summary>Serves <paramref name="plan"/>: its workflow joins the registry, and its slots, routes and schedules replace the current ones.</summary>
    internal void Apply(KernelServingPlan plan)
    {
        lock (_lock)
        {
            if (plan.Served is { } served) Registry.Add(served.Workflow);
            _state = plan.State;
        }
    }

    private ServingState Build(Dictionary<string, KernelServedSlot> slots)
    {
        var registry = new WorkflowRegistry();
        foreach (var slot in slots.Values.OrderBy(slot => slot.SlotId, StringComparer.Ordinal)) registry.Add(slot.Workflow);
        return new(slots, new HttpRoutes(registry), new NativeSchedules(registry, _cronError));
    }
}

internal sealed record ServingState(IReadOnlyDictionary<string, KernelServedSlot> Slots, HttpRoutes Routes, NativeSchedules Schedules);

/// <summary>A change to the serving set, checked: the slot, what it served before, what it serves after, and the state that results.</summary>
internal sealed record KernelServingPlan(string SlotId, KernelServedSlot? Previous, KernelServedSlot? Served, ServingState State);
