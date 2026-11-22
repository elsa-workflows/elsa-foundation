using Kernel.Foundation.Contracts;
using Kernel.Model.Contracts;

namespace Kernel.Statecharts;

/// <summary>Executes the prototype chart profile through the public model contract.</summary>
public sealed class StatechartModel : IExecutionModel
{
    private readonly IReadOnlyDictionary<string, Chart> _charts;
    private readonly IReadOnlyDictionary<string, Action<ITurn, Step>> _activities;

    public StatechartModel(IReadOnlyDictionary<string, Definition> definitions, IReadOnlyDictionary<string, Action<ITurn, Step>> activities)
    {
        _charts = definitions.ToDictionary(pair => pair.Key, pair => new Chart(pair.Value), StringComparer.Ordinal);
        _activities = activities.ToDictionary(StringComparer.Ordinal);
    }

    public string Key => "statechart";
    public Type StateType => typeof(StatechartCheckpoint);
    public int StateVersion => 1;

    public object CreateState(WorkflowStart start)
    {
        if (!_charts.ContainsKey(start.Definition))
            throw new ArgumentException("The statechart definition is unavailable.", nameof(start));
        return new StatechartCheckpoint(null);
    }

    public object UpgradeState(string state, int fromVersion) =>
        throw new NotSupportedException($"Statechart state version {fromVersion} cannot be upgraded to {StateVersion}.");

    public ValueTask TurnAsync(ITurn turn, CancellationToken cancellationToken)
    {
        var chart = _charts[turn.Instance.Definition];
        var state = (StatechartCheckpoint)turn.State;
        foreach (var message in turn.Inbox)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var reaction = state.Active is null
                ? (message.Kind == Kinds.Start ? chart.Start() : null)
                : chart.Select(state.Active, message.Id, message.Event?.Key ?? message.Kind, message.Payload);
            if (reaction is null) continue;
            foreach (var step in reaction.Steps)
            {
                if (step.Activity is { } name) _activities[name](turn, step);
                turn.Record(step);
            }

            state = new StatechartCheckpoint(reaction.Next);
            turn.State = state;
            if (chart.IsFinal(reaction.Next))
            {
                turn.SetStatus(ActorStatus.Finished, output: reaction.Next);
                break;
            }
        }
        return default;
    }

    public ValueTask<object?> ExecuteJobAsync(IJob job, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("This statechart profile does not create background jobs.");

    public RestorePlan RestoreWaits(InstanceInfo instance, object state) => new([], [], []);
    public long TerminalGrowth(InstanceInfo instance, object state) => 0;

    private sealed record StatechartCheckpoint(string[]? Active);
}
