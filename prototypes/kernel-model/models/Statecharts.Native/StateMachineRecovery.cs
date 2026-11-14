using System.Text.Json.Nodes;
using Kernel.Native;

namespace Kernel.Statecharts.Native;

internal sealed partial class StateMachine
{
    JsonNode? IInterventionParent.PrepareRetry(Node definition, JsonNode? value, RetryChild child)
    {
        // The native owner has already matched original material. This preparation reads only literal definition/state
        // values and returns a new state; no binding evaluation, callback, scope allocation or chart progress occurs.
        T? Literal<T>(string name) where T : class => definition.Inputs.GetValueOrDefault(name) switch
        {
            null => null,
            LiteralBinding binding => Values.To<T>(binding.Value),
            _ => throw new InvalidOperationException("Statechart retry requires a literal definition.")
        };
        if (string.IsNullOrEmpty(child.Parent) || child.Tag is not null || child.Scope is not null || value is null)
            throw new InvalidOperationException("Invalid statechart retry obligation.");
        MachineState state;
        try
        {
            var graph = Compile(definition, Literal<Definition>(nameof(ChartDefinition)), Literal<StructuralDefinition>(nameof(StructuralDefinition)));
            state = Restore(Values.To<MachineState>(value) ?? throw new InvalidOperationException("Missing statechart retry state."), graph, child.Parent);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        { throw new InvalidOperationException("Invalid statechart retry state.", error); }
        if (state.Scopes is null || state.LegacyScopes) throw new InvalidOperationException("Statechart retry requires materialized visit scopes.");
        if (state.Inbox is { } inbox)
            InvocationInputs.Validate(child.Inbox, child.Parent, inbox.Capacity, inbox.ReceiptCapacity, inbox.RetentionMilliseconds, state.SelectedInput);
        var progress = state.Progress;
        if (state.Pending is null || progress is null || !progress.Running.TryGetValue(child.Source, out var index) || progress.Done[index] ||
            progress.Requires[index].Any(i => !progress.Done[i]) || state.Pending.Steps[index].Activity != child.Node || progress.Running.ContainsKey(child.Successor))
            throw new InvalidOperationException("Invalid statechart retry obligation.");
        var step = state.Pending.Steps[index];
        var visit = (step.Phase == "entry" ? state.PlannedVisits! : state.Visits!).Single(v => v.State == step.State);
        if (child.LexicalScope is null || state.Scopes!.Visits.GetValueOrDefault(visit.Occurrence) != child.LexicalScope)
            throw new InvalidOperationException("Statechart retry has the wrong visit scope.");
        progress.Running.Remove(child.Source);
        progress.Running.Add(child.Successor, index);
        return Values.From(state);
    }
}
