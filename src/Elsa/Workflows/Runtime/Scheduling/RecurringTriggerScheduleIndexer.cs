using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Scheduling;

/// <summary>
/// Decorates the activation-scoped <see cref="IWorkflowTriggerIndexer"/> to also populate the recurring-trigger
/// schedule store (W16). When an activation is prepared, this decorator first materializes the complete
/// recurring schedule set from the pinned executable — walking its nodes, asking each
/// <see cref="IRecurringTriggerScheduleProvider"/> to describe the Timer/Cron trigger nodes, and seeding each
/// schedule's initial <see cref="RecurringTriggerSchedule.NextOccurrence"/> through the
/// <see cref="IRecurringScheduleCalculator"/>.
/// </summary>
/// <remarks>
/// <para>
/// Materializing here — rather than in the runtime core or the activation coordinator — keeps the
/// recurring-trigger feature self-contained: it composes over the existing indexer service without modifying the
/// coordinator or the trigger core. All schedule calculation finishes before the inner indexer runs, so invalid
/// or exhausted recurring starts fail before bindings or schedules mutate.
/// </para>
/// <para>
/// Both projections are written in prepared (non-serving) state under the coordinator's activation id, and this
/// decorator is the <b>single</b> writer of the recurring projection's preparation (FR-B-006). It therefore
/// prepares even when no <see cref="IRecurringTriggerScheduleProvider"/> is composed: the resulting empty
/// projection is explicit, so a later activate or compensate has a projection to move rather than silently
/// nothing, and no caller has to read the projection back and re-prepare it.
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

    public async ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> PrepareActivationAsync(
        WorkflowExecutable executable,
        string activationId,
        string slotId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(activationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);

        // With no providers composed the materialized set is empty, and the empty projection is still prepared
        // deliberately: it is this decorator's single owned write of the recurring projection.
        var schedules = MaterializeSchedules(executable, _timeProvider.GetUtcNow(), activationId, slotId);
        var bindings = await _inner.PrepareActivationAsync(executable, activationId, slotId, cancellationToken);
        await _store.PrepareActivationAsync(activationId, schedules, cancellationToken);
        return bindings;
    }

    private IReadOnlyCollection<RecurringTriggerSchedule> MaterializeSchedules(
        WorkflowExecutable executable,
        DateTimeOffset now,
        string activationId,
        string slotId)
    {
        var artifactId = executable.Identity.ArtifactId;
        var schedules = new List<RecurringTriggerSchedule>();

        // Fully materialize every provider-owned recurring projection before either index is mutated.
        foreach (var node in Flatten(executable.RootActivity))
        {
            if (!IsTrigger(node))
                continue;

            var selection = Describe(artifactId, node);
            if (selection is null)
                continue;
            var (providerId, descriptors) = selection.Value;

            // A single-descriptor node (Timer/Cron) keeps the plain (artifact, node) schedule id so its identity is
            // unchanged; a fan-out node (a BPMN process with several timer starts) disambiguates each schedule by
            // the descriptor's stimulus hash so the rows do not collapse onto one id (spec 117 D3).
            var fanOut = descriptors.Count > 1;
            foreach (var descriptor in descriptors)
            {
                DateTimeOffset? next;
                try
                {
                    next = _calculator.ComputeNext(descriptor.Kind, descriptor.Expression, now);
                }
                catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException)
                {
                    throw Failure(artifactId, node, [providerId], "RecurringSchedule", $"Recurring expression '{descriptor.Expression}' could not be materialized.", exception);
                }
                if (next is null)
                    throw Failure(artifactId, node, [providerId], "RecurringSchedule", $"Recurring expression '{descriptor.Expression}' has no future occurrence.");

                schedules.Add(new RecurringTriggerSchedule(
                    ScheduleId: BuildScheduleId(activationId, artifactId, node.ExecutableNodeId, descriptor.StimulusHash, fanOut),
                    ArtifactId: artifactId,
                    ExecutableNodeId: node.ExecutableNodeId,
                    StimulusType: descriptor.StimulusType,
                    StimulusHash: descriptor.StimulusHash,
                    Kind: descriptor.Kind,
                    Expression: descriptor.Expression,
                    NextOccurrence: next.Value,
                    CreatedAt: now,
                    ActivationId: activationId,
                    SlotId: slotId,
                    // Prepared, never born serving: the coordinator flips the projection after the slot CAS.
                    IsActive: false));
            }
        }

        return schedules;
    }

    private static WorkflowTriggerPreflightException Failure(string artifactId, ExecutableNode node, IReadOnlyCollection<string> providerIds, string facet, string message, Exception? innerException = null) =>
        new(artifactId, node.ExecutableNodeId, node.ActivityType, providerIds, facet, message, innerException);

    private static string BuildScheduleId(string activationId, string artifactId, string executableNodeId, string stimulusHash, bool fanOut) =>
        fanOut
            ? RecurringTriggerSchedule.BuildFanOutId(activationId, artifactId, executableNodeId, stimulusHash)
            : RecurringTriggerSchedule.BuildId(activationId, artifactId, executableNodeId);

    private (string ProviderId, IReadOnlyCollection<RecurringScheduleDescriptor> Descriptors)? Describe(string artifactId, ExecutableNode node)
    {
        var claims = new List<(string ProviderId, IReadOnlyCollection<RecurringScheduleDescriptor> Descriptors)>();

        foreach (var provider in _providers)
        {
            var providerId = provider.ProviderId;
            var providerType = provider.GetType().FullName ?? provider.GetType().Name;
            IReadOnlyCollection<RecurringScheduleDescriptor> descriptors;
            try
            {
                descriptors = provider.Describe(node);
            }
            catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException)
            {
                if (string.IsNullOrWhiteSpace(providerId))
                    throw Failure(
                        artifactId,
                        node,
                        [],
                        "ProviderIdentity",
                        $"Recurring trigger provider type '{providerType}' has a blank provider id and failed while describing node '{node.ExecutableNodeId}'.",
                        exception);

                throw Failure(
                    artifactId,
                    node,
                    [providerId],
                    "RecurringSchedule",
                    $"The recurring schedule descriptor is invalid: {exception.Message}",
                    exception);
            }

            if (descriptors.Count > 0)
            {
                if (string.IsNullOrWhiteSpace(providerId))
                    throw Failure(
                        artifactId,
                        node,
                        [],
                        "ProviderIdentity",
                        $"Recurring trigger provider type '{providerType}' recognizes node '{node.ExecutableNodeId}' but has a blank provider id.");

                claims.Add((providerId, descriptors));
            }
        }

        return claims.Count switch
        {
            0 => null,
            1 => claims[0],
            _ => throw Failure(
                artifactId,
                node,
                claims.Select(x => x.ProviderId).ToArray(),
                "ProviderRecognition",
                $"Multiple recurring trigger providers recognize node '{node.ExecutableNodeId}'.")
        };
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
