using System.Text.Json;
using Kernel.Runtime;

namespace Kernel.Durable;

/// <summary>Reject malformed row mutations before any effect; one row mutation per actor per batch.</summary>
public static class StoreBatchRules
{
    public static void Validate(CommitBatch batch,bool deferUpdateAliases=false,bool deferInsertAliases=false,bool ownedSets=false,
        bool supportsJobTrialClear=false,bool supportsFoundationParkResolution=false,bool supportsFoundationValues=false,bool supportsDueBacklog=false)
    {
        if (batch.ClearJobTrials.Count > 0 && (!supportsJobTrialClear || batch.HandOver || batch.Turns.Count > 0 ||
            batch.Seen.Count > 0 || batch.Trial.Count > 0 || batch.ClearJobTrials.Distinct().Count() != batch.ClearJobTrials.Count))
            throw new StoreException(Failure.Definite, "A job trial clear requires supported, distinct keys in a separate fenced batch.");
        // A ticket scope is its origin's string length plus one. Physical key encodings
        // and other issued key domains cannot appear in a logical trial clear.
        if (batch.ClearJobTrials.Any(key => key is not { Scope: > 0 and <= (long)int.MaxValue + 1, Match: null }))
            throw new StoreException(Failure.Definite, "A job trial clear requires logical ticket keys.");
        if (batch.PublicationReservation is { } publication && (!supportsFoundationParkResolution ||
            batch.Trial is not [var key] || key != publication.Key || batch.Seen.Count != 0 || batch.Turns.Count != 0 ||
            batch.ClearJobTrials.Count != 0))
            throw new StoreException(Failure.Definite, "A publication reservation requires its own fenced commit.");
        if (batch.Fences is { } fences) ValidateFences(batch,fences,ownedSets);
        if (batch.DueBacklog is { Count: > 0 } backlog &&
            (!supportsDueBacklog || backlog.Any(estimate => estimate.Value < 0 ||
                (batch.Fences?.Any(fence => fence.Partition == estimate.Key) ?? (batch.Partition == estimate.Key)) == false)))
            throw new StoreException(Failure.Definite, "A due-backlog estimate requires its partition's fence and a supported store.");
        var changed = new HashSet<string>(StringComparer.Ordinal);
        var rows = new HashSet<string>(StringComparer.Ordinal);
        HashSet<InboxKey>? consumedControls = null;
        var removed = batch.Turns.Any(turn => turn.FoundationReleasedHolds is { Count: > 0 })
            ? batch.Turns.SelectMany(turn => turn.Consumed.Concat(turn.Done)).ToHashSet() : null;
        ValidateParkPages(batch);
        if (batch.Turns.Any(turn => turn.FoundationCatchUpFailed is not null) &&
            (batch.Turns.Count != 1 || batch.Seen.Count != 0 || batch.Trial.Count != 0 || batch.ClearJobTrials.Count != 0))
            throw new StoreException(Failure.Definite, "A catch-up failure is recorded in its own fenced commit.");
        if (batch.Turns.Any(turn => turn.FoundationPublicationSequence is not null) &&
            (batch.Turns.Count != 1 || batch.Seen.Count != 0 || batch.Trial.Count != 0 || batch.ClearJobTrials.Count != 0))
            throw new StoreException(Failure.Definite, "A publication page requires its own fenced commit.");
        if (batch.Turns.Any(turn => turn.FoundationRecoverySubscriptions is { Count: > 0 }))
        {
            var restoring = batch.Turns.Where(turn => turn.FoundationRecoverySubscriptions is { Count: > 0 })
                .Select(turn => turn.Instance).ToHashSet(StringComparer.Ordinal);
            if (batch.Turns.Any(turn => (turn.FoundationRecoverySubscriptions is null &&
                    turn.Subscribed.Concat(turn.Unsubscribed).Any(row => restoring.Contains(row.Instance))) ||
                    turn.Unsubscribe && restoring.Contains(turn.Instance)))
                throw new StoreException(Failure.Definite, "Other writes in a batch cannot change subscriptions recovery restores.");
        }
        if (batch.Turns.Any(turn => turn.FoundationKept is { Count: > 0 }))
        {
            var kept = batch.Turns.SelectMany(turn => turn.FoundationKept?.Keys ?? []).ToHashSet();
            if (batch.Turns.SelectMany(turn => turn.Consumed.Concat(turn.Done).Concat(turn.Held ?? [])).Concat(batch.Trial)
                .Where(kept.Contains).GroupBy(key => key).Any(group => group.Skip(1).Any()))
                throw new StoreException(Failure.Definite, "A Park's kept delivery may be mutated only once in a batch.");
        }
        foreach (var turn in batch.Turns)
        {
            if (turn.FoundationValues is { Count: > 0 } &&
                (!supportsFoundationValues || turn.Delete || turn.Row is not { Retired: false, Creation: > 0 } valueOwner ||
                    valueOwner.Id != turn.Instance))
                throw new StoreException(Failure.Definite, "Values require a supported store and a checkpoint of the same creation.");
            if (!supportsFoundationParkResolution && (turn.FoundationRetry is not null || turn.FoundationParkClear is not null || turn.FoundationRecoveryPage is not null || turn.FoundationRecoveryTimers is not null || turn.FoundationRecoveryJobs is not null ||
                turn.FoundationRecoverySubscriptions is not null || turn.FoundationCatchUpStep is not null ||
                turn.FoundationCatchUpFailed is not null || turn.FoundationCatchUpBefore is not null ||
                turn.FoundationCatchUpExpected is not null || turn.FoundationPublicationSequence is not null ||
                turn.FoundationCascade is not null || turn.Row?.HasCascadeObligation == true ||
                turn.Row is { ParkResolutionGeneration: > 0 } or { ParkRecovery: not null } || turn.Sent.Any(ParkDrainEnvelope.IsInDomain)))
                throw new StoreException(Failure.Definite, "This store does not support Park resolution.");
            if (turn.FoundationReleasedHolds is { Count: > 0 } releases &&
                (turn.Row is null || releases.Distinct().Count() != releases.Count || releases.Any(removed!.Contains) ||
                    releases.Intersect(turn.Held ?? []).Any()))
                throw new StoreException(Failure.Definite, "Held releases must name distinct, unconsumed rows at a checkpoint.");
            if (turn.Sent.Any(Kinds.IsUnissuedPrivate))
                throw new StoreException(Failure.Definite, Kinds.UnissuedPrivateKind);
            if ((batch.Seen.Count != 0 || batch.Trial.Count != 0) && turn.Sent.Any(message => ParkDrainEnvelope.IsInDomain(message) &&
                    (batch.Seen.Contains(message.InboxKey) || batch.Trial.Contains(message.InboxKey))))
                throw new StoreException(Failure.Definite, "A new Park control cannot also carry a poll or trial mark.");
            if (turn.Consumed.Concat(turn.Done).Any(key => key.Scope == ParkDrainEnvelope.Scope &&
                    !(consumedControls ??= []).Add(key)) ||
                (turn.Held ?? []).Any(key => key.Scope == ParkDrainEnvelope.Scope))
                throw new StoreException(Failure.Definite, "Each protected control may be consumed only once and may not be held.");
            if (turn.Delete)
            {
                if (turn.Expected is not { } expected)
                    throw new StoreException(Failure.Definite,"Delete requires the existing row's exact revision.");
                if (turn.Row is { } marker && (turn.Journal is null || !marker.Retired || marker.Creation < 0 ||
                    marker.Creation == 0 && !CreationProtocol.IsPhysicalAddress(marker.Id) ||
                    marker.Status == InstanceStatus.Running || expected == long.MaxValue ||
                    marker != new InstanceRow(marker.Id, "", "", marker.Status, "null", expected + 1,
                        marker.HeldCascade is null ? null : marker.Parent, History: History.Minimal, Creation: marker.Creation,
                        Retired: true, ParentCreation: marker.HeldCascade is null ? 0 : marker.ParentCreation)
                        { Cascade = marker.Cascade, HeldCascade = marker.HeldCascade }))
                    throw new StoreException(Failure.Definite,"A row-bearing Delete must be a canonical retirement marker.");
            }
            if (turn.Row is { } value) ValidateIdentity(value);
            if (turn.Row is { } row && !StringComparer.Ordinal.Equals(turn.Instance,row.Id) &&
                !(turn.Expected is null?deferInsertAliases:deferUpdateAliases))
                throw new StoreException(Failure.Definite,"Turn and row must name the same physical actor.");
            if (turn.Row is { } mutation && !rows.Add(mutation.Id))
                throw new StoreException(Failure.Definite,"A batch cannot mutate the same row twice.");
            if ((turn.Row is not null || turn.Delete) && !changed.Add(turn.Instance))
                throw new StoreException(Failure.Definite,"A batch cannot mutate the same actor row twice.");
        }
    }

    private static void ValidateParkPages(CommitBatch batch)
    {
        HashSet<InboxKey>? changed = null;
        HashSet<InboxKey>? preserved = null;
        HashSet<InboxKey>? selected = null;
        foreach (var turn in batch.Turns)
        {
            if (turn.FoundationRetry is { } retry)
            {
                selected ??= [];
                if (retry.Marked.Count > InMemoryStore.MaxRetryHandles || retry.Marked.Any(row => !selected.Add(row.Key)))
                    throw new StoreException(Failure.Definite, "A Park retry must mark a bounded set of distinct keys.");
            }
            if (turn.FoundationRecoveryTimers is { Preserved.Count: > 0 } observedTimers)
                (preserved ??= []).UnionWith(observedTimers.Preserved);
            if (turn.FoundationRecoveryJobs is { } observedJobs && (observedJobs.Pending.Count != 0 || observedJobs.Retained.Count != 0))
            {
                (preserved ??= []).UnionWith(observedJobs.Pending);
                preserved.UnionWith(observedJobs.Retained);
            }
            if (turn.FoundationRecoveryTimers is { Rearms.Count: > 0 } timers)
            {
                changed ??= [];
                if (timers.Rearms.Any(rearm => !changed.Add(rearm.Envelope.InboxKey)))
                    throw new StoreException(Failure.Definite, "Each re-armed timer must have a distinct full key.");
            }
            if (turn.FoundationRecoveryJobs is { Writes.Count: > 0 } jobs)
            {
                changed ??= [];
                if (jobs.Writes.Any(job => !changed.Add(job.InboxKey)))
                    throw new StoreException(Failure.Definite, "Each restarted job write must have a distinct full key.");
            }
            if (turn.FoundationRecoveryJobs is { DeadlineRearms.Count: > 0 } jobDeadlines)
            {
                changed ??= [];
                if (jobDeadlines.DeadlineRearms.Any(deadline => !changed.Add(deadline.Envelope.InboxKey)))
                    throw new StoreException(Failure.Definite, "Each re-armed job deadline must have a distinct full key.");
            }
            var rows = turn.FoundationParkClear?.Rows ?? turn.FoundationRecoveryPage?.Writes().ToArray();
            if (rows is null) continue;
            changed ??= [];
            if ((turn.FoundationParkClear is not null && turn.FoundationRecoveryPage is not null) ||
                turn.OpensPark || turn.Delete || rows.Count is 0 or > InMemoryStore.ParkPageLimit ||
                rows.Any(row => !changed.Add(row.Cursor.Key)))
                throw new StoreException(Failure.Definite, "A Park page must write a bounded set of distinct keys.");
        }
        if (preserved is not null && ((changed is not null && preserved.Overlaps(changed)) ||
            batch.Turns.SelectMany(turn => turn.Consumed.Concat(turn.Done).Concat(turn.Held ?? [])
                .Concat(turn.FoundationReleasedHolds ?? []).Concat(turn.Sent.Select(message => message.InboxKey))).Any(preserved.Contains)))
            throw new StoreException(Failure.Definite, "Other writes in a batch cannot change a wait that recovery preserves.");
        if (selected is not null)
        {
            changed ??= [];
            if (selected.Any(key => !changed.Add(key)))
                throw new StoreException(Failure.Definite, "A selected row cannot also change elsewhere in the recovery batch.");
        }
        if (changed is not null && batch.Seen.Concat(batch.Trial).Concat(batch.ClearJobTrials).Concat(batch.Turns.SelectMany(turn =>
                turn.Consumed.Concat(turn.Done).Concat(turn.Held ?? []).Concat(turn.FoundationReleasedHolds ?? [])
                    .Concat(turn.Sent.Select(message => message.InboxKey))))
            .Any(changed.Contains))
            throw new StoreException(Failure.Definite, "A Park page input cannot be changed elsewhere in the batch.");
    }

    /// <summary>
    /// An owned-set batch, one fence or several, fences each partition once, names its lowest one positionally, and
    /// each turn's address embeds one of the fenced partitions. Over several partitions it needs a store that fences k rows.
    /// </summary>
    private static void ValidateFences(CommitBatch batch,IReadOnlyList<PartitionFence> fences,bool ownedSets)
    {
        var lowest=fences.MinBy(f=>f.Partition);
        if (lowest is null || fences.Select(f=>f.Partition).Distinct().Count()!=fences.Count ||
            lowest!=new PartitionFence(batch.Partition,batch.Epoch,batch.Position))
            throw new StoreException(Failure.Definite,"An owned batch fences each of its partitions once and names the lowest.");
        var fenced=fences.Select(f=>f.Partition).ToHashSet();
        foreach (var turn in batch.Turns.Where(t => !TicketsOnly(t)))
            if (AddressPartition(turn.Instance) is not { } partition || !fenced.Contains(partition))
                throw new StoreException(Failure.Definite,$"Turn {turn.Instance} is outside the batch's fenced partitions.");
        if (fences.Count>1 && !ownedSets) throw new StoreException(Failure.Definite,"This store fences one partition per commit.");
    }

    /// <summary>An owned set names each partition once: a set poll or renew with a repeated partition is refused (a repeated partition in a release is harmless).</summary>
    public static void RequireDistinct(IReadOnlyCollection<PartitionFence> owned)
    {
        if (owned.Select(f=>f.Partition).Distinct().Count()!=owned.Count)
            throw new StoreException(Failure.Definite,"An owned set names each of its partitions once.");
    }

    /// <summary>The owner's turn of no actor that only consumes tickets (meet and publish): the store checks each ticket's partition.</summary>
    public static bool TicketsOnly(TurnCommit turn) => turn.Instance.Length == 0 && turn.Row is null && turn.Expected is null && !turn.Delete && turn.Journal is null
        && turn.Sent.Count == 0 && turn.Subscribed.Count == 0 && turn.Unsubscribed.Count == 0 && !turn.Unsubscribe && turn.Held is not { Count: > 0 };

    /// <summary>The partition an actor address embeds, or null when it embeds none.</summary>
    public static int? AddressPartition(string id)
    {
        try { return new InstanceRef(id).Partition; }
        catch (Exception e) when (e is FormatException or OverflowException or ArgumentOutOfRangeException) { return null; }
    }

    internal static void ValidateActorSends(TurnCommit turn, InstanceRow? previous)
    {
        IReadOnlyCollection<CreationAddress>? children = null;
        foreach (var message in turn.Sent)
        {
            if (message.CascadeOrigin && (message.Kind != Kinds.Terminate || !message.HasIssuedTicket ||
                message.InboxScope != InboxKey.Issued(message.Id).Scope || (turn.Row ?? previous) is not { } issuer ||
                message.Sender != issuer.Id || message.SenderCreation != issuer.Creation || issuer.Id != turn.Instance))
                throw new StoreException(Failure.Definite, "A cascade terminate requires an issued origin from the committing creation.");
            if (!message.ActorControl) continue;
            if (message.Kind == Kinds.Terminate && message.TargetCreation is > 0 && !RequestsOperatorDelete(message.Payload))
            {
                children ??= turn.FoundationCascade is { Input: null } opening ? opening.Entries.Children :
                    ChildRegistry.Read(turn.Row is { } row ? row.Children : previous?.Children);
                if (children.Contains(new CreationAddress(message.Target.Id, message.TargetCreation.Value))) continue;
            }

            throw new StoreException(Failure.Definite, "Actor turns may send only Terminate to a registered child creation.");
        }
    }

    private static bool RequestsOperatorDelete(string? payload)
    {
        if (payload is null) return false;
        try
        {
            using var body = JsonDocument.Parse(payload);
            return body.RootElement.ValueKind == JsonValueKind.Object &&
                body.RootElement.EnumerateObject().Any(property =>
                    string.Equals(property.Name, "ThenDelete", StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.True);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static void ValidateIdentity(InstanceRow row)
    {
        if (row.FoundationNullState && (row.State != "null" || row.Retired || row.IdentityOnly))
            throw new StoreException(Failure.Definite, "An absent actor state requires the null placeholder on an actor checkpoint.");
        if (row.IdentityMetadataRequired && !row.IdentityOnly)
            throw new StoreException(Failure.Definite,"A terminal metadata protocol requires an identity-only row.");
        if (row is { IdentityOnly:true } identity && (identity.Retired || identity.Status==InstanceStatus.Running || identity.Creation!=0 || identity.Parent is not null ||
            identity.State!="null" || !CreationProtocol.IsPhysicalAddress(identity.Id) || !identity.Id.Contains("/key/",StringComparison.Ordinal)))
            throw new StoreException(Failure.Definite,"Identity-only rows must be state-free ended keyed runs.");
    }

    public static void RequirePhysicalCapability(CommitBatch batch,bool available)
    {
        if (!available && batch.Turns.Any(t=>t.Row is { Retired:true,Creation:0 }))
            throw new StoreException(Failure.Definite,"Store has no physical retirement capability.");
    }
}
