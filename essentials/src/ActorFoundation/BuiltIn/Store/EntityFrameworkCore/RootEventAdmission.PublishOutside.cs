using System.Text;
using Contract = Kernel.Foundation.Contracts;
using static Kernel.EntityFramework.RootAdmissionWire;

namespace Kernel.EntityFramework;

internal static partial class RootEventAdmission
{
    internal static async Task<Contract.Admission> PublishOutsideAsync(KernelDb db, string tenant, string key, string id,
        string? payload, long now, TimeSpan retention, TimeSpan receiptWindow, RootAdmissionPressure.Attempt? attempt,
        Func<FormattableString, FormattableString>? bind, CancellationToken token)
    {
        var qualified = tenant + "_" + key;
        var operation = FoundationRootCreator.OperationDigest(id);
        FormattableString BindPublic(FormattableString sql) => RootPublicEventStarts.Bind(sql, bind);
        if (await RootPublicEventPublications.ReadReplayAsync(db, tenant, qualified, id, operation, now, BindPublic, token) is { } replay)
            return new(Contract.AdmissionOutcome.Duplicate) { Publication = new(key, id, replay, Contract.PublicationStatus.Matched) };
        attempt?.RequireCapacity();

        var incarnation = Guid.NewGuid();
        var occurrence = FoundationRootCreator.OperationDigest(DurableKeyJson.Write(new[] { "outside", qualified, incarnation.ToString("N") }));
        var keepThrough = checked(now + (long)retention.TotalMilliseconds);
        var receiptThrough = checked(now + (long)receiptWindow.TotalMilliseconds);
        var waits = await ReadWaitsAsync(db, tenant, qualified, BindLive, bind is not null, token);
        if (waits.Count > FoundationMetadataLimits.MaxTurnSends) throw FanoutFull();
        var messages = new List<MessageEntity>(waits.Count);
        long bytes = 0;
        foreach (var wait in waits)
        {
            var (header, body, kind) = EncodeDirect(id, 1, Kinds.Event, payload, null, held: false, new(qualified, wait.Wait!),
                eventOccurrence: occurrence);
            bytes += Encoding.UTF8.GetByteCount(header) + (body is null ? 0 : Encoding.UTF8.GetByteCount(body));
            if (!FitsEnvelope(header, body) || bytes > FoundationMetadataLimits.MessageContentBytes) throw FanoutFull();
            messages.Add(new MessageEntity
            {
                Id = PublicEventOccurrence(wait.Reference!, occurrence, id, wait.Wait!), InboxScope = InboxKey.FoundationOccurrenceScope,
                Target = wait.Actor!, Partition = wait.Partition!.Value, Kind = kind, Start = header, Payload = body
            });
        }

        int[] starts;
        try
        {
            var captured = await RootPublicEventStarts.CaptureAsync(db, tenant, qualified, id, occurrence, payload,
                FoundationMetadataLimits.MaxTurnSends - waits.Count, FoundationMetadataLimits.MessageContentBytes - bytes, bind, token);
            starts = captured.Count == 0 ? [] : await RootPublicEventStarts.DispatchAsync(db, tenant, occurrence, keepThrough, bind, token);
        }
        catch (RootPublicEventStarts.FanoutExceeded) { throw FanoutFull(); }
        if (waits.Count != 0 || starts.Length != 0)
            attempt?.Require(waits.Select(wait => wait.Partition!.Value).Concat(starts).Distinct().ToArray());
        if (messages.Count != 0) await EnqueueAsync(db, messages, null, keepThrough, BindLive, bind is not null, token);
        await RootPublicEventPublications.InsertAsync(db, tenant, qualified, id, operation, incarnation, occurrence, payload, receiptThrough,
            keepThrough, BindPublic, token);
        return new(Contract.AdmissionOutcome.Accepted) { Publication = new(key, id, incarnation, Contract.PublicationStatus.Matched) };

        FormattableString BindLive(FormattableString sql) => BindEventSource(sql, bind);
    }
}
