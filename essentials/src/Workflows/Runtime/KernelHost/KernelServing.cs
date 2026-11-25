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
/// Coordinators change it one at a time, also when several shells each compose one over it (<see cref="RunAsync{T}"/>):
/// each checks its change against the serving set it changes. Turns and the HTTP edge read it.
/// </summary>
public sealed class KernelServing
{
    /// <summary>The slot a publication serves unless it names another: the one a Dispatch reaches first.</summary>
    public const string DefaultSlotName = "default";

    private readonly Func<string, string?>? _cronError;
    private readonly string? _schedulesUnavailable;
    private readonly bool _timers;
    private readonly bool _children;

    /// <summary>Why a Delay is refused on a host whose foundation runs no timers.</summary>
    public const string DelayUnavailable =
        "Delay doesn't run on this host yet. Remove it, or wait for an event or an HTTP request instead.";

    /// <summary>Why a Dispatch Workflow is refused on a host whose foundation runs no child workflows.</summary>
    public const string DispatchUnavailable =
        "Dispatch Workflow doesn't run on this host yet. Remove it, or start the other workflow from an HTTP endpoint or an event.";
    private readonly Lock _lock = new();
    private readonly SemaphoreSlim _turn = new(1, 1);
    private ServingState _state;

    /// <param name="cronError">The host's check of a cron expression (<see cref="NativeSchedules"/>): null when it reads.</param>
    /// <param name="schedulesUnavailable">
    /// Set by a host that runs no schedule actor: why. A workflow with a Timer or Cron start trigger is then refused at the
    /// serving check, with this reason, before anything is minted.
    /// </param>
    /// <param name="timers">
    /// Whether the host's foundation runs timers (self-sends with a due time). Without them a workflow with a Delay is refused
    /// where it is mapped, with <see cref="DelayUnavailable"/>.
    /// </param>
    /// <param name="children">
    /// Whether the host's foundation runs child workflows. Without them a workflow with a Dispatch Workflow, waited or not,
    /// is refused where it is mapped, with <see cref="DispatchUnavailable"/>.
    /// </param>
    public KernelServing(Func<string, string?>? cronError = null, string? schedulesUnavailable = null, bool timers = true, bool children = true)
    {
        _cronError = cronError;
        _schedulesUnavailable = schedulesUnavailable;
        (_timers, _children) = (timers, children);
        _state = Build(new Dictionary<string, KernelServedSlot>(StringComparer.Ordinal));
    }

    public WorkflowRegistry Registry { get; } = new();

    public HttpRoutes Routes => _state.Routes;

    public NativeSchedules Schedules => _state.Schedules;

    public IReadOnlyCollection<KernelServedSlot> Slots => [.. _state.Slots.Values];

    public KernelServedSlot? Find(string slotId) => _state.Slots.GetValueOrDefault(slotId);

    /// <summary>
    /// What serves a definition now, for a Dispatch that names it (<see cref="NativeExecutionOptions.ServedWorkflow"/>): its
    /// default slot's workflow, else the one slot's that serves it; none when no slot serves it; several when other slots do
    /// and none is the default.
    /// </summary>
    public ServedDefinition ServedWorkflow(string definition)
    {
        var serving = _state.Slots.Values.Where(slot => slot.DefinitionId == definition).ToArray();
        if (serving.Length == 0) return ServedDefinition.None;
        var reached = serving.FirstOrDefault(slot => slot.SlotName == DefaultSlotName) ?? (serving.Length == 1 ? serving[0] : null);
        return reached is null ? ServedDefinition.SeveralServe : ServedDefinition.By(reached.Workflow.Id);
    }

    /// <summary>
    /// Refuses a mapped workflow whose activities the host's foundation does not run, as the mapper refuses one it cannot map:
    /// a Delay without timers, a Dispatch Workflow without child workflows. The refusal names the first such node.
    /// </summary>
    /// <exception cref="NativeMappingRefusedException">The workflow holds an activity this host's foundation does not run.</exception>
    internal void RequireRunnable(Workflow workflow)
    {
        foreach (var node in workflow.Nodes.Values.OrderBy(node => node.Id, StringComparer.Ordinal))
        {
            if (!_timers && node.Type == typeof(Delay)) throw NativeMappingRefusedException.Activity(node.Id, DelayUnavailable);
            if (!_children && node.Type == typeof(DispatchWorkflow)) throw NativeMappingRefusedException.Activity(node.Id, DispatchUnavailable);
        }
    }

    /// <summary>Runs one coordinator's change to what serves: its check, its slot move and its serving, while no other runs.</summary>
    internal async ValueTask<T> RunAsync<T>(Func<ValueTask<T>> change, CancellationToken cancellationToken)
    {
        await _turn.WaitAsync(cancellationToken);
        try { return await change(); }
        finally { _turn.Release(); }
    }

    /// <summary>
    /// The recurring start triggers <paramref name="workflow"/> serves: read off the serving set's table when it serves, else
    /// off a table built over it alone (a slot this host does not serve, as after a restart). One whose triggers the model
    /// refuses has none.
    /// </summary>
    internal NativeSchedule[] SchedulesOf(Workflow workflow)
    {
        var served = _state.Schedules.All.Where(schedule => schedule.Workflow == workflow.Id).ToArray();
        if (served.Length > 0 || _state.Slots.Values.Any(slot => slot.Workflow.Id == workflow.Id)) return served;
        try { return [.. new NativeSchedules(new WorkflowRegistry().Add(workflow), _cronError).All]; }
        catch (ArgumentException) { return []; }
    }

    /// <summary>
    /// The serving state with <paramref name="slotId"/> serving <paramref name="served"/>, or nothing when it is null. Building it
    /// checks its routes and schedules: a route conflict (<see cref="HttpRouteConflictException"/>), a route template the model
    /// refuses, a recurring trigger it refuses (<see cref="NativeScheduleException"/>), or one on a host that runs no schedule
    /// actor, throws, and nothing changes.
    /// </summary>
    internal KernelServingPlan Plan(string slotId, KernelServedSlot? served)
    {
        var slots = new Dictionary<string, KernelServedSlot>(_state.Slots, StringComparer.Ordinal);
        var previous = slots.GetValueOrDefault(slotId);
        if (served is null) slots.Remove(slotId);
        else slots[slotId] = served;
        var state = Build(slots);
        if (_schedulesUnavailable is { } reason && served is not null && state.Schedules.All.Any(schedule => schedule.Workflow == served.Workflow.Id))
            throw new InvalidOperationException(reason);
        return new(slotId, previous, served, state);
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
