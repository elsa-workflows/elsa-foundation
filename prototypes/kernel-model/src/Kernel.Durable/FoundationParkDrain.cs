namespace Kernel.Durable;

internal enum ParkClearKind { Discard, Expiry, Terminate, Delete }

internal sealed record ParkDiscardDrain(string Operation, ParkPageCursor? Cursor, long Cleared, long Serial, InboxKey Control)
{
    internal ParkClearKind Kind { get; init; }
    internal bool Expired => Kind == ParkClearKind.Expiry;
}

internal sealed record ParkDrainEnvelope(long Creation, long ParkRevision, long ParkAt, long Generation)
{
    internal const long Scope = 1L << 62;

    internal static ParkDrainEnvelope For(InstanceRow row, InMemoryStore.FoundationParkMark park) =>
        new(row.Creation, park.Revision, park.At, row.ParkResolutionGeneration);

    internal Message Issue(InstanceRow row, long serial, long? expiryAt = null, long? drainAt = null)
    {
        var key = Key(row, serial);
        return new Message(key.Id, new(row.Id), expiryAt is null ? Kinds.Drain : Kinds.ParkExpiry, Json.Write(this), row.Id,
                Due: expiryAt ?? drainAt, TargetCreation: row.Creation,
                SenderCreation: row.Creation)
            .WithInboxScope(key.Scope)
            .AsKernelTicket();
    }

    private InboxKey Key(InstanceRow row, long serial)
    {
        var origin = CreationProtocol.Origin(row.Id, row.Creation);
        return new(FormattableString.Invariant($"{origin}#park:{ParkRevision}:{ParkAt}:{Generation}:{serial}"), Scope);
    }

    internal bool Matches(Message message, InstanceRow row, InboxKey control, string kind = Kinds.Drain)
    {
        if (message.Kind != kind || message.InboxScope != Scope || message.EventMatch is not null ||
            message.InboxKey != control || message.Sender != row.Id || message.SenderCreation != Creation ||
            message.Target.Id != row.Id || message.TargetCreation != Creation || message.Start is not null)
            return false;

        try { return message.Payload is not null && message.Read<ParkDrainEnvelope>() == this; }
        catch (System.Text.Json.JsonException) { return false; }
    }

    internal bool MatchesIssued(Message message, InstanceRow row, ParkDiscardDrain drain) =>
        MatchesIssued(message, row, drain.Serial, drain.Control);

    internal bool MatchesIssued(Message message, InstanceRow row, long serial, InboxKey control) =>
        message.HasIssuedTicket && control == Key(row, serial) && Matches(message, row, control);

    internal bool MatchesExpiry(Message message, InstanceRow row, ParkExpiry expiry) =>
        expiry.Armed is { } key && key == Key(row, expiry.Serial) && Matches(message, row, key, Kinds.ParkExpiry);

    internal static bool IsInDomain(Message message) =>
        message.Kind is Kinds.Drain or Kinds.ParkExpiry or Kinds.Cascade || message.InboxScope == Scope;
}

public sealed partial class InMemoryStore
{
    internal bool AuthorizesDrainingTerminate(InstanceRow row, Message message)
    {
        if (message.Kind != Kinds.Terminate || message.Target.Id != row.Id || message.TargetCreation != row.Creation)
            return false;
        if (message.Operator is { } command)
        {
            if (command.Selection is null)
                return row is { Status: InstanceStatus.Running, ParkRecovery: not null };
            var identity = FoundationIdentity(row.Id, row.Creation);
            return MatchesFoundationSelection(identity.Tenant, identity.Reference, command.Selection);
        }
        return message.InboxKey.HasIssuedOrigin && CreationProtocol.MatchesCascadeParent(row, message);
    }

    private static bool IsParkControl(Message message) => message.Kind != Kinds.Cascade && ParkDrainEnvelope.IsInDomain(message);

    private (long At, bool Due)? ValidateParkDrain(TurnCommit turn, InstanceRow? previous,
        IReadOnlyDictionary<int, long> epochs, long now)
    {
        var next = turn.Row;
        var before = previous?.ParkDiscard;
        var after = next?.ParkDiscard;
        var interrupted = previous is { Status: InstanceStatus.Running, ParkRecovery: { } } &&
            next is { ParkRecovery: null } && after?.Kind == ParkClearKind.Terminate ? previous.ParkRecovery : null;
        var openingRecovery = previous?.ParkRecovery is null && next is { ParkRecovery: not null };
        if ((turn.FoundationRetry is not null || turn.FoundationRecoveryTimers is not null || turn.FoundationRecoveryJobs is not null ||
            turn.FoundationRecoverySubscriptions is not null) && !openingRecovery) throw InvalidDrain();
        if (previous is { ParkRecovery: not null } && next is { Status: InstanceStatus.Faulted, ParkRecovery: null } &&
            turn.FoundationKept is { Count: > 0 })
        {
            ValidateRecoveryInterruption(turn, previous, epochs);
            return null;
        }
        if (turn.FoundationRecoveryPage is not null || interrupted is null && previous?.ParkRecovery is not null &&
            turn.Consumed.Concat(turn.Done).Any(key => key == previous.ParkRecovery.Control))
        {
            ValidateRecoveryStep(turn, previous, epochs, now);
            return null;
        }
        if (openingRecovery)
            return ValidateRecoveryOpen(turn, previous, epochs, now);
        if (next?.ParkRecovery?.Deletion is { } deletion && previous?.ParkRecovery?.Deletion != deletion)
        {
            ValidateRecoveryDeleteTakeover(turn, previous, next, deletion, epochs, now);
            return null;
        }
        if (!turn.Delete && next is not null && next.EndedDuringParkRecovery != (previous?.EndedDuringParkRecovery ?? false))
            throw InvalidDrain();
        if (interrupted is null && (next is not null && next.ParkRecovery != previous?.ParkRecovery || previous?.ParkRecovery is not null &&
            (turn.Delete || turn.Consumed.Concat(turn.Done).Concat(turn.Held ?? []).Any(IsOutstandingDrain))))
            throw InvalidDrain();

        var retiring = before?.Kind == ParkClearKind.Delete && turn.Delete && after is null;
        if (before is not null && (turn.Delete && !retiring || turn.FoundationKept is { Count: > 0 })) throw InvalidDrain();
        var expiry = previous is null ? null : _foundationParkExpiries.GetValueOrDefault(new(previous.Id, previous.Creation));
        if (expiry?.Armed is not null && (turn.Delete || next is { Status: InstanceStatus.Running })) throw InvalidDrain();

        var takesOutstanding = turn.Consumed.Concat(turn.Done).Concat(turn.Held ?? [])
            .Any(key => IsOutstandingDrain(key) || IsOutstandingExpiry(key));
        if (!retiring && next is not null && (previous?.ParkTimerDispositions != next.ParkTimerDispositions || previous?.ParkRestartedJobs != next.ParkRestartedJobs) &&
            !(before is not null && after is null && next.ParkDiscarded && next.ParkTimerDispositions is null && next.ParkRestartedJobs is null))
            throw InvalidDrain();
        if (!retiring && !takesOutstanding && turn.FoundationParkClear is null && !turn.Sent.Any(IsParkControl) &&
            (next is null || turn.Delete || before == after &&
            (previous?.ParkDiscarded ?? false) == next.ParkDiscarded &&
            (previous?.ParkResolutionGeneration ?? 0) == next.ParkResolutionGeneration))
            return null;

        var terminating = before?.Kind == ParkClearKind.Terminate || after?.Kind == ParkClearKind.Terminate;
        var terminal = terminating && after is null;
        var runningTermination = terminating && previous?.Status == InstanceStatus.Running && (interrupted is not null || before is not null);
        var activeStatus = runningTermination ? InstanceStatus.Running : InstanceStatus.Faulted;
        if (next is null || next.Retired != retiring || next.Status != (terminal ? InstanceStatus.Cancelled : activeStatus) ||
            previous is not { Retired: false } || previous.Status != activeStatus || previous.Creation != next.Creation ||
            next.Revision != previous.Revision + 1 ||
            StoreBatchRules.AddressPartition(next.Id) is not { } partition || !epochs.ContainsKey(partition) ||
            _foundationParks.GetValueOrDefault(new(next.Id, next.Creation)) is not { } park)
            throw InvalidDrain();

        var inputs = turn.Consumed.Concat(turn.Done).ToArray();
        // Open recalls the previous resolution's control or the armed expiry in the command's commit.
        var recall = interrupted?.Control ?? expiry?.Armed;
        var recalled = inputs.Length == 2 && recall is { } armed && turn.Consumed.Contains(armed);
        if (inputs.Length != 1 && !recalled) throw InvalidDrain();
        var selected = recalled && inputs[0] == recall ? inputs[1] : inputs[0];
        if (Messages.GetValueOrDefault(selected) is not { Done: false, Message: { } input } ||
            input.Target.Id != next.Id || input.TargetCreation != next.Creation)
            throw InvalidDrain();

        var controls = turn.Sent.Where(IsParkControl).ToArray();
        var envelope = ParkDrainEnvelope.For(next, park);
        var deleting = before?.Kind == ParkClearKind.Delete || after?.Kind == ParkClearKind.Delete;
        if ((terminating || deleting) && (turn.Subscribed.Count != 0 || turn.Unsubscribed.Count != 0 || turn.Held is { Count: > 0 }))
            throw InvalidDrain();
        if (input.Kind == Kinds.ParkExpiry && after is null)
        {
            if (before is not null || next != (previous with { Revision = next.Revision }) ||
                expiry is null || !ValidExpiryInput() || controls is not [var replacement] ||
                !replacement.HasIssuedTicket || replacement.Due != expiry.At || replacement.KeepUntil is not null ||
                !envelope.MatchesExpiry(replacement, next, expiry with { Serial = checked(expiry.Serial + 1), Armed = replacement.InboxKey }) ||
                turn.FoundationParkClear is not null || turn.FoundationKept is not null || turn.Sent.Count != 1)
                throw InvalidDrain();
            if (now >= expiry.At)
                throw new StoreException(Failure.WindowExpired, "The Park expiry became due before the re-arm committed.");
            return (expiry.At, false);
        }
        if (before is null)
        {
            if (after is not { Cursor: null, Cleared: 0 } || next.ParkDiscarded ||
                next.ParkResolutionGeneration != previous.ParkResolutionGeneration + 1 || turn.FoundationParkClear is not null ||
                after.Serial != checked((interrupted?.Serial ?? expiry?.Serial ?? 0) + 1) || after.Operation != input.Id)
                throw InvalidDrain();

            if (after.Expired)
            {
                if (expiry is null || !ValidExpiryInput()) throw InvalidDrain();
                if (now < expiry.At)
                    throw new StoreException(Failure.WindowExpired, "The Park expiry is no longer due at the commit clock.");
            }
            else if ((recall is not null) != recalled)
                throw InvalidDrain();
            else if (after.Kind == ParkClearKind.Terminate)
            {
                if (!AuthorizesDrainingTerminate(previous, input) ||
                    interrupted is not null && (turn.Delete || turn.FoundationKept is not null ||
                        Messages.GetValueOrDefault(interrupted.Control) is not { Done: false, Message: { } oldControl } ||
                        !ParkDrainEnvelope.For(previous, park).MatchesIssued(oldControl, previous, interrupted.Serial, interrupted.Control)) ||
                    next != (previous with
                    {
                        Revision = next.Revision, ParkDiscard = after, ParkResolutionGeneration = next.ParkResolutionGeneration,
                        ParkRecovery = null, ParkCohort = null,
                        Jobs = null, Children = null, Cascade = next.Cascade
                    }) || !turn.Unsubscribe || turn.Journal != InterruptedRecoverJournal(interrupted) ||
                    turn.FoundationCascade is null && CascadeRegistry.From(previous).Count != 0 ||
                    turn.Sent.Count != controls.Length + turn.Sent.Count(message => message.CascadeOrigin || message.Kind == Kinds.Cascade))
                    throw InvalidDrain();
            }
            else if (after.Kind is not (ParkClearKind.Discard or ParkClearKind.Delete) ||
                input.Kind != (after.Kind == ParkClearKind.Delete ? Kinds.Delete : "kernel.discard") ||
                input.Operator?.Selection is not { } selection ||
                !_referenceBirths.TryGetValue(new(next.Id, next.Creation), out var binding) ||
                !MatchesFoundationSelection(binding.Tenant, binding.Reference, selection, requireTerminal: after.Kind == ParkClearKind.Delete))
                throw InvalidDrain();
            if (after.Kind == ParkClearKind.Delete && (turn.Sent.Count != 1 || turn.Journal is not null || turn.Unsubscribe ||
                next != (previous with { Revision = next.Revision, ParkDiscard = after, ParkResolutionGeneration = next.ParkResolutionGeneration })))
                throw InvalidDrain();
        }
        else if (!retiring && next.ParkResolutionGeneration != previous.ParkResolutionGeneration ||
            !ParkDrainEnvelope.For(previous, park).Matches(input, previous, before.Control))
            throw InvalidDrain();
        else if (after is null)
        {
            if (!retiring && !next.ParkDiscarded || controls.Length != 0 || turn.FoundationParkClear is not null ||
                ReadFoundationKeptPage(new(next.Id, next.Creation), park, after: null, limit: 1) is not { Count: 0 })
                throw InvalidDrain();
            if (terminal && (turn.Unsubscribe || !MatchesFinalNotice(turn.Sent, next) || next != (previous with
                {
                    Revision = next.Revision, Status = InstanceStatus.Cancelled, Suspended = false, ParkDiscard = null,
                    ParkDiscarded = true, ParkTimerDispositions = null, ParkRestartedJobs = null
                })))
                throw InvalidDrain();
            if (before.Kind == ParkClearKind.Delete)
            {
                var expired = previous with
                {
                    Revision = next.Revision, ParkDiscard = null, ParkDiscarded = true, ParkTimerDispositions = null, ParkRestartedJobs = null
                };
                // CreationProtocol.Validate already checked the submitted obligation against the prior registries.
                var expected = retiring ? CreationProtocol.Retire(previous, next.HeldCascade) : expired;
                if (next != expected || !MatchesFinalNotice(turn.Sent, expired) || turn.Unsubscribe != retiring ||
                    turn.Journal != ParkDeleteJournal(retiring, before))
                    throw InvalidDrain();
            }
            return null;
        }
        else if (turn.FoundationParkClear is not { } page || page.Park != park || next.ParkDiscarded ||
            after.Operation != before.Operation || after.Kind != before.Kind || after.Cursor != page.Rows[^1].Cursor || after.Cleared != before.Cleared + page.Rows.Count ||
            after.Serial != before.Serial + 1 ||
            !page.Rows.Select(row => row.Cursor).SequenceEqual(
                ReadFoundationKeptPage(new(next.Id, next.Creation), park, before.Cursor)!.Select(row => row.Cursor)))
            throw InvalidDrain();

        if ((terminating || deleting) && before is not null && (turn.Sent.Count != 1 || turn.Journal is not null || turn.Unsubscribe ||
            next != (previous with { Revision = next.Revision, ParkDiscard = after })))
            throw InvalidDrain();

        if (controls.Length != 1 || controls[0].Due is not null || controls[0].KeepUntil is not null ||
            !envelope.MatchesIssued(controls[0], next, after))
            throw InvalidDrain();
        return before is null && after.Expired && expiry is not null ? (expiry.At, true) : null;

        bool ValidExpiryInput() => expiry is not null && inputs.Length == 1 && turn.Consumed.Contains(selected) &&
            ParkDrainEnvelope.For(previous, park).MatchesExpiry(input, previous, expiry);
    }

    private static bool MatchesFinalNotice(IReadOnlyList<Message> sent, InstanceRow row) => row.Parent is null
        ? sent.Count == 0
        : sent is [var notice] && notice == FoundationChildNotice.Create(row, null, final: true);

    internal static string? InterruptedRecoverJournal(ParkRecoveryDrain? recovery) =>
        recovery is null ? null : Json.Write(new { Outcome = "interrupted", recovery.Operation });

    internal static string ParkDeleteJournal(bool retiring, ParkDiscardDrain drain) => retiring
        ? Json.Write(new { Outcome = "deleted", drain.Operation, drain.Cleared })
        : Json.Write(new { Outcome = "protected", Disposition = "discarded", drain.Operation, drain.Cleared });

    private bool IsOutstandingDrain(InboxKey key) =>
        key.Scope == ParkDrainEnvelope.Scope && Messages.GetValueOrDefault(key) is { Done: false, Message.Kind: Kinds.Drain } control &&
        Instances.GetValueOrDefault(control.Message.Target.Id) is { } row && control.Message.TargetCreation == row.Creation &&
        (row.ParkDiscard?.Control == key || row.ParkRecovery?.Control == key);

    private static StoreException InvalidDrain() =>
        new(Failure.Definite, "A Park drain must consume its exact input and advance the same fenced checkpoint.");
}
