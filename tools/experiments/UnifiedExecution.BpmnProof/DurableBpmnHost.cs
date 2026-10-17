using Bpmn.Model;
using Bpmn.Model.State;
using Bpmn.Semantics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UnifiedExecution.BpmnProof;

/// <summary>
/// A deliberately small single-scope host used to exercise the real interpreter across process restarts.
/// Its work rows are persisted command effects, not real external workers.
/// </summary>
internal sealed class DurableBpmnHost : IDisposable
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new(System.Text.Json.JsonSerializerDefaults.Web);
    private readonly ProofStore _store;
    private readonly BpmnGraph _graph;
    private readonly BpmnInterpreter _interpreter = BpmnInterpreter.CreateDefault();

    private DurableBpmnHost(ProofStore store, BpmnGraph graph, string scopeId)
    {
        _store = store;
        _graph = graph;
        ScopeId = scopeId;
    }

    public string ScopeId { get; }
    public BpmnExecutionState State => _store.GetState(ScopeId);
    public string ContinuationKind => _store.GetScope(ScopeId).ContinuationKind;
    public IReadOnlyList<BpmnLiveWork> LiveWork => _store.GetLiveWork(ScopeId);
    public IReadOnlyList<WorkRecord> WorkItems => _store.GetWorkItems(ScopeId);
    public IReadOnlyList<HostTraceEvent> Trace => _store.GetTrace(ScopeId);
    public IReadOnlyList<IncidentRecord> Incidents => _store.GetIncidents(ScopeId);
    public int PendingCommandCount => _store.PendingOutboxCount(ScopeId);
    public TimeSpan Clock => TimeSpan.FromTicks(_store.GetScope(ScopeId).ClockTicks);

    public static DurableBpmnHost Create(string databasePath, string scopeId, BpmnProcessDefinition definition)
    {
        var store = new ProofStore(databasePath);
        var graph = BpmnGraph.Build(definition, BpmnBoundWork.Derive(definition), BpmnHostCapabilities.Full);
        store.CreateScope(scopeId, definition.ProcessId, Fingerprint(definition));
        return new DurableBpmnHost(store, graph, scopeId);
    }

    public static DurableBpmnHost Restore(string databasePath, string scopeId, BpmnProcessDefinition definition)
    {
        var store = new ProofStore(databasePath);
        var graph = BpmnGraph.Build(definition, BpmnBoundWork.Derive(definition), BpmnHostCapabilities.Full);
        var host = new DurableBpmnHost(store, graph, scopeId);
        try
        {
            var scope = store.GetScope(scopeId);
            if (!string.Equals(scope.ProcessId, definition.ProcessId, StringComparison.Ordinal))
                throw new InvalidOperationException($"Stored process '{scope.ProcessId}' does not match supplied definition '{definition.ProcessId}'.");
            var fingerprint = Fingerprint(definition);
            if (!string.Equals(scope.DefinitionFingerprint, fingerprint, StringComparison.Ordinal))
                throw new InvalidOperationException("Stored execution definition fingerprint does not match the supplied BPMN graph.");
            host.DrainOutbox();
            return host;
        }
        catch
        {
            host.Dispose();
            throw;
        }
    }

    public void Dispose() => _store.Dispose();

    public void Start(bool dispatchOwedCommands = true)
    {
        var evaluation = _interpreter.Start(new BpmnStartRequest(_graph, State, Snapshot()));
        Persist(evaluation, new HostTraceEvent("process-started", Message: _graph.ProcessId), dispatchOwedCommands);
    }

    public void CompleteWork(string handle, params string[] outcomeNames)
    {
        var work = GetOwnedWork(handle);
        var callbackStatus = work.Status == "live" ? "completed" : "late-reported";
        var live = _store.GetLiveWork(ScopeId).Where(item => !StringComparer.Ordinal.Equals(item.Handle, handle)).ToArray();
        var evaluation = _interpreter.OnWorkCompleted(new BpmnWorkCompletedRequest(
            _graph, State, Snapshot(live), work.BindingRef, work.Handle, outcomeNames, work.IterationId));

        Persist(
            evaluation,
            new HostTraceEvent("worker-completed", work.BindingRef, work.ElementId, work.Handle, string.Join(',', outcomeNames)),
            dispatchOwedCommands: true,
            callbackHandle: handle,
            callbackStatus: callbackStatus);
    }

    public BpmnErrorDisposition FaultWork(string handle, string message)
    {
        var work = GetOwnedWork(handle);
        var callbackStatus = work.Status == "live" ? "faulted" : "late-reported";
        var live = _store.GetLiveWork(ScopeId).Where(item => !StringComparer.Ordinal.Equals(item.Handle, handle)).ToArray();
        var fault = _interpreter.OnWorkFaulted(new BpmnWorkFaultedRequest(
            _graph, State, Snapshot(live), work.BindingRef, work.Handle, message)
            { FaultedIterationId = work.IterationId });

        var disposition = fault.Disposition;
        var incident = disposition is BpmnErrorDisposition.Propagated && fault.Continuation is BpmnContinuation.Fault
            ? new IncidentRecord(ScopeId, work.BindingRef, handle, message, _store.GetScope(ScopeId).Revision + 1)
            : null;
        var evaluation = new BpmnEvaluation(fault.State, fault.Continuation, fault.Commands);
        Persist(
            evaluation,
            new HostTraceEvent("worker-faulted", work.BindingRef, work.ElementId, handle, Message: message, Disposition: disposition.GetType().Name),
            dispatchOwedCommands: true,
            callbackHandle: handle,
            callbackStatus: callbackStatus,
            incident: incident);
        return disposition;
    }

    public void AdvanceBy(TimeSpan amount)
    {
        if (amount < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(amount));

        var target = Clock + amount;
        _store.SetClock(ScopeId, target.Ticks);
        foreach (var timer in _store.GetDueTimers(ScopeId, target.Ticks))
            CompleteWork(timer.Handle);
    }

    /// <summary>Delivers the named modeled message only to its live BPMN message catch listener.</summary>
    public void DeliverModeledMessage(string listenerHandle, string messageName)
    {
        var listener = GetOwnedWork(listenerHandle);
        if (listener.WorkKind != "message-listener" || listener.Status != "live")
            throw new InvalidOperationException($"'{listenerHandle}' is not a live BPMN message listener.");

        var element = _graph.GetRequiredElement(listener.ElementId);
        var modeledName = element.EventDefinitions
            .Where(definition => definition.Type == BpmnEventDefinitionTypes.Message)
            .Select(definition => definition.Properties.GetValueOrDefault(BpmnEventDefinitionProperties.Name))
            .SingleOrDefault();
        if (!string.Equals(modeledName, messageName, StringComparison.Ordinal))
            throw new InvalidOperationException($"Message '{messageName}' does not match listener '{listener.ElementId}'.");

        PersistMessageDelivery(listener, messageName);
    }

    public void RejectUnsupportedAlteration(string operation) => throw new UnsupportedAlterationException(operation);

    private void PersistMessageDelivery(WorkRecord listener, string messageName)
    {
        // A BPMN catch listener fires through the ordinary interpreter callback. The host validates the
        // external message identity; the interpreter decides whether that event interrupts its attached host.
        CompleteWork(listener.Handle, messageName);
    }

    private WorkRecord GetOwnedWork(string handle)
    {
        var work = _store.GetWork(handle);
        if (!string.Equals(work.ScopeId, ScopeId, StringComparison.Ordinal))
            throw new InvalidOperationException($"Work handle '{handle}' belongs to scope '{work.ScopeId}', not '{ScopeId}'.");
        return work;
    }

    private static string Fingerprint(BpmnProcessDefinition definition)
    {
        var json = JsonSerializer.Serialize(definition, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    private void Persist(
        BpmnEvaluation evaluation,
        HostTraceEvent trigger,
        bool dispatchOwedCommands,
        string? callbackHandle = null,
        string? callbackStatus = null,
        IncidentRecord? incident = null)
    {
        var commands = evaluation.Commands.Select(command => HostCommandMapper.Map(command, _graph)).ToArray();
        _store.CommitEvaluation(ScopeId, evaluation, commands, callbackHandle, callbackStatus, incident, trigger);
        if (dispatchOwedCommands)
            DrainOutbox();
    }

    private void DrainOutbox()
    {
        while (true)
        {
            var pending = _store.GetPendingCommands(ScopeId).FirstOrDefault();
            if (pending is null)
                return;

            var command = JsonSerializer.Deserialize<StoredHostCommand>(pending.Json, JsonOptions)
                ?? throw new InvalidOperationException($"Outbox command '{pending.CommandId}' was empty.");
            switch (command.Kind)
            {
                case "start-work":
                    _store.ApplyStartCommand(pending, command);
                    break;
                case "cancel-work-subtree":
                    _store.ApplyCancelCommand(pending, command);
                    break;
                case "signal-enclosing-scope":
                    if (!_store.GetScope(ScopeId).HasParent)
                        throw new InvalidOperationException("A root proof scope cannot dispatch an enclosing-scope signal.");
                    _store.ApplySignalCommand(pending, command);
                    break;
                default:
                    throw new NotSupportedException($"Unknown stored command kind '{command.Kind}'.");
            }
        }
    }

    private BpmnHostSnapshot Snapshot(IReadOnlyCollection<BpmnLiveWork>? liveWork = null)
    {
        var scope = _store.GetScope(ScopeId);
        return new BpmnHostSnapshot(
            ScopeInstanceId: ScopeId,
            HasEnclosingScope: scope.HasParent,
            LiveWork: liveWork ?? _store.GetLiveWork(ScopeId),
            InvocationCorrelation: scope.Correlation,
            Variables: BpmnNoVariables.Instance,
            Capabilities: BpmnHostCapabilities.Full);
    }
}
