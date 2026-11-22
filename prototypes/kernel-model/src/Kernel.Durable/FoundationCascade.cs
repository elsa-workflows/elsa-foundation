using System.Collections.Immutable;
using System.Text.Json;

namespace Kernel.Durable;

/// <summary>The exact jobs and child creations a terminated actor handed to the durable layer.</summary>
internal sealed record CascadeRegistry(ImmutableArray<JobRegistration> Jobs, ImmutableArray<CreationAddress> Children)
{
    internal const int ChildBytes = 384;
    internal const int JobBytes = 12_416;
    internal const int MaxBytes = 16_441_344;
    internal int Count => checked(Jobs.Length + Children.Length);
    internal long StorageBytes => ChildBytes * (long)Children.Length + JobBytes * (long)Jobs.Length;
    internal static CascadeRegistry From(InstanceRow row) =>
        new([.. JobRegistry.Read(row.Jobs)], [.. ChildRegistry.Read(row.Children)]);
    internal static CascadeRegistry? ForRetirement(InstanceRow row) =>
        row is { Creation: > 0, Parent: not null, Cascade: null } ? From(row) : null;
}

internal sealed record CascadeDischarge(CascadeRegistry Entries, int Cursor, long Serial, InboxKey Control);
internal sealed record CascadePage(CascadeDischarge? Next, IReadOnlyList<JobRegistration> Jobs, IReadOnlyList<Message> Sent);
internal sealed record CascadeCommit(CascadeRegistry Entries, InboxKey? Input = null);

internal static class CascadeProtocol
{
    internal const int PageLimit = 1_000;

    internal static TurnOutcome Open(TurnOutcome outcome, CascadeRegistry registry)
    {
        if (registry.Count == 0) return outcome;
        var row = outcome.Row ?? throw new InvalidOperationException("A cascade starts at a committed end.");
        var page = Page(row, registry, 0, row.Revision);
        outcome.Row = row with { Cascade = page.Next, Jobs = null, Children = null };
        outcome.FoundationCascade = new(registry);
        outcome.Sent.AddRange(page.Sent);
        return outcome;
    }

    internal static TurnOutcome Continue(InstanceRow row, Delivery input)
    {
        var discharge = row.Cascade!;
        var next = row with { Revision = checked(row.Revision + 1) };
        var page = Page(next, discharge.Entries, discharge.Cursor, discharge.Serial);
        return new(row.Id)
        {
            Row = next with { Cascade = page.Next }, Expected = row.Revision, Revision = next.Revision,
            Status = row.Status, Consumed = [input], Sent = [.. page.Sent],
            FoundationCascade = new(discharge.Entries, discharge.Control)
        };
    }

    internal static TurnOutcome OpenRetired(InstanceRow row, Delivery input)
    {
        var refusal = CreationProtocol.Refuse(row.Id, row, [input], "RetiredCreation");
        refusal.Row = row with { HeldCascade = null, Revision = checked(row.Revision + 1) };
        refusal.Expected = row.Revision;
        refusal.Revision = refusal.Row.Revision;
        return Open(refusal, row.HeldCascade!);
    }

    internal static bool MatchesRetiredTerminate(InstanceRow row, Message command) =>
        row is { Retired: true, HeldCascade: not null } &&
        command is { Kind: Kinds.Terminate, CascadeOrigin: true, HasIssuedTicket: true } &&
        command.Target.Id == row.Id && command.TargetCreation == row.Creation && CreationProtocol.MatchesCascadeParent(row, command);

    internal static CascadePage Page(InstanceRow row, CascadeRegistry registry, int cursor, long serial)
    {
        if (cursor < 0 || cursor >= registry.Count || serial < 0)
            throw new InvalidOperationException("A cascade page needs an outstanding entry.");

        var end = cursor + Math.Min(PageLimit, registry.Count - cursor);
        var jobs = registry.Jobs.Skip(cursor).Take(end - cursor).ToArray();
        var sent = new List<Message>();
        var origin = CreationProtocol.Origin(row.Id, row.Creation);
        for (var index = Math.Max(cursor, registry.Jobs.Length); index < end; index++)
        {
            var child = registry.Children[index - registry.Jobs.Length];
            var id = FormattableString.Invariant($"{origin}#cascade-child:{row.Revision}:{index}");
            sent.Add(new Message(id, new(child.Id), Kinds.Terminate, Sender: row.Id,
                TargetCreation: child.Creation > 0 ? child.Creation : null, SenderCreation: row.Creation)
                .AsIssued().AsCascadeTerminate());
        }

        if (end == registry.Count) return new(null, jobs, sent);
        var nextSerial = checked(serial + 1);
        var key = ControlKey(row, nextSerial);
        sent.Add(new Message(key.Id, new(row.Id), Kinds.Cascade, Sender: row.Id,
            TargetCreation: row.Creation > 0 ? row.Creation : null, SenderCreation: row.Creation)
            .WithInboxScope(key.Scope).AsKernelTicket());
        return new(new(registry, end, nextSerial, key), jobs, sent);
    }

    internal static bool Matches(InstanceRow row, Message message) => row.Cascade is { } discharge &&
        message is { Kind: Kinds.Cascade, HasIssuedTicket: true, Start: null, Due: null, Payload: null, ReplyTo: null } &&
        discharge.Control == ControlKey(row, discharge.Serial) && message.InboxKey == discharge.Control &&
        message.Target.Id == row.Id && message.Sender == row.Id && message.SenderCreation == row.Creation &&
        message.TargetCreation == (row.Creation > 0 ? row.Creation : (long?)null);

    private static InboxKey ControlKey(InstanceRow row, long serial) =>
        new(FormattableString.Invariant($"{CreationProtocol.Origin(row.Id, row.Creation)}#cascade:{serial}"), ParkDrainEnvelope.Scope);
}

public sealed partial class InMemoryStore
{
    private sealed record CascadeInputs(IReadOnlyList<InboxKey> Pending, IReadOnlyList<InboxKey> Kept)
    {
        public IReadOnlyList<InboxKey> All => [.. Pending, .. Kept];
    }

    private CascadeInputs? PrepareCascade(TurnCommit turn, InstanceRow? previous, IReadOnlyDictionary<int, long> epochs,
        IReadOnlySet<InboxKey>? earlier, IReadOnlySet<InboxKey> competing)
    {
        var before = previous?.Cascade;
        var held = previous?.HeldCascade;
        var sent = turn.Sent.Where(message => message.CascadeOrigin || message.Kind == Kinds.Cascade).ToArray();
        if (turn.FoundationCascade is not { } action)
        {
            if (turn.Row is { } ordinary && (ordinary.Cascade != before || !turn.Delete && ordinary.HeldCascade != held) ||
                before is not null && turn.Consumed.Concat(turn.Done).Concat(turn.Held ?? []).Contains(before.Control) ||
                sent.Any(message => message.Kind == Kinds.Cascade))
                throw InvalidCascade();
            return null;
        }
        if (turn.Row is not { } next ||
            StoreBatchRules.AddressPartition(next.Id) is not { } partition || !epochs.ContainsKey(partition) ||
            action.Entries.Count == 0 || turn.Delete || action.Input is null != (before is null))
            throw InvalidCascade();

        if (before is not null || held is not null)
        {
            if (action.Entries != (before?.Entries ?? held) || turn.Consumed is not [var input] || turn.Done.Count != 0 ||
                Messages.GetValueOrDefault(input) is not { Done: false, Message: { } source } ||
                next != previous! with { Revision = checked(previous!.Revision + 1), Cascade = next.Cascade, HeldCascade = null } ||
                turn.Unsubscribe || turn.Journal is not null || turn.Subscribed.Count != 0 || turn.Unsubscribed.Count != 0 ||
                turn.Sent.Count != sent.Length || turn.Held is { Count: > 0 })
                throw InvalidCascade();
            var matches = before is null ? CascadeProtocol.MatchesRetiredTerminate(previous!, source) :
                action.Input == before.Control && input == before.Control && CascadeProtocol.Matches(previous!, source);
            if (!matches) throw InvalidCascade();
        }
        else
        {
            var birth = previous is null or { Retired: true };
            var cancelled = next.Status == InstanceStatus.Cancelled && (birth || previous!.Status == InstanceStatus.Running);
            var tookTerminate = false;
            foreach (var key in turn.Consumed.Concat(turn.Done))
            {
                if (Messages.GetValueOrDefault(key)?.Message is not { Kind: Kinds.Terminate } command) continue;
                var addressed = command.Target.Id == next.Id && CreationProtocol.TargetError(next, command) is null;
                if (command.CascadeOrigin && (!addressed || birth || !CreationProtocol.MatchesCascadeParent(previous, command)))
                    throw InvalidCascade();
                tookTerminate |= addressed;
            }
            if (next is not { Retired: false, Jobs: null, Children: null, HeldCascade: null } ||
                (birth ? next.Revision <= 0 : next.Revision != checked(previous!.Revision + 1)) ||
                !(cancelled || tookTerminate) ||
                action.Entries.Jobs.Select(job => job.Key).Distinct(StringComparer.Ordinal).Count() != action.Entries.Jobs.Length ||
                action.Entries.Children.Distinct().Count() != action.Entries.Children.Length)
                throw InvalidCascade();
            if (previous is { ParkDiscard: not null } draining &&
                (turn.Consumed.Concat(turn.Done).ToArray() is not [var input] ||
                    Messages.GetValueOrDefault(input) is not { Done: false, Message: { } source } ||
                    !AuthorizesDrainingTerminate(draining, source) ||
                    next != draining with { Revision = next.Revision, Jobs = null, Children = null, Cascade = next.Cascade } ||
                    turn.Unsubscribe || turn.Journal is not null || turn.Subscribed.Count != 0 || turn.Unsubscribed.Count != 0 ||
                    turn.Sent.Count != sent.Length || turn.Held is { Count: > 0 }))
                throw InvalidCascade();
            ValidateCascadeOpen(turn, previous, next, action.Entries, earlier!);
        }

        var expected = CascadeProtocol.Page(next, action.Entries, before?.Cursor ?? 0, before?.Serial ?? next.Revision);
        var checkedNext = expected.Next;
        if (next.Cascade != checkedNext || sent.Any(message => !message.HasIssuedTicket) ||
            !sent.Select(message => message.WithoutTicketAuthority())
                .SequenceEqual(expected.Sent.Select(message => message.WithoutTicketAuthority())))
            throw InvalidCascade();

        var pending = new List<InboxKey>();
        var kept = new List<InboxKey>();
        var own = turn.Consumed.Concat(turn.Done).ToHashSet();
        foreach (var job in expected.Jobs)
        {
            var key = InboxKey.Ticket(next.Id, job.Key, next.Creation, job.Generation);
            var ticket = Include(key, Kinds.Job, job.Generation);
            if (next.Creation > 0)
                Include(InboxKey.JobResult(next.Id, job.Key, next.Creation, job.Generation), Kinds.JobFinished, job.Generation);
            var timer = ticket?.JobTiming.Timer ?? (next.Creation > 0
                ? DurableTurn.FoundationJobDeadlineId(next.Id, next.Creation, job.Generation) : null);
            if (timer is not null)
            {
                var timeout = InboxKey.Issued(timer);
                if (Messages.GetValueOrDefault(timeout) is { Message: { Due: { } at }, JobTiming.JobKey: { } jobKey } &&
                    (ticket?.JobTiming.At is not { } due || at == due) && jobKey == job.Key)
                    Include(timeout, Kinds.JobDeadline, 0);
            }
        }
        return new(pending, kept);

        MessageRow? Include(InboxKey key, string kind, long generation)
        {
            if (own.Contains(key) || earlier!.Contains(key) || Messages.GetValueOrDefault(key) is not { } row ||
                row.Message.Kind != kind || row.Message.Target.Id != next.Id || row.Message.JobGeneration != generation ||
                row.Message.Sender != next.Id || row.Message.SenderCreation != next.Creation ||
                (row.Message.TargetCreation ?? 0) != next.Creation || row is { Done: true, KeptBy: null })
                return null;
            if (competing.Contains(key)) throw InvalidCascade();
            if (row.Done) kept.Add(key);
            else pending.Add(key);
            return row;
        }
    }

    private void ApplyCascade(CascadeInputs inputs, long now)
    {
        foreach (var key in inputs.Pending) Messages.Remove(key);
        if (inputs.Kept.Count == 0) return;
        var until = checked(now + (long)IngressDeduplicationWindow.TotalMilliseconds);
        foreach (var key in inputs.Kept)
        {
            var stored = Messages[key];
            stored.KeptBy = null;
            stored.Message = stored.Message with { KeepUntil = until };
        }
    }

    private void ValidateCascadeOpen(TurnCommit turn, InstanceRow? previous, InstanceRow next, CascadeRegistry entries,
        IReadOnlySet<InboxKey> earlier)
    {
        var consumed = turn.Consumed.Concat(turn.Done).ToHashSet();
        var children = ChildRegistry.Read(previous?.Children).ToHashSet();
        foreach (var key in consumed)
            if (Messages.GetValueOrDefault(key) is { Message: { Kind: Kinds.ChildFinished, Sender: { } sender, Payload: { } body } notice } &&
                notice.Target.Id == next.Id && (notice.TargetCreation ?? 0) == next.Creation && FinalNotice(body))
                children.Remove(new(sender, notice.SenderCreation));
        foreach (var message in turn.Sent)
            if (message.Start is { Parent: { } parent } && parent == next.Id && message.Start.ParentCreation == next.Creation)
                children.Add(new(message.Target.Id, message.TargetCreation ?? 0));
        if (!children.SetEquals(entries.Children)) throw InvalidCascade();

        var registered = JobRegistry.Read(previous?.Jobs).ToHashSet();
        if (!(previous is null && next.FoundationInlineBirth is not null) && !registered.IsSupersetOf(entries.Jobs))
            throw InvalidCascade();
        foreach (var omitted in registered.Except(entries.Jobs))
        {
            var ticket = InboxKey.Ticket(next.Id, omitted.Key, next.Creation, omitted.Generation);
            var result = InboxKey.JobResult(next.Id, omitted.Key, next.Creation, omitted.Generation);
            if (Unaccounted(ticket) || Unaccounted(result)) throw InvalidCascade();
        }

        bool Unaccounted(InboxKey key) => !consumed.Contains(key) && !earlier.Contains(key) &&
            Messages.GetValueOrDefault(key) is { } row && (!row.Done || row.KeptBy is not null);

        static bool FinalNotice(string body)
        {
            try
            {
                using var json = JsonDocument.Parse(body);
                return json.RootElement.ValueKind == JsonValueKind.Object &&
                    (!json.RootElement.TryGetProperty("Final", out var final) || final.ValueKind == JsonValueKind.True);
            }
            catch (JsonException) { return false; }
        }
    }

    private static StoreException InvalidCascade() =>
        new(Failure.Definite, "A cascade requires the exact obligation, page, and owner fence.");
}
