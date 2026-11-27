using System.Text;
using Contract = Kernel.Foundation.Contracts;
using static Kernel.EntityFramework.RootAdmissionWire;

namespace Kernel.EntityFramework;

internal static partial class RootEventAdmission
{
    internal sealed class HeldPublicEventExpired : Exception;

    internal static async Task<Contract.Admission> PublishOutsideAsync(KernelDb db, string tenant, string key, string id,
        string? payload, long now, TimeSpan retention, TimeSpan receiptWindow, RootAdmissionPressure.Attempt? attempt,
        Func<FormattableString, FormattableString>? bind, CancellationToken token)
    {
        var (outcome, receipt) = await PublishPublicAsync(db, tenant, key, id, payload, now, retention, receiptWindow, attempt,
            bind, null, token);
        return new(outcome == HeldRootAdmission.Duplicate ? Contract.AdmissionOutcome.Duplicate : Contract.AdmissionOutcome.Accepted)
            { Publication = receipt };
    }

    internal static async Task<HeldRootAdmission> PublishHeldPublicAsync(KernelDb db, HeldRootRequest request, string key,
        string? payload, long now, TimeSpan receiptWindow, RootAdmissionPressure.Attempt? attempt,
        Func<FormattableString, FormattableString>? bind, CancellationToken token)
    {
        if (!request.IsOpen) return HeldRootAdmission.Refused;
        if (request.Caller.Deadline <= now) return HeldRootAdmission.Expired;
        var (outcome, _) = await PublishPublicAsync(db, request.Tenant, key, request.Caller.Id, payload, now, TimeSpan.Zero,
            receiptWindow, attempt, bind, request, token);
        return outcome;
    }

    internal static bool IsValidPublicRequest(HeldRootRequest request, string key, string? payload) =>
        request.IsOpen && RootPublicEventPublications.Validate(key, request.Caller.Id, payload, null) is null;

    private static async Task<(HeldRootAdmission Outcome, Contract.PublicationReceipt? Receipt)> PublishPublicAsync(KernelDb db,
        string tenant, string key, string id, string? payload, long now, TimeSpan retention, TimeSpan receiptWindow,
        RootAdmissionPressure.Attempt? attempt, Func<FormattableString, FormattableString>? bind, HeldRootRequest? caller,
        CancellationToken token)
    {
        var qualified = tenant + "_" + key;
        var operation = FoundationRootCreator.OperationDigest(id);
        FormattableString BindPublic(FormattableString sql) => RootPublicEventStarts.Bind(sql, bind);
        if (await RootPublicEventPublications.ReadReplayAsync(db, tenant, qualified, id, operation, now, BindPublic, token) is { } replay)
            return (HeldRootAdmission.Duplicate, new(key, id, replay, Contract.PublicationStatus.Matched));
        var address = caller?.Address;
        if (caller is null) attempt?.RequireCapacity();

        var incarnation = Guid.NewGuid();
        var occurrence = FoundationRootCreator.OperationDigest(DurableKeyJson.Write(new[] { "outside", qualified, incarnation.ToString("N") }));
        var keepThrough = checked(now + (long)retention.TotalMilliseconds);
        var receiptThrough = checked(now + (long)receiptWindow.TotalMilliseconds);
        var keepInputsThrough = caller is null ? keepThrough : receiptThrough;
        var waits = await ReadWaitsAsync(db, tenant, qualified, BindLive, bind is not null, token);
        if (waits.Count > FoundationMetadataLimits.MaxTurnSends) throw FanoutFull();
        var messages = new List<MessageEntity>(waits.Count);
        long bytes = 0;
        foreach (var wait in waits)
        {
            var (header, body, kind) = EncodeDirect(id, 1, Kinds.Event, payload, null, held: address is not null, new(qualified, wait.Wait!),
                eventOccurrence: occurrence);
            bytes += Encoding.UTF8.GetByteCount(header) + (body is null ? 0 : Encoding.UTF8.GetByteCount(body)) +
                (address is null ? 0 : Encoding.UTF8.GetByteCount(address));
            if (!FitsEnvelope(header, body, address) || bytes > FoundationMetadataLimits.MessageContentBytes) throw FanoutFull();
            messages.Add(new MessageEntity
            {
                Id = PublicEventOccurrence(wait.Reference!, occurrence, id, wait.Wait!), InboxScope = InboxKey.FoundationOccurrenceScope,
                Target = wait.Actor!, Partition = wait.Partition!.Value, Kind = kind, Start = header, Payload = body, ReplyTo = address
            });
        }

        int[] starts;
        try
        {
            var captured = await RootPublicEventStarts.CaptureAsync(db, tenant, qualified, id, occurrence, payload,
                FoundationMetadataLimits.MaxTurnSends - waits.Count, FoundationMetadataLimits.MessageContentBytes - bytes, bind, token, address);
            if (caller is not null && waits.Count == 0 && captured.Count == 0) return (HeldRootAdmission.NoWaiter, null);
            await RootPublicEventPublications.InsertAsync(db, tenant, qualified, id, operation, incarnation, occurrence,
                caller is null ? payload : null, receiptThrough, keepThrough, BindPublic, token);
            starts = captured.Count == 0 ? [] : await RootPublicEventStarts.DispatchAsync(db, tenant, occurrence, keepInputsThrough,
                bind, token, address, rememberDelivery: keepThrough > now);
        }
        catch (RootPublicEventStarts.FanoutExceeded) { throw FanoutFull(); }
        if (waits.Count != 0 || starts.Length != 0)
            attempt?.Require(waits.Select(wait => wait.Partition!.Value).Concat(starts).Distinct().ToArray());
        if (messages.Count != 0)
        {
            if (keepThrough > now)
                await RootPublicEventLiveWaits.RememberPublishedAsync(db, occurrence, messages.Select(message => message.Id).ToArray(), bind, token);
            await EnqueueAsync(db, messages, address, keepInputsThrough, BindLive, bind is not null, token);
        }
        if (caller is not null)
        {
            var committedAt = bind is null ? await SqliteRootAdmission.ReadClockAsync(db, token) : await ReadPostgresClockAsync(db, token);
            if (!caller.IsOpen || caller.Caller.Deadline <= committedAt) throw new HeldPublicEventExpired();
        }
        return (HeldRootAdmission.Accepted, new(key, id, incarnation, Contract.PublicationStatus.Matched));

        FormattableString BindLive(FormattableString sql) => BindEventSource(sql, bind);
    }
}
