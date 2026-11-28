using System.Text;
using Kernel.Foundation.Hosting;
using Microsoft.EntityFrameworkCore;
using static Kernel.EntityFramework.RootAdmissionWire;

namespace Kernel.EntityFramework;

internal static partial class RootEventAdmission
{
    internal const string PreparedWaitReceiptTable = "FoundationPreparedWaitReceipts";

    internal static string PreparedWaitReceiptColumns(bool sqlite)
    {
        var key = sqlite ? "TEXT" : "text COLLATE \"C\"";
        return FormattableString.Invariant($"""
            "Tenant" {key} NOT NULL, "Operation" {key} NOT NULL CHECK (length("Operation") = 64),
            "Request" {key} NOT NULL CHECK (length("Request") = 64),
            "Cohort" {key} NOT NULL CHECK (length("Cohort") = 64),
            "Count" INTEGER NOT NULL CHECK ("Count" BETWEEN 0 AND {FoundationMetadataLimits.MaxTurnSends}),
            PRIMARY KEY ("Tenant", "Operation")
            """);
    }

    internal static PreparedWaitDelivery[] SnapshotPreparedWaitDeliveries(string operation, string sourceDigest,
        IReadOnlyList<PreparedWaitDelivery> deliveries)
    {
        ArgumentNullException.ThrowIfNull(deliveries);
        if (!IsDigest(operation) || !IsDigest(sourceDigest))
            throw new ArgumentException("A prepared wait handoff requires its bounded source identity and route keys.");
        if (!PreparedWaitDelivery.FitsEnvelope(deliveries))
            throw new ArgumentException("The prepared wait handoff does not fit its committed delivery envelope.");
        return deliveries.OrderBy(delivery => delivery.Key, StringComparer.Ordinal).ToArray();

        static bool IsDigest(string value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigitLower);
    }

    internal static async Task<PreparedWaitReceipt> DeliverPreparedWaitsAsync(KernelDb db, string tenant, string operation,
        string sourceDigest, PreparedWaitDelivery[] deliveries, bool allowNew, long now, TimeSpan window, RootAdmissionPressure.Attempt? pressure,
        Func<FormattableString, FormattableString>? bind, CancellationToken token)
    {
        if (!db.UsesPreparedWaitDelivery || db.Database.CurrentTransaction is null)
            throw new NotSupportedException("Prepared waits require the selected Foundation admission transaction.");
        var request = FoundationRootCreator.OperationDigest(DurableKeyJson.Write(new { sourceDigest, deliveries }));
        if (await db.Database.SqlQuery<SavedWaitReceipt>(Apply($"""
            WITH source AS MATERIALIZED (/*event-source*/)
            SELECT receipt."Request", receipt."Cohort", receipt."Count"
            FROM "FoundationPreparedWaitReceipts" receipt CROSS JOIN source
            WHERE source."SourceReady" AND receipt."Tenant" = {tenant} AND receipt."Operation" = {operation}
            """)).SingleOrDefaultAsync(token) is { } saved)
        {
            if (saved.Request != request) throw new InvalidOperationException("The source identity already names a different wait handoff.");
            return new(saved.Cohort, saved.Count);
        }

        if (!allowNew) throw new NotSupportedException("The current HTTP route does not allow a new wait handoff.");
        var occurrence = FoundationRootCreator.OperationDigest(DurableKeyJson.Write(new[] { "prepared-waits", tenant, operation }));
        var eventId = "prepared-waits:" + operation;
        var messages = new List<MessageEntity>();
        long bytes = 0;
        foreach (var delivery in deliveries)
        {
            var key = tenant + "_" + delivery.Key;
            var waits = await ReadWaitsAsync(db, tenant, key, Apply, bind is not null, token);
            if (waits.Count == 0) continue;
            if (delivery.Payload is null || delivery.RequiredWaitSuffix is { } suffix &&
                waits.Any(wait => wait.Wait?.EndsWith(suffix, StringComparison.Ordinal) != true))
                throw new NotSupportedException("A committed wait lacks the exact route authority required by the source.");
            if (waits.Count > FoundationMetadataLimits.MaxTurnSends - messages.Count) throw FanoutFull();
            foreach (var wait in waits)
            {
                if (!FoundationMetadataLimits.FitsLiveWaitName(wait.Wait!)) throw new StoreFormatException("A committed event wait has an invalid identity.");
                // Use the existing detached matched-event wire without registering a public publication or Event start.
                var (header, payload, kind) = EncodeDirect(eventId, 1, Kinds.Event, delivery.Payload, null, held: false,
                    new(key, wait.Wait!), eventOccurrence: occurrence);
                bytes += Encoding.UTF8.GetByteCount(header) + (payload is null ? 0 : Encoding.UTF8.GetByteCount(payload));
                if (!FitsEnvelope(header, payload) || bytes > FoundationMetadataLimits.MessageContentBytes) throw FanoutFull();
                messages.Add(new MessageEntity
                {
                    Id = PublicEventOccurrence(wait.Reference!, occurrence, eventId, wait.Wait!), InboxScope = InboxKey.FoundationOccurrenceScope,
                    Target = wait.Actor!, Partition = wait.Partition!.Value, Kind = kind, Start = header, Payload = payload
                });
            }
        }
        // A wait can be selected only once, even if more than one permitted key names that registration.
        var selected = messages.DistinctBy(message => message.Id, StringComparer.Ordinal).ToArray();
        if (selected.Length != 0)
        {
            pressure?.Require(selected.Select(message => message.Partition).Distinct().ToArray());
            var until = checked(now + (long)window.TotalMilliseconds);
            foreach (var batch in selected.GroupBy(message => message.Payload, StringComparer.Ordinal))
                await EnqueueAsync(db, batch.ToList(), until, Apply, bind is not null, token);
        }
        var cohort = FoundationRootCreator.OperationDigest(DurableKeyJson.Write(selected.Select(message => message.Id).ToArray()));
        if (await db.Database.ExecuteSqlInterpolatedAsync(Apply($"""
            WITH source AS MATERIALIZED (/*event-source*/)
            INSERT INTO "FoundationPreparedWaitReceipts" ("Tenant", "Operation", "Request", "Cohort", "Count")
            SELECT {tenant}, {operation}, {request}, {cohort}, {selected.Length} FROM source WHERE "SourceReady"
            """), token) != 1)
            throw new StoreFormatException("The prepared wait receipt did not commit in the pinned source.");
        return new(cohort, selected.Length);

        FormattableString Apply(FormattableString sql) => BindEventSource(sql, bind);
    }

    private sealed record SavedWaitReceipt(string Request, string Cohort, int Count);
}
