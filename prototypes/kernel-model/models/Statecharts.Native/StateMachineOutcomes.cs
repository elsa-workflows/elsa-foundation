using Kernel.Native;

namespace Kernel.Statecharts.Native;

// Private single-outcome transport profile, not a product outcome declaration registry.
internal sealed record FinalOutcomeSettings(Dictionary<string, string> Outcomes);

internal sealed partial class StateMachine
{
    [Input] public FinalOutcomeSettings? FinalOutcomes { get; set; }

    private FinalOutcomeSettings? FreezeOutcomes(CompositeContext context, SelectionChart graph)
    {
        if (context.Node.Inputs.TryGetValue(nameof(FinalOutcomes), out var binding) && binding is not LiteralBinding)
            throw new NotSupportedException("Statechart final outcomes must be literal for this prototype.");
        if (FinalOutcomes is null) return null;
        if (InternalExecution is null || InputInbox is null || context.RecoveryPolicy < 4)
            throw new NotSupportedException("Statechart final outcomes require the qualified internal profile.");
        ValidateOutcomes(FinalOutcomes, graph);
        return new(FinalOutcomes.Outcomes.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal));
    }

    private static void ValidateOutcomes(FinalOutcomeSettings? settings, SelectionChart graph)
    {
        if (settings?.Outcomes is not { Count: > 0 } map || map.Values.Any(string.IsNullOrEmpty) ||
            !graph.TopFinalStates.ToHashSet(StringComparer.Ordinal).SetEquals(map.Keys))
            throw new InvalidOperationException("Invalid statechart final-outcome mapping.");
    }

    private static void ValidateOutcomeState(MachineState state, SelectionChart graph)
    {
        if (state.Version < 7)
        {
            if (state.FinalOutcomes is not null) throw new InvalidOperationException("Mixed statechart final-outcome checkpoint.");
            return;
        }
        ValidateOutcomes(state.FinalOutcomes, graph);
    }

    private static void CompleteFinal(CompositeContext context, MachineState state, SelectionChart graph)
    {
        var outcome = state.FinalOutcomes is { } settings
            ? settings.Outcomes[graph.TopFinalStates.Single(state.Active.Contains)] : "Done";
        context.Complete(outcome);
    }
}
