namespace Kernel.Store.Conformance;

internal static class JobTrialClearChecks
{
    internal static readonly Case[] Cases =
    [
        new("job trial clear: count the exact pending tickets under their current fence", Level.Required, Counted),
        new("job trial clear: each member of an owned set uses its partition epoch", Level.Required, Owned),
        new("job trial clear: takeover after a lost answer before or after the clear", Level.Required, LostAnswer)
    ];

    private static void Supported(Context c)
    {
        if (!c.S.SupportsJobTrialClear) throw new SkipException("the provider has no fenced job trial clear");
    }

    private static Message Job(string key, int partition = 0)
    {
        var target = InstanceRef.In(partition, "worker");
        var ticket = InboxKey.Ticket(target.Id, key);
        return new Message(ticket.Id, target, Kinds.Job, KeepUntil: long.MaxValue).WithInboxScope(ticket.Scope).AsKernelTicket();
    }

    private static CommitBatch Request(Lease lease, params InboxKey[] keys) =>
        new(0, lease.Epoch, lease.Position, Context.Long, false, [], [], []) { ClearJobTrials = keys };

    private static Task<Committed> Clear(Context c, Lease lease, params InboxKey[] keys) =>
        c.S.CommitAsync(Request(lease, keys)).AsTask();

    private static Task<Committed> Send(Context c, Lease lease, params Message[] messages) =>
        c.Commit(lease, 0, [Make.Turn("0/worker", sent: messages, journal: null)]);

    private static async Task Counted(Context c)
    {
        Supported(c);
        var lease = await c.Take(0);
        var first = Job("first");
        var other = Job("other\0handle");
        var settled = Job("settled");
        var old = Job("old");
        var foreign = Job("foreign", 1);
        var ordinary = Make.M(first.Id, 0);
        var nonjob = Job("nonjob") with { Kind = "ordinary-work" };
        var second = await c.Take(1);
        await c.Lands(() => Send(c, lease, first, other, settled, old, foreign, ordinary, nonjob));
        await c.Lands(() => c.Commit(lease, 0, [], trial: [old.InboxKey]));
        var predecessor = lease;
        lease = await c.Take(0);
        await c.Refused(() => Clear(c, predecessor, old.InboxKey), "stale owner clearing its previous epoch's ticket");
        await c.Lands(() => c.Commit(lease, 0, [], trial: [first.InboxKey, other.InboxKey, settled.InboxKey, ordinary.InboxKey, nonjob.InboxKey]));
        await c.Lands(() => c.Commit(second, 1, [], trial: [foreign.InboxKey]));
        await c.Lands(() => c.Commit(lease, 0, [Make.Turn("0/worker", done: [settled.InboxKey], journal: null)]));

        foreach (var invalid in new[] { old.InboxKey, Job("absent").InboxKey, settled.InboxKey, foreign.InboxKey,
            ordinary.InboxKey, nonjob.InboxKey, other.InboxKey with { Match = "wrong-match" }, first.InboxKey })
        {
            await c.Refused(() => Clear(c, lease, first.InboxKey, invalid), $"clear containing {invalid.Id}/{invalid.Scope}");
            var retained = (await c.All(0)).Single(row => row.Message.InboxKey == first.InboxKey);
            Context.Equal(lease.Epoch, retained.Trial, "a failed group rolled back its valid member");
        }
        var outsideTicketScope = new InboxKey("outside-ticket-domain", (long)int.MaxValue + 2);
        var refusal = await c.Refused(() => Clear(c, lease, first.InboxKey, outsideTicketScope), "out-of-domain ticket key");
        Context.Equal("definite", refusal, "invalid ticket scope is a definite refusal");
        var stillPending = (await c.All(0)).Single(row => row.Message.InboxKey == first.InboxKey);
        Context.Equal(lease.Epoch, stillPending.Trial, "invalid key validation leaves the valid trial intact");
        await c.Lands(() => Clear(c, lease, first.InboxKey, other.InboxKey));
        var rows = (await c.All(0)).ToDictionary(row => row.Message.InboxKey);
        Context.Equal(0L, rows[first.InboxKey].Trial, "first selected trial");
        Context.Equal(0L, rows[other.InboxKey].Trial, "other selected trial");
        Context.Equal(lease.Epoch, rows[first.InboxKey].Seen, "selected ticket remains seen at the fenced epoch");
        Context.Equal(lease.Epoch, rows[ordinary.InboxKey].Trial, "ordinary input with the same wire id retains its trial");
        Context.Equal(lease.Epoch, rows[nonjob.InboxKey].Trial, "other work retains its trial");
        Context.Equal(second.Epoch, (await c.All(1)).Single().Trial, "other partition retains its trial");
    }

    private static async Task Owned(Context c)
    {
        Supported(c);
        if (!c.S.OwnedSetsAvailable) throw new SkipException("the store fences one partition per commit");
        var left = await c.Take(0);
        var previousRight = await c.Take(1);
        Context.Equal(left.Epoch, previousRight.Epoch, "the right ticket starts at the current left epoch");
        var a = Job("left");
        var b = Job("right", 1);
        await c.Lands(() => Send(c, left, a, b));
        await c.Lands(() => c.Commit(left, 0, [], trial: [a.InboxKey]));
        await c.Lands(() => c.Commit(previousRight, 1, [], trial: [b.InboxKey]));
        var right = await c.Take(1);
        Context.That(right.Epoch > previousRight.Epoch, "the right takeover advances its epoch");
        var batch = CommitBatch.Owned([new(0, left.Epoch, left.Position), new(1, right.Epoch, right.Position)],
            Context.Long, false, [], [], []) with { ClearJobTrials = [a.InboxKey, b.InboxKey] };
        await c.Refused(() => c.S.CommitAsync(batch).AsTask(), "clear with the right ticket still at the left epoch");
        Context.Equal(left.Epoch, (await c.All(0)).Single().Trial, "the refused clear preserves the valid left trial");
        Context.Equal(previousRight.Epoch, (await c.All(1)).Single().Trial, "the refused clear preserves the stale right trial");

        await c.Lands(() => c.Commit(right, 1, [], trial: [b.InboxKey]));
        await c.Lands(() => c.S.CommitAsync(batch).AsTask());
        Context.That((await c.All(0)).Single().Trial == 0 && (await c.All(1)).Single().Trial == 0,
            "both trials cleared under their distinct epochs");
    }

    private static async Task LostAnswer(Context c)
    {
        Supported(c);
        var lease = await c.Take(0, "original", TimeSpan.FromMilliseconds(300));
        var cleared = Job("after-commit");
        var uncleared = Job("before-commit");
        var command = Make.M("queued-command", 0);
        await c.Lands(() => Send(c, lease, cleared, command, uncleared));
        await c.Lands(() => c.Commit(lease, 0, [], trial: [cleared.InboxKey, uncleared.InboxKey]));
        // The same transport wrapper as the recovery stress suite, with one deterministic failure on each side.
        foreach (var (key, seed, after) in new[] { (cleared.InboxKey, 0, true), (uncleared.InboxKey, 1, false) })
        {
            var transport = new FaultyStore(c.S, 1, seed);
            try
            {
                await transport.CommitAsync(Request(lease, key));
                throw new ConformanceException("the caller unexpectedly received an acknowledgment");
            }
            catch (IOException)
            {
                Context.That(transport.After == (after ? 1 : 0) && transport.Before == (after ? 0 : 1),
                    "the scheduled request or its answer was lost");
            }
        }
        var before = (await c.All(0)).Select(row => (row.Message.InboxKey, row.Seq)).ToArray();
        await Task.Delay(TimeSpan.FromMilliseconds(700));
        var next = await c.Take(0, "successor");
        var rows = await c.All(0);
        Context.Equal(0, rows.Single(row => row.Message.InboxKey == cleared.InboxKey).Attempts, "cleared trial has no death");
        Context.Equal(1, rows.Single(row => row.Message.InboxKey == uncleared.InboxKey).Attempts, "unacknowledged unsent clear leaves one death");
        Context.That(next.Epoch > lease.Epoch && before.SequenceEqual(rows.Select(row => (row.Message.InboxKey, row.Seq))),
            "owner change keeps the waiting command and both job tickets in their inbox order");
    }
}
