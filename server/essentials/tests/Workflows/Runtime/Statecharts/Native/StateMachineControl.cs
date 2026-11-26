using Kernel.Native;

namespace Kernel.Statecharts.Native;

internal sealed record InternalExecutionSettings(int Limit, string ControlActivity, Dictionary<string, string>? Conditions = null);
internal enum ControlPurpose { Continue, Guard, Loop }
internal sealed record InternalControlSpec(ControlPurpose Purpose, string? Rule = null, string? Visit = null, string? Detail = null);
internal sealed record ControlObligation(string Original, string Current, InternalControlSpec Spec, string? ReboundFrom = null);
internal sealed record ControlResult(string Frame, string? Source);

// A normal, statically indexed private child. Native intervention/retry owns its actual fault and successor.
internal sealed class StateMachineControl : Activity<ControlResult>
{
    internal const string Key = "$statechart-control";
    internal const string Tag = "statechart-internal";
    public StateMachineControl() { }
    [Input] public InternalControlSpec Descriptor { get; set; } = null!;
    protected override ValueTask<Transition> ExecuteAsync(ActivityContext context) => ValueTask.FromResult(
        Descriptor.Purpose != ControlPurpose.Continue && context.RecoverySource is null
            ? Fault(Descriptor.Purpose == ControlPurpose.Guard ? "statechart.guard" : "statechart.loop", Descriptor.Detail!)
            : Complete(new(context.FrameId, context.RecoverySource)));
}

internal sealed partial class StateMachine
{
    [Input] public InternalExecutionSettings? InternalExecution { get; set; }

    private static void ValidateExecution(Node node, SelectionChart graph, InternalExecutionSettings execution)
    {
        var control = node.Children.SingleOrDefault(n => n.Id == execution.ControlActivity);
        if (execution.Limit <= 0 || control is null || control.Type != typeof(StateMachineControl) || control.External || control.Background ||
            control.Children.Count != 0 || control.Connections.Count != 0 || control.Captures.Count != 0 || control.Gateways.Count != 0 ||
            control.Inputs.Count != 1 || control.Inputs.GetValueOrDefault(nameof(StateMachineControl.Descriptor)) is not VariableBinding { Name: StateMachineControl.Key } ||
            graph.Activities.Contains(control.Id, StringComparer.Ordinal))
            throw new ArgumentException("Invalid statechart internal control definition.");
        if (graph.Rules.Any(r => r.Kind != TriggerKind.Event && r.When is not null) ||
            execution.Conditions?.Any(p => p.Value is null || !graph.Rules.Any(r => r.Id == p.Key)) == true)
            throw new ArgumentException("Invalid statechart condition mapping.");
    }
}
