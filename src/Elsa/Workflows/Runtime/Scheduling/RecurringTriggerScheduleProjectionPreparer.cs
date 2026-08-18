using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Scheduling;

/// <summary>
/// Default <see cref="IRecurringTriggerScheduleProjectionPreparer"/> (W16). It materializes the complete recurring
/// schedule set from the pinned executable — walking its nodes, asking each
/// <see cref="IRecurringTriggerScheduleProvider"/> to describe the Timer/Cron trigger nodes, and seeding each
/// schedule's initial <see cref="RecurringTriggerSchedule.NextOccurrence"/> through the
/// <see cref="IRecurringScheduleCalculator"/> — then writes it as the candidate activation's prepared
/// (non-serving) projection.
/// </summary>
/// <remarks>
/// <para>
/// This was a decorator over <see cref="IWorkflowTriggerIndexer"/> until spec 151's T044b. Wrapping the indexer
/// kept the recurring feature self-contained, but at the cost of making a <b>replacement</b> contract silently own
/// a projection it does not advertise: a host substituting its own indexer lost recurring preparation, and found
/// out only when the coordinator activated the projection — after the slot CAS. It is now a collaborator the
/// coordinator calls in its own right, so neither contract can disarm the other.
/// </para>
/// <para>
/// The coordinator invokes it <b>before</b> the indexer, preserving the decorator's ordering guarantee: all
/// schedule calculation finishes before any binding is written, so an invalid or exhausted recurring start fails
/// with neither projection mutated.
/// </para>
/// <para>
/// It is the <b>single</b> writer of the recurring projection's preparation (FR-B-006) and therefore prepares even
/// when no <see cref="IRecurringTriggerScheduleProvider"/> is composed: the resulting empty projection is
/// explicit, so a later activate or compensate has a projection to move rather than silently nothing.
/// </para>
/// <para>
/// Only nodes the compiler marked as start-triggers are considered, exactly as the trigger extractor does, so a
/// Timer/Cron activity used mid-flow (not as a trigger) never produces a phantom schedule.
/// </para>
/// </remarks>
public sealed class RecurringTriggerScheduleProjectionPreparer : IRecurringTriggerScheduleProjectionPreparer
{
    private readonly IReadOnlyList<IRecurringTriggerScheduleProvider> _providers;
    private readonly IRecurringTriggerScheduleStore _store;
    private readonly IRecurringScheduleCalculator _calculator;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RecurringTriggerScheduleProjectionPreparer> _logger;

    public RecurringTriggerScheduleProjectionPreparer(
        IEnumerable<IRecurringTriggerScheduleProvider> providers,
        IRecurringTriggerScheduleStore store,
        IRecurringScheduleCalculator calculator,
        TimeProvider timeProvider,
        ILogger<RecurringTriggerScheduleProjectionPreparer> logger)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(calculator);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _providers = providers.ToArray();
        _store = store;
        _calculator = calculator;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async ValueTask PrepareActivationAsync(
        WorkflowExecutable executable,
        string activationId,
        string slotId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(activationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);

        // With no providers composed the materialized set is empty, and the empty projection is still prepared
        // deliberately: it is this preparer's single owned write of the recurring projection.
        var schedules = MaterializeSchedules(executable, _timeProvider.GetUtcNow(), activationId, slotId);
        await _store.PrepareActivationAsync(activationId, schedules, cancellationToken);
    }

    private IReadOnlyCollection<RecurringTriggerSchedule> MaterializeSchedules(
        WorkflowExecutable executable,
        DateTimeOffset now,
        string activationId,
        string slotId)
    {
        var artifactId = executable.Identity.ArtifactId;
        var schedules = new List<RecurringTriggerSchedule>();

        // Fully materialize every provider-owned recurring projection before the projection is mutated.
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
