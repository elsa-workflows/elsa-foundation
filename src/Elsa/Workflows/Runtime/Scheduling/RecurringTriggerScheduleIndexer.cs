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
        var schedules = MaterializeSchedules(executable, now, publicationId: null, slotId: null);

        var bindings = await _inner.IndexAsync(executable, cancellationToken);
        await _store.DeleteByArtifactAsync(artifactId, cancellationToken);
        foreach (var schedule in schedules)
            await _store.SaveAsync(schedule, cancellationToken);

        return bindings;
    }

    public async ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> PreparePublicationAsync(
        WorkflowExecutable executable,
        string publicationId,
        string slotId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);

        if (_providers.Count == 0)
            return await _inner.PreparePublicationAsync(executable, publicationId, slotId, cancellationToken);

        var schedules = MaterializeSchedules(executable, _timeProvider.GetUtcNow(), publicationId, slotId);
        var bindings = await _inner.PreparePublicationAsync(executable, publicationId, slotId, cancellationToken);
        await _store.PreparePublicationAsync(publicationId, schedules, cancellationToken);
        return bindings;
    }

    private IReadOnlyCollection<RecurringTriggerSchedule> MaterializeSchedules(
        WorkflowExecutable executable,
        DateTimeOffset now,
        string? publicationId,
        string? slotId)
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
                    ScheduleId: BuildScheduleId(publicationId, artifactId, node.ExecutableNodeId, descriptor.StimulusHash, fanOut),
                    ArtifactId: artifactId,
                    StimulusType: descriptor.StimulusType,
                    StimulusHash: descriptor.StimulusHash,
                    Kind: descriptor.Kind,
                    Expression: descriptor.Expression,
                    NextOccurrence: next.Value,
                    CreatedAt: now,
                    PublicationId: publicationId,
                    SlotId: slotId,
                    IsActive: publicationId is null));
            }
        }

        return schedules;
    }

    private static WorkflowTriggerPreflightException Failure(string artifactId, ExecutableNode node, IReadOnlyCollection<string> providerIds, string facet, string message, Exception? innerException = null) =>
        new(artifactId, node.ExecutableNodeId, node.ActivityType, providerIds, facet, message, innerException);

    private static string BuildScheduleId(string? publicationId, string artifactId, string executableNodeId, string stimulusHash, bool fanOut) =>
        (publicationId, fanOut) switch
        {
            (null, false) => RecurringTriggerSchedule.BuildId(artifactId, executableNodeId),
            (null, true) => RecurringTriggerSchedule.BuildFanOutId(artifactId, executableNodeId, stimulusHash),
            (not null, false) => RecurringTriggerSchedule.BuildId(publicationId, artifactId, executableNodeId),
            (not null, true) => RecurringTriggerSchedule.BuildFanOutId(publicationId, artifactId, executableNodeId, stimulusHash)
        };

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
