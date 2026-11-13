using System.Text.Json.Nodes;
using Bpmn.Model;
using Bpmn.Semantics;

namespace Kernel.Bpmn;

/// <summary>
/// What the host does for one binding ref. The interpreter does not tell a timer from a service call: every unit of
/// host work is a <c>StartWork</c>, and the host's table says which kernel operation it becomes (BPMN.md, section 2).
/// </summary>
public abstract record Work;

/// <summary>A service task: a background job running the named service (<see cref="ProcessRegistry.Service"/>).</summary>
public sealed record JobWork(string Service) : Work;

/// <summary>A timer catch or boundary timer: a message to self with a delay; cancelled by recalling it.</summary>
public sealed record TimerWork(int Milliseconds) : Work;

/// <summary>A message catch: a wait for the event <c>msg:{Name}:{correlation}</c> that also gets kept events.</summary>
public sealed record MessageWork(string Name, string Correlation) : Work;

/// <summary>A call activity: a child instance of the called process; its variables come back as the output.</summary>
public sealed record CallWork(string Process) : Work;

/// <summary>What a service gives back: variables to set, and outcome names for flows conditioned on outcomes.</summary>
public sealed record ServiceResult(JsonObject? Variables = null, string[]? Outcomes = null);

/// <summary>A process the host can run: the definition, its graph (built once), the host's table of work, start variables.</summary>
public sealed class BpmnProcess
{
    public const BpmnHostCapabilities Capabilities = BpmnHostCapabilities.Full | BpmnHostCapabilities.ConditionEvaluation;

    public BpmnProcess(BpmnProcessDefinition definition, IReadOnlyDictionary<string, Work> work, JsonObject? variables = null)
    {
        Definition = definition;
        Work = work;
        Variables = variables ?? [];
        Graph = BpmnGraph.Build(definition, [.. work.Keys.Select(key => new BpmnBoundWork(key))], Capabilities);
    }

    public string Id => Definition.ProcessId;
    public BpmnProcessDefinition Definition { get; }
    public BpmnGraph Graph { get; }
    public IReadOnlyDictionary<string, Work> Work { get; }
    public JsonObject Variables { get; }
}

public sealed class ProcessRegistry
{
    private readonly Dictionary<string, BpmnProcess> _processes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<JsonObject, ServiceResult>> _services = new(StringComparer.Ordinal);

    public ProcessRegistry Add(BpmnProcess process)
    {
        _processes.Add(process.Id, process);
        return this;
    }

    public ProcessRegistry Service(string name, Func<JsonObject, ServiceResult> service)
    {
        _services.Add(name, service);
        return this;
    }

    public BpmnProcess Get(string id) => _processes.TryGetValue(id, out var p) ? p : throw new KeyNotFoundException($"No BPMN process '{id}'.");
    public Func<JsonObject, ServiceResult> GetService(string name) => _services.TryGetValue(name, out var s) ? s : throw new KeyNotFoundException($"No service '{name}'.");
    public IEnumerable<BpmnProcess> All => _processes.Values;
}

/// <summary>
/// The library's builder, plus flows conditioned on an expression: its <c>Connect</c> only conditions a flow on an
/// outcome name. The expression is the host's tiny language (<see cref="Conditions"/>), not FEEL.
/// </summary>
public sealed class ProcessDraft(string id)
{
    private readonly Dictionary<string, string> _expressions = new(StringComparer.Ordinal);
    private int _flows;

    public BpmnProcessBuilder B { get; } = new BpmnProcessBuilder(id).Executable();

    public ProcessDraft Flow(string source, string target, string? when = null, bool isDefault = false)
    {
        var flow = $"f{++_flows}-{source}-{target}";
        B.Connect(source, target, isDefault: isDefault, flowId: flow);
        if (when is not null) _expressions[flow] = when;
        return this;
    }

    public ProcessDraft Chain(params string[] elements)
    {
        for (var i = 0; i < elements.Length - 1; i++) Flow(elements[i], elements[i + 1]);
        return this;
    }

    public BpmnProcessDefinition Build()
    {
        var definition = B.Build();
        return definition with
        {
            SequenceFlows = [.. definition.SequenceFlows.Select(f => _expressions.TryGetValue(f.FlowId, out var body) ? f.WithExpression(body) : f)],
        };
    }
}

internal static class FlowExtensions
{
    public static BpmnSequenceFlow WithExpression(this BpmnSequenceFlow f, string body) =>
        new(f.FlowId, f.SourceRef, f.TargetRef, f.Name, f.ConditionOutcome, f.IsDefault, f.Extensions) { ConditionExpression = new BpmnConditionExpression(body) };
}
