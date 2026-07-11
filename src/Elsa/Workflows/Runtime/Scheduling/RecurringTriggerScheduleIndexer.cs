using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Scheduling;

/// <summary>
/// Decorates the publish-time <see cref="IWorkflowTriggerIndexer"/> to also populate the recurring-trigger
/// schedule store (W16). When an artifact is (re)published, this decorator first materializes the complete
/// recurring schedule set from the pinned executable — walking its nodes, asking each
/// <see cref="IRecurringTriggerScheduleProvider"/> to describe the Timer/Cron trigger nodes, and seeding each
/// schedule's initial <see cref="RecurringTriggerSchedule.NextOccurrence"/> through the
/// <see cref="IRecurringScheduleCalculator"/>.
/// </summary>
/// <remarks>
/// <para>
/// Materializing here — rather than in the runtime core or the publishing pipeline — keeps the recurring-trigger
/// feature self-contained: it composes over the existing indexer service without modifying the publish handler
/// or the trigger core. All schedule calculation finishes before the inner indexer runs, so invalid or exhausted
/// recurring starts fail before bindings or schedules mutate. After preflight, schedule population mirrors the
/// indexer's delete-by-artifact-then-write replacement semantics.
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

        // No recurring-trigger providers composed → nothing to do (and no store need be present).
        if (_providers.Count == 0)
            return await _inner.IndexAsync(executable, cancellationToken);

        var artifactId = executable.Identity.ArtifactId;
        var now = _timeProvider.GetUtcNow();
        var schedules = new List<RecurringTriggerSchedule>();

        // Fully materialize every provider-owned recurring projection before either index is mutated.
        foreach (var node in Flatten(executable.RootActivity))
        {
            if (!IsTrigger(node))
                continue;

            RecurringScheduleDescriptor? descriptor;
            try
            {
                descriptor = Describe(node);
            }
            catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException)
            {
                throw Failure(artifactId, node, "RecurringSchedule", "The recurring schedule descriptor is invalid.", exception);
            }
            if (descriptor is null)
                continue;

            DateTimeOffset? next;
            try
            {
                next = _calculator.ComputeNext(descriptor.Kind, descriptor.Expression, now);
            }
            catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException)
            {
                throw Failure(artifactId, node, "RecurringSchedule", $"Recurring expression '{descriptor.Expression}' could not be materialized.", exception);
            }
            if (next is null)
                throw Failure(artifactId, node, "RecurringSchedule", $"Recurring expression '{descriptor.Expression}' has no future occurrence.");

            schedules.Add(new RecurringTriggerSchedule(
                ScheduleId: RecurringTriggerSchedule.BuildId(artifactId, node.ExecutableNodeId),
                ArtifactId: artifactId,
                StimulusType: descriptor.StimulusType,
                StimulusHash: descriptor.StimulusHash,
                Kind: descriptor.Kind,
                Expression: descriptor.Expression,
                NextOccurrence: next.Value,
                CreatedAt: now));
        }

        var bindings = await _inner.IndexAsync(executable, cancellationToken);
        await _store.DeleteByArtifactAsync(artifactId, cancellationToken);
        foreach (var schedule in schedules)
            await _store.SaveAsync(schedule, cancellationToken);

        return bindings;
    }

    private static WorkflowTriggerPreflightException Failure(string artifactId, ExecutableNode node, string facet, string message, Exception? innerException = null) =>
        new(artifactId, node.ExecutableNodeId, node.ActivityType, [], facet, message, innerException);

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
