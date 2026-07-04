using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Scheduling;

/// <summary>
/// Decorates the publish-time <see cref="IWorkflowTriggerIndexer"/> to also populate the recurring-trigger
/// schedule store (W16). When an artifact is (re)published, the inner indexer first replaces the artifact's
/// trigger bindings (and fails the publish on an unroutable trigger); this decorator then replaces the
/// artifact's recurring schedules from the same pinned executable — walking its nodes, asking each
/// <see cref="IRecurringTriggerScheduleProvider"/> to describe the Timer/Cron trigger nodes, and seeding each
/// schedule's initial <see cref="RecurringTriggerSchedule.NextOccurrence"/> through the
/// <see cref="IRecurringScheduleCalculator"/>.
/// </summary>
/// <remarks>
/// <para>
/// Populating here — rather than in the runtime core or the publishing pipeline — keeps the recurring-trigger
/// feature self-contained: it composes over the existing indexer service without modifying the publish handler
/// or the trigger core. The inner indexer runs first so a publish that must fail (unroutable trigger) fails
/// before any schedule is written; schedule population mirrors the indexer's delete-by-artifact-then-write so a
/// republished version's schedules fully supersede the previous version's.
/// </para>
/// <para>
/// Only nodes the compiler marked as start-triggers are considered, exactly as the trigger extractor does, so a
/// Timer/Cron activity used mid-flow (not as a trigger) never produces a phantom schedule.
/// </para>
/// </remarks>
public sealed class RecurringTriggerScheduleIndexer : IWorkflowTriggerIndexer
{
    private readonly IWorkflowTriggerIndexer _inner;
    private readonly IReadOnlyList<IRecurringTriggerScheduleProvider> _providers;
    private readonly IRecurringTriggerScheduleStore _store;
    private readonly IRecurringScheduleCalculator _calculator;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RecurringTriggerScheduleIndexer> _logger;

    public RecurringTriggerScheduleIndexer(
        IWorkflowTriggerIndexer inner,
        IEnumerable<IRecurringTriggerScheduleProvider> providers,
        IRecurringTriggerScheduleStore store,
        IRecurringScheduleCalculator calculator,
        TimeProvider timeProvider,
        ILogger<RecurringTriggerScheduleIndexer> logger)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(calculator);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _inner = inner;
        _providers = providers.ToArray();
        _store = store;
        _calculator = calculator;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> IndexAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executable);

        // Trigger bindings first: this validates + persists them and throws on an unroutable trigger, so a bad
        // publish fails before any schedule is touched.
        var bindings = await _inner.IndexAsync(executable, cancellationToken);

        // No recurring-trigger providers composed → nothing to do (and no store need be present).
        if (_providers.Count == 0)
            return bindings;

        var artifactId = executable.Identity.ArtifactId;
        var now = _timeProvider.GetUtcNow();

        // Replace-on-republish: drop the prior version's schedules, then write the current set.
        await _store.DeleteByArtifactAsync(artifactId, cancellationToken);

        foreach (var node in Flatten(executable.RootActivity))
        {
            if (!IsTrigger(node))
                continue;

            var descriptor = Describe(node);
            if (descriptor is null)
                continue;

            var next = _calculator.ComputeNext(descriptor.Kind, descriptor.Expression, now);
            if (next is null)
            {
                // A cron with no future occurrence (e.g. a fixed past year) is authored-but-dead: skip it rather
                // than persist a schedule that can never fire.
                _logger.LogWarning(
                    "Recurring trigger node '{NodeId}' in artifact '{ArtifactId}' has no future occurrence for expression '{Expression}'; not scheduled.",
                    node.ExecutableNodeId, artifactId, descriptor.Expression);
                continue;
            }

            var schedule = new RecurringTriggerSchedule(
                ScheduleId: RecurringTriggerSchedule.BuildId(artifactId, node.ExecutableNodeId),
                ArtifactId: artifactId,
                StimulusType: descriptor.StimulusType,
                StimulusHash: descriptor.StimulusHash,
                Kind: descriptor.Kind,
                Expression: descriptor.Expression,
                NextOccurrence: next.Value,
                CreatedAt: now);

            await _store.SaveAsync(schedule, cancellationToken);
        }

        return bindings;
    }

    private RecurringScheduleDescriptor? Describe(ExecutableNode node)
    {
        foreach (var provider in _providers)
        {
            var descriptor = provider.Describe(node);
            if (descriptor is not null)
                return descriptor;
        }

        return null;
    }

    private static bool IsTrigger(ExecutableNode node) =>
        node.Metadata.TryGetValue(TriggerNodeMetadata.ExecutionTypeKey, out var executionType) &&
        StringComparer.Ordinal.Equals(executionType, TriggerNodeMetadata.TriggerExecutionType);

    private static IEnumerable<ExecutableNode> Flatten(ExecutableNode root)
    {
        var stack = new Stack<ExecutableNode>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;

            foreach (var child in node.ChildSlots.SelectMany(slot => slot.Activities))
                stack.Push(child);
        }
    }
}
