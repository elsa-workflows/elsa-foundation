using Kernel.EntityFramework;
using Kernel.Store.Conformance;
using Microsoft.EntityFrameworkCore;

namespace Kernel.Ef.Conformance;

internal static class ParkWriteBoundaryChecks
{
    internal static async Task Run(Func<KernelDb> open, IKernelStore store, IKernelStore reader)
    {
        const string actor = "0/park-boundary";
        const string other = "0/park-unrelated";
        var lease = TimeSpan.FromMinutes(2);
        var fence = await store.TakeAsync(0, "park-boundary", lease)
            ?? throw new InvalidOperationException("The Park fixture did not acquire its fence.");
        var row = Make.Row(actor, 1);
        var input = Make.M("park-input", 0, payload: "[1]", keep: long.MaxValue, target: "park-boundary");
        var output = Make.M("park-output", 0, target: "park-unrelated");
        var wait = new SubscriptionRow("park-event", actor, "park-wait");
        var setup = await Commit(fence.Position, Make.Turn(actor, row, sent: [input], subscribed: [wait]));
        Check(setup.Landed, "the live state, input and wait are installed");
        var journal = await reader.ReadJournalAsync(0, 0, 100);
        var turn = Make.Turn(actor, row with { Revision = 2, Status = InstanceStatus.Faulted, Fault = "fixture" },
            expected: 1, done: [input.InboxKey], sent: [output], journal: "[\"parked\"]", unsubscribe: true);

        await Refuse(turn with { OpensPark = true,
            FoundationKept = new Dictionary<InboxKey, ParkKeepReason> { [input.InboxKey] = ParkKeepReason.Consumed } });
        await Refuse(turn with
        {
            FoundationKept = new Dictionary<InboxKey, ParkKeepReason> { [input.InboxKey] = ParkKeepReason.Arrived }
        });
        Check(await reader.ReadJournalAsync(0, 0, 100) is { Count: 1 } journalAfter &&
            journal.SequenceEqual(journalAfter), "the refused batches leave the original journal");

        await using (var db = open())
        {
            var saved = await db.Messages.AsNoTracking().SingleAsync();
            Check(saved.Id == input.Id && !saved.Done && saved.Payload == input.Payload && saved.KeepUntil == input.KeepUntil,
                "the refused batches preserve the pending input and do not send");
            Check((await db.Subscriptions.AsNoTracking().SingleAsync()).Wait == wait.Wait,
                "the refused batches preserve the wait");
            var partition = await db.Partitions.AsNoTracking().SingleAsync();
            Check(partition.Position == fence.Position + 1 && partition.Epoch == fence.Epoch,
                "the refused batches leave the fence epoch and journal position");
        }
        Check((await reader.LoadAsync([actor, other])).Single() == row,
            "the refused batches leave the original checkpoint and no unrelated checkpoint");

        Check((await Commit(fence.Position + 1, turn with { OpensPark = true })).Landed &&
            (await reader.LoadAsync([actor])).Single().Status == InstanceStatus.Faulted,
            "a legacy fault with ordinary done semantics still lands at the same fence");

        ValueTask<Committed> Commit(long position, params TurnCommit[] turns) =>
            store.CommitAsync(new(0, fence.Epoch, position, lease, false, turns, [], []));

        async Task Refuse(TurnCommit park)
        {
            try
            {
                var result = await Commit(fence.Position + 1, Make.New(other), park);
                Check(result.Landed, "the current fence did not silently lose the test batch");
            }
            catch (StoreException error) when (error.Failure == Failure.Definite &&
                error.Message == "This store does not support retaining Park inputs.")
            {
                Check(true, "the store refuses a batch containing Park retention metadata");
                return;
            }
            throw new InvalidOperationException("The relational writer accepted a Park it cannot retain.");
        }
    }

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
        Console.WriteLine("ok " + message);
    }
}
