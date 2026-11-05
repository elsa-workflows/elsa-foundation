using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Services.History;

/// <summary>Composition-only qualification, resolved lazily so reading retained metadata does not activate an actor.</summary>
internal sealed class RuntimeHistorySourceAvailability(IServiceProvider services) : IRuntimeHistorySourceContributor
{
    internal ValueTask<RuntimeCheckpointCommit> AddFactsAsync(RuntimeCheckpointCommit commit,
        Func<IEnumerable<RuntimeHistorySourceFact>> facts, CancellationToken cancellationToken) => AddFactsAsync(commit, _ => facts(), cancellationToken);

    public async ValueTask<RuntimeCheckpointCommit> AddFactsAsync(RuntimeCheckpointCommit commit,
        Func<RuntimeHistoryCapturePolicy, IEnumerable<RuntimeHistorySourceFact>> facts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit); ArgumentNullException.ThrowIfNull(facts);
        WorkflowExecutionState? native;
        try { native = await services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(commit.WorkflowExecutionId, cancellationToken); }
        catch (Exception exception) when (exception is IOException or TimeoutException or NotSupportedException)
        { throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "The semantic source could not read its admitted native policy.", exception); }
        var policy = native is not null ? native.HistoryCapturePolicy : commit.StateChanges.WorkflowExecution?.State.HistoryCapturePolicy;
        if (!RuntimeHistorySourceFacts.IsFinalProfile(policy)) return commit;
        var combined = new List<RuntimeHistorySourceFact>(commit.HistorySourceFacts ?? []);
        foreach (var fact in facts(policy!))
        {
            var existing = combined.SingleOrDefault(item => item.Kind == fact.Kind && item.SubjectId == fact.SubjectId);
            if (existing is null) combined.Add(fact);
            else if (JsonSerializer.Serialize(existing) != JsonSerializer.Serialize(fact))
                throw new InvalidOperationException("One checkpoint owner supplied conflicting semantic facts.");
        }
        return commit with { HistorySourceFacts = RuntimeHistorySourceFacts.ForPolicy(policy, combined) };
    }

    internal bool OwnerObservations(RuntimeHistorySourceSupport owner)
    {
        try { return owner.ObservationIntentKind is { } kind && services.GetService<IRuntimeHistoryObservationWriter>() is { } writer &&
            services.GetService<IRuntimeHistoryStore>() is { } reader && writer.CanRecordHistory(reader) &&
            services.GetService<IRuntimePostCommitIntentDispatcher>()?.CanRecordHistory(kind, writer, reader) == true; }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException) { return false; }
    }
    internal bool Values() => services.GetService<IRuntimeActivityExecutionInspectionAccumulator>()?.SupportsCheckpointValueCapture == true;
    internal bool Delivery()
    {
        try { return services.GetService<IRuntimeHistoryObservationWriter>() is { } writer && services.GetService<IRuntimeHistoryStore>() is { } reader &&
            writer.CanRecordHistory(reader) && services.GetService<IBookmarkResumeDispatcher>() is IRuntimeHistoryDeliverySource source && source.CanRecordHistory(writer, reader); }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException) { return false; }
    }
    internal bool Timer()
    {
        try { return services.GetService<IRuntimeHistoryObservationWriter>() is { } writer && services.GetService<IRuntimeHistoryStore>() is { } reader &&
            writer.CanRecordHistory(reader) && services.GetService<IDurableTimerStore>() is IDurableTimerHistoryStore { SupportsHistoryResponsibility: true } store &&
            services.GetService<IDurableTimerScheduler>() is IRuntimeHistoryTimerSource source && source.CanRecordHistory(store); }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException) { return false; }
    }
}
