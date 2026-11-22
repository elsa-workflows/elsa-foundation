using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Resolvers;

namespace Elsa.Workflows.Runtime.Services.Triggers;

/// <summary>Bounded pre-effect exact wait capture using the selected bookmark store's own index and physical owners.</summary>
internal static class RuntimeIngressWaitCapture
{
    internal static async ValueTask<RuntimeIngressWaitDelivery> ReadAsync(RuntimeStandaloneIngressSource source,
        RuntimeIngressWaitCandidate candidate, IBookmarkStimulusIndex index,
        Func<string, string, CancellationToken, ValueTask<BookmarkStateObservation?>> observe,
        IWorkflowExecutionStateStore executions, IActivityExecutionStateStore activities,
        IWorkflowExecutableStore material, IBookmarkResumeResolver? resolver, TimeProvider clock, CancellationToken token)
    {
        var page = await index.ListByStimulusPageAsync(new(candidate.StimulusType, candidate.StimulusHash, RuntimeStorePageRequest.MaximumLimit), token);
        if (page.NextContinuationToken is not null)
            throw new TriggerOperationException("wait-cohort-too-large", "The complete ordinary wait cohort exceeds the bounded source admission limit.");
        var selected = new List<RuntimeIngressWaitRecipient>();
        var observations = new List<RuntimeIngressWaitObservation>();
        var now = clock.GetUtcNow();
        var correlation = candidate.Metadata.GetValueOrDefault(RuntimeMetadataKeys.CorrelationId);
        foreach (var listed in page.Items)
        {
            if (listed.ExpiresAt <= now) continue;
            var execution = await executions.FindAsync(listed.WorkflowExecutionId, token) ??
                throw new TriggerOperationException("unavailable", "An ordinary wait owner is temporarily unavailable.");
            if (execution.TenantId != source.TenantId || execution.RunKind == WorkflowRunKind.TestRun || execution.TestScope is not null) continue;
            var observation = await observe(listed.WorkflowExecutionId, listed.BookmarkId, token) ??
                throw new TriggerOperationException("conflict", "The ordinary wait cohort changed before source acceptance.");
            if (observation.Scope is { } physicalScope && physicalScope != source.Scope)
                throw new InvalidDataException("An ordinary wait observation crossed the selected physical scope.");
            if (observation.State.StimulusType != candidate.StimulusType || observation.State.StimulusHash != candidate.StimulusHash)
                throw new TriggerOperationException("conflict", "The ordinary wait stimulus changed before source acceptance.");
            if (!GlobalBookmarkStimulusLookupResult.CorrelationMatches(observation.State, correlation)) continue;
            var activity = await activities.FindAsync(listed.WorkflowExecutionId, observation.State.ActivityExecutionId, token) ??
                throw new TriggerOperationException("unavailable", "An ordinary wait activity is temporarily unavailable.");
            var expected = StimulusResumeRecipient.From(observation, execution, source.Scope);
            if (!expected.MatchesAdmission(observation, execution, activity, source.Scope, now)) continue;
            var partition = execution.Partition ?? new WorkflowExecutionPartition(WorkflowExecutionPartition.DefaultValue);
            if (partition.Value != source.Scope.Value || partition != source.Partition)
                throw new NotSupportedException("The actual ordinary wait partition cannot be targeted by this source namespace.");
            var executable = await material.FindAsync(execution.PinnedExecutable.ArtifactId, token) ??
                throw new TriggerOperationException("unavailable", "An ordinary wait executable is temporarily unavailable.");
            if (!WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(executable.Identity, execution.PinnedExecutable))
                throw new InvalidDataException("An ordinary wait executable differs from its physical owner pin.");
            // Recheck the same prerequisites inside the provider's protected acceptance boundary too. A bookmark
            // alone is not a resumable typed suspension: the ordinary callback otherwise drops its command.
            var resolution = (resolver ?? new BookmarkResumeResolver()).Resolve(new BookmarkResumeRequest(execution, executable, observation.State, candidate.Input));
            var identity = new RuntimeIngressWaitIdentity(1, source.Scope, source.OutboxItemId, expected.WorkflowExecutionId, expected.BookmarkId, observation.Generation);
            RequireResumable(activity, observation.State, resolution.ExecutableNode, candidate, identity);
            observations.Add(RuntimeIngressWaitObservation.From(observation));
            selected.Add(new(identity, expected));
        }
        if (candidate.ObservedWaits is { } authorized && (authorized.Count != observations.Count ||
            authorized.Any(expected => !observations.Contains(expected))))
            throw new TriggerOperationException("conflict", "The authorized ordinary wait cohort changed before source acceptance; authorize the current request again.");
        return new(1, source.OutboxItemId, source.Fingerprint, source.Scope, now, candidate, selected,
            selected.Select(recipient => new RuntimeIngressWaitProgress(recipient.WaitId, RuntimeIngressWaitPhase.Pending)).ToArray());
    }

    private static void RequireResumable(ActivityExecutionState state, BookmarkState bookmark, ExecutableNode node,
        RuntimeIngressWaitCandidate candidate, RuntimeIngressWaitIdentity identity)
    {
        try { state.EnsureValueFlowCompatible(); }
        catch (InvalidOperationException exception) { throw Unresumable(exception); }
        if (state.Completion is not null || node.ActivityContract is not { } contract || state.PrivateState is not { } privateState ||
            privateState.InvocationId != state.InvocationId || privateState.Value.Presence != ValuePresence.ExplicitNull &&
            (privateState.Value.Presence != ValuePresence.Present || privateState.Value.InlineValue is null) ||
            state.InputSnapshot is not { } snapshot || snapshot.InvocationId != state.InvocationId || snapshot.ContractFingerprint != contract.SchemaFingerprint ||
            state.ContractIdentity is not { } pinned || pinned.ActivityTypeKey != contract.ActivityTypeKey ||
            pinned.ContractVersion != contract.ContractVersion || pinned.SchemaFingerprint != contract.SchemaFingerprint)
            throw Unresumable();
        var registrations = state.TriggerRegistrations?.Where(registration => registration.RegistrationId == bookmark.BookmarkId).ToArray() ?? [];
        if (registrations.Length != 1) throw Unresumable();
        var registration = registrations[0];
        // An untyped source inherits the actual registration type, just as ordinary stimulus resume does.
        var payloadType = candidate.PayloadType ?? registration.PayloadType;
        if (registration.InvocationId != state.InvocationId || registration.ResumeTargetKey != bookmark.ResumeTargetId ||
            registration.StimulusType != bookmark.StimulusType || registration.StimulusHash != bookmark.StimulusHash ||
            registration.PayloadType.Alias != payloadType.Alias || registration.PayloadType.CollectionKind != payloadType.CollectionKind ||
            registration.PayloadType.SchemaVersion != payloadType.SchemaVersion || registration.PayloadType.Schema?.GetRawText() != payloadType.Schema?.GetRawText())
            throw Unresumable();
        if (state.TriggerDeliveries?.Any(delivery => delivery.RegistrationId == registration.RegistrationId &&
            delivery.Status is ActivityTriggerDeliveryStatus.Received or ActivityTriggerDeliveryStatus.Consumed &&
            (delivery.DeliveryId == identity.CommandId || registration.DeduplicationPolicy == ActivityTriggerDeduplicationPolicy.Once ||
             delivery.DeduplicationKey == identity.IdempotencyKey)) == true)
            throw Unresumable();
        // A fresh source has no committed command to redeliver. Claimed-resume recovery belongs to the existing
        // accepted source and ordinary handler, never a new intake with already consumed trigger identity.
    }

    private static TriggerOperationException Unresumable(Exception? inner = null) =>
        new("wait-not-resumable", "An ordinary wait has incompatible or incomplete typed suspension state; repair it before accepting this source.", innerException: inner);

    internal static void Compare(RuntimeIngressWaitDelivery expected, RuntimeIngressWaitDelivery current)
    {
        if (expected.Candidate.Fingerprint != current.Candidate.Fingerprint || expected.Recipients.Count != current.Recipients.Count ||
            expected.Recipients.Any(recipient => current.Recipients.SingleOrDefault(candidate => candidate.WaitId == recipient.WaitId)?.IsEquivalentTo(recipient) != true))
            throw new TriggerOperationException("conflict", "The complete ordinary wait cohort changed before source acceptance.");
    }

    internal static RuntimePostCommitOutboxItem Attach(RuntimePostCommitOutboxItem candidate, RuntimeIngressWaitDelivery waits)
    {
        var selected = candidate.SubscriptionDelivery!;
        var complete = new RuntimeSubscriptionDelivery(selected.Version, selected.SourceOutboxItemId, selected.SourceFingerprint, selected.Scope,
            selected.Recipients, selected.Refusals, selected.Progress, resourceCaptures: selected.ResourceCaptures, waitDelivery: waits);
        return RuntimePostCommitOutboxItem.CreateStandaloneIngress(candidate.StandaloneIngress!, complete, candidate.RetryPolicy);
    }
}
