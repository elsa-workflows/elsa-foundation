using Elsa.ActorFoundation.TestSupport;
using Kernel.Durable;
using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static partial class KernelAuthoredEventChecks
{
    internal static async Task<int> RunPagedEventCleanupAsync(string provider)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = timeout.Token;
        var scope = ContractScopes.Bind("paged-event-cleanup");
        var key = scope.Tenant + "_topic";
        var model = new PagedEventActor();
        var directory = Directory.CreateTempSubdirectory("paged-event-cleanup");
        try
        {
            var (open, ingress, _, owner, _, _) = await OpenAsync(model, provider,
                Path.Combine(directory.FullName, "store.db"), inBatch: false, token, pagedPublicEvents: true);
            await owner.DisposeAsync();

            var references = new List<string>();
            foreach (var id in new[] { "one", "two" })
            {
                var claim = await ingress.SendAsync(scope, new Contract.Target.Start(new(model.Type, id)),
                    id, Contract.Kinds.Start, cancellationToken: token);
                references.Add(claim.Actor?.Id ?? throw new InvalidOperationException("The cleanup fixture needs committed births."));
            }

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            const string eventId = "expired-publication";
            var occurrence = FoundationRootCreator.OperationDigest(eventId);
            var until = now - 10_000;
            var receiptUntil = now + 10_000;
            var body = new string('x', 3 * 1024 * 1024);
            await using (var db = open())
            {
                await using var transaction = await db.Database.BeginTransactionAsync(token);
                var bind = await BindAsync(db);
                await RootPublicEventPublications.InsertAsync(db, scope.Tenant, key, eventId, occurrence, Guid.NewGuid(),
                    occurrence, body, now - 20_000, receiptUntil, until, sql => RootPublicEventStarts.Bind(sql, bind), token);
                var publisher = InstanceRef.In(0, "publisher");
                await RootPublicEventDispatch.AddAsync(db, occurrence, "cleanup-ticket", publisher.Id, publisher.Partition, bind, token);
                var item = await RootPublicEventDispatch.ReadPendingAsync(db, occurrence, "cleanup-ticket", publisher.Id, publisher.Partition, bind, token)
                    ?? throw new InvalidOperationException("The cleanup publication did not land.");
                Check(await RootPublicEventDispatch.ChangeAsync(db, item, item.Position, Contract.PublicationStatus.DeadLettered, bind, token),
                    "the fixture has a durable dead-letter state");
                await RootPublicEventLiveWaits.RememberPublishedAsync(db, occurrence,
                    Enumerable.Range(0, 341).Select(index => $"delivery-{index:D3}").ToArray(), bind, token);
                foreach (var reference in references)
                    await db.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO "FoundationPublicEventTargets"
                            ("Tenant", "Publication", "Registration", "Key", "Wait", "EventId", "Start", "Payload", "PayloadEscaped", "Reference")
                        VALUES ({scope.Tenant}, {occurrence}, {reference}, {key}, {"wait"}, {eventId}, {"start"}, {body}, false, {reference})
                        """, token);
                await transaction.CommitAsync(token);
            }

            Check(!await SweepAsync(now) && await CountsAsync() == (341, 2, 1),
                "a dead letter keeps its body and delivery identities through the longer deduplication window");
            var after = receiptUntil + 1;
            foreach (var expected in new[] { (171, 2, 1), (1, 2, 1), (0, 2, 1), (0, 1, 1), (0, 0, 1), (0, 0, 0) })
                Check(await SweepAsync(after) && await CountsAsync() == expected,
                    $"one sweep leaves {expected.Item1} receipts, {expected.Item2} materialized starts and {expected.Item3} publication");
            Check(!await SweepAsync(after), "the final sweep releases the occurrence");
            return 0;

            async Task<bool> SweepAsync(long at)
            {
                await using var db = open();
                await using var transaction = await db.Database.BeginTransactionAsync(token);
                var bind = await BindAsync(db);
                var next = await RootPublicEventPublications.FindCleanupAsync(db, at, bind, token);
                var changed = next is not null && await RootPublicEventPublications.CleanupAsync(db, next, at, bind, token);
                await transaction.CommitAsync(token);
                return changed;
            }

            async Task<(int Receipts, int Starts, int Publications)> CountsAsync()
            {
                await using var db = open();
                return (
                    await db.Database.SqlQuery<int>($"""
                        SELECT CAST(count(*) AS integer) AS "Value" FROM "FoundationPublicEventLiveReceipts" WHERE "Occurrence" = {occurrence}
                        """).SingleAsync(token),
                    await db.Database.SqlQuery<int>($"""
                        SELECT CAST(count(*) AS integer) AS "Value" FROM "FoundationPublicEventTargets" WHERE "Publication" = {occurrence}
                        """).SingleAsync(token),
                    await db.Database.SqlQuery<int>($"""
                        SELECT CAST(count(*) AS integer) AS "Value" FROM "FoundationPublicEventPublications" WHERE "Occurrence" = {occurrence}
                        """).SingleAsync(token));
            }

            async Task<Func<FormattableString, FormattableString>?> BindAsync(KernelDb db)
            {
                if (!db.Database.IsNpgsql()) return null;
                var marker = await PostgresRootAdmission.PinRootCommitNamespaceAsync(db, writesJournal: false, token);
                var bind = await PostgresRootAdmission.PinAuthoredEventSourceAsync(db, marker, token);
                await RootEventAdmission.LockPostgresKeysAsync(db, [key], token);
                return bind;
            }
        }
        finally { directory.Delete(recursive: true); }
    }
}
