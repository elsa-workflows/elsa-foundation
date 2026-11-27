using System.Data.Common;
using System.Diagnostics;
using Kernel.Durable;
using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static partial class KernelAuthoredEventChecks
{
    private const string PagedFirstOwner = "paged-event-interrupted-owner";
    private const string OwnerBoundaryCommitted = "paged-event-boundary-committed";

    internal static async Task<int> RunPausedPagedEventOwnerAsync(string provider, bool inBatch, string path, bool afterProducer)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(75));
        using var barrier = new PageCommitBarrier(timeout.Token, afterProducer ? IsAuthoredTicketInsert : IsDispatchUpdate);
        Contract.IActorType model = afterProducer ? new ProtectedSnapshotActor() : new PagedEventActor();
        await using var owner = (await OpenAsync(model, provider, path, inBatch, timeout.Token,
            pagedPublicEvents: true, node: PagedFirstOwner, attachExisting: true)).Owner;
        await Task.Delay(Timeout.InfiniteTimeSpan, timeout.Token);
        return 1;

        static bool IsAuthoredTicketInsert(DbCommand command) =>
            command.CommandText.Contains("INSERT INTO \"Messages\"", StringComparison.Ordinal) &&
            command.Parameters.Cast<DbParameter>().Any(parameter => Equals(parameter.Value, Kinds.Publish));

        static bool IsDispatchUpdate(DbCommand command) =>
            command.CommandText.Contains("UPDATE \"FoundationPublicEventDispatch\"", StringComparison.Ordinal);
    }

    private static Task<InterruptedPage> InterruptPagedEventOwnerAsync(string provider, bool inBatch, string path,
        Func<KernelDb> open, string tenant, string publicationId, CancellationToken token) =>
        InterruptPagedEventOwnerAtAsync(provider, inBatch, path, afterProducer: false, async () =>
        {
            var snapshot = await ReadReplacementStateAsync(open, tenant, publicationId, token);
            Check(snapshot.Page is { Owner: PagedFirstOwner, Status: (int)Contract.PublicationStatus.Pending, Done: false,
                      LiveComplete: false, CursorWait: "wide-wait", Attempts: 0, Trial: 0 } &&
                snapshot.Deliveries is [var first] && snapshot.Receipts.SetEquals([first.Id]) &&
                snapshot.Page.CursorInstance == first.Target && snapshot.Page.Sequence == first.Sequence + 1,
                "the first owner's partial live page and its pending successor committed together");
            return snapshot;
        }, token);

    private static async Task<T> InterruptPagedEventOwnerAtAsync<T>(string provider, bool inBatch, string path, bool afterProducer,
        Func<Task<T>> readBeforeTermination, CancellationToken token)
    {
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true };
        foreach (var arg in new[] { typeof(KernelAuthoredEventChecks).Assembly.Location, "kernel-paged-event-paused-owner",
                     provider, inBatch ? "in-batch" : "checked", path, afterProducer ? "producer" : "first-page" })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the first Event owner.");
        try
        {
            var signal = await process.StandardOutput.ReadLineAsync(token);
            Check(signal == OwnerBoundaryCommitted,
                $"the first hosted process reached the committed {(afterProducer ? "producer" : "page")} boundary ({signal ?? "no signal"})");
            var snapshot = await readBeforeTermination();
            Check(!process.HasExited, "the first owner remained live at its commit boundary before termination");
            process.Kill();
            await process.WaitForExitAsync(token);
            Check(process.ExitCode != 0, "the first owner was terminated before its next transaction");
            return snapshot;
        }
        finally
        {
            if (!process.HasExited) process.Kill();
            await process.WaitForExitAsync();
        }
    }

    private static async Task VerifyPagedEventOwnerReplacementAsync(Func<KernelDb> open, string tenant, string publicationId,
        InterruptedPage interrupted, int expected, CancellationToken token)
    {
        var settled = await ReadReplacementStateAsync(open, tenant, publicationId, token);
        var first = interrupted.Deliveries.Single();
        Check(settled.Page is { Status: (int)Contract.PublicationStatus.Matched, Done: true, LiveComplete: true,
                  CursorWait: "wide-wait", Attempts: 0, Trial: 0 } &&
            settled.Page.Owner is not null && settled.Page.Owner != interrupted.Page.Owner && settled.Page.Epoch > interrupted.Page.Epoch &&
            settled.Page.Occurrence == interrupted.Page.Occurrence && settled.Page.Ticket == interrupted.Page.Ticket,
            $"the replacement took epoch {settled.Page.Epoch} after epoch {interrupted.Page.Epoch} and settled the original publication");
        Check(settled.Deliveries.Count == expected && settled.Deliveries.All(row => row.Done) &&
            settled.Deliveries.Select((row, index) => row.Sequence == first.Sequence + index * 2L).All(exact => exact) &&
            settled.Deliveries.Select(row => row.Target).Distinct(StringComparer.Ordinal).Count() == expected &&
            settled.Deliveries[0] == first with { Done = true } &&
            settled.Receipts.SetEquals(settled.Deliveries.Select(row => row.Id)) &&
            settled.Page.Sequence == settled.Deliveries[^1].Sequence - 1 && settled.Page.CursorInstance == settled.Deliveries[^1].Target,
            "the replacement advanced the saved cursor across the remaining pages and consumed each exact wait once");
    }

    private static async Task<InterruptedPage> ReadReplacementStateAsync(Func<KernelDb> open, string tenant, string eventId, CancellationToken token)
    {
        await using var db = open();
        var page = await db.Database.SqlQuery<ReplacementPage>($"""
            SELECT d."Occurrence", d."Ticket", d."Status", d."LiveComplete", d."LiveThroughInstance" AS "CursorInstance",
                d."LiveThroughWait" AS "CursorWait", m."Seq" AS "Sequence", m."Done", m."Attempts", m."Trial", owner."Epoch", owner."Owner"
            FROM "FoundationPublicEventDispatch" d
            JOIN "FoundationPublicEventPublications" p ON p."Occurrence" = d."Occurrence"
            JOIN "Messages" m ON m."Id" = d."Ticket" AND m."Target" = d."Target" AND m."InboxScope" = {RootPublicEventTicket.Scope}
            JOIN "Partitions" owner ON owner."Id" = d."Partition"
            WHERE p."Tenant" = {tenant} AND p."EventId" = {DurableKeyJson.Write(eventId)}
            """).SingleAsync(token);
        var deliveries = await db.Messages.Where(row => row.Kind == Kinds.Event && row.InboxScope == InboxKey.FoundationOccurrenceScope)
            .OrderBy(row => row.Seq).Select(row => new ReplacementDelivery(row.Target, row.Id, row.Seq, row.Done)).ToListAsync(token);
        var receipts = await db.Database.SqlQuery<string>($"""
            SELECT "Delivery" AS "Value" FROM "FoundationPublicEventLiveReceipts" WHERE "Occurrence" = {page.Occurrence}
            """).ToListAsync(token);
        return new(page, deliveries, receipts.ToHashSet(StringComparer.Ordinal));
    }

    private sealed record ReplacementPage(string Occurrence, string Ticket, int Status, bool LiveComplete, string? CursorInstance,
        string? CursorWait, long Sequence, bool Done, int Attempts, long Trial, long Epoch, string? Owner);
    private sealed record ReplacementDelivery(string Target, string Id, long Sequence, bool Done);
    private sealed record InterruptedPage(ReplacementPage Page, List<ReplacementDelivery> Deliveries, HashSet<string> Receipts);

    // EF emits the transaction notification after the provider commits and before the worker gets its next turn.
    // This observer lives only in the disposable owner process. Its timeout releases the gate if that process is left running.
    private sealed class PageCommitBarrier : IObserver<DiagnosticListener>,
        IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly List<IDisposable> _listeners = [];
        private readonly IDisposable _allListeners;
        private readonly CancellationToken _deadline;
        private readonly Func<DbCommand, bool> _matches;
        private Guid? _page;

        internal PageCommitBarrier(CancellationToken deadline, Func<DbCommand, bool> matches)
        {
            _deadline = deadline;
            _matches = matches;
            _allListeners = DiagnosticListener.AllListeners.Subscribe(this);
        }

        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name == "Microsoft.EntityFrameworkCore")
                _listeners.Add(listener.Subscribe(this, name =>
                    name == RelationalEventId.CommandExecuted.Name || name == RelationalEventId.TransactionCommitted.Name));
        }

        public void OnNext(KeyValuePair<string, object?> notice)
        {
            if (notice.Value is CommandExecutedEventData { Result: 1 } command && _matches(command.Command))
                _page = command.Context?.Database.CurrentTransaction?.TransactionId;
            else if (notice.Value is TransactionEndEventData committed && committed.TransactionId == _page)
            {
                Console.WriteLine(OwnerBoundaryCommitted);
                _deadline.WaitHandle.WaitOne();
            }
        }

        public void OnCompleted() { }
        public void OnError(Exception error) { }

        public void Dispose()
        {
            _allListeners.Dispose();
            foreach (var listener in _listeners) listener.Dispose();
        }
    }
}
