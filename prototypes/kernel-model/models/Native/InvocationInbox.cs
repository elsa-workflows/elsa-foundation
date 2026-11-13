using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Kernel.Native;

// Bounded M0 research envelope. Not the production JSON/admission API or retention contract.
public sealed record InvocationInput(string Frame, string Id, string Name, string? Value = null)
{
    public const string Kind = "native.invocation-input";
}
public sealed record ReceiptQuery(string Frame, string Id) { public const string Kind = "native.receipt-query"; }
public sealed record Admission(string Frame, string Id, string Result, long? Order = null, string? Outcome = null,
    string? Actor = null, string? Status = null, long? ValidUntil = null, long? FinalizedAt = null);
public sealed record InputReceipt(string Digest, long Order, string Status = "Queued", string? Outcome = null,
    long? Retention = null, string? Clock = null);
public sealed class InvocationInbox
{
    public int Capacity { get; set; }
    public int ReceiptCapacity { get; set; }
    public long Next { get; set; }
    public long? Retention { get; set; }
    public string? Closed { get; set; }
    public Dictionary<string, InputReceipt> Receipts { get; set; } = new(StringComparer.Ordinal);
    public List<InvocationInput> Pending { get; set; } = [];
}

[Flags]
public enum AdmissionFlaw { None = 0, Hydrate = 1, ForgetIds = 2, IgnoreCapacity = 4, EarlyReply = 8, BlockedSignal = 16, DropMetadata = 32, IgnoreRetention = 64, KeepPayload = 128, Restamp = 256, UnsafeNull = 512, ReleaseClockEarly = 1024 }

internal sealed partial class Burst
{
    private bool AdmissionHas(AdmissionFlaw flaw) => (model.Options.AdmissionFlaws & flaw) != 0;

    private bool AdmitPrefix()
    {
        // A held composite signal may stay at the head while authored work waits. It cannot gate metadata admission.
        var first = AdmissionHas(AdmissionFlaw.BlockedSignal) ? turn.Inbox.FirstOrDefault()
            : turn.Inbox.FirstOrDefault(m => m.Kind != CompositeSignal.Kind);
        if (first is null || !model.HandlesMetadata(first)) return false;
        var prefix = true;
        foreach (var message in turn.Inbox)
        {
            if (message.Kind == CompositeSignal.Kind) { turn.Hold(message); continue; }
            prefix &= model.HandlesMetadata(message);
            if (!prefix) { turn.Hold(message); continue; }
            Admission result;
            try { result = Reduce(message); }
            catch (Exception error) when (error is JsonException or EncoderFallbackException)
            { result = new("", "", "Invalid", Actor: turn.Instance.Ref.Id); }
            if (AdmissionHas(AdmissionFlaw.EarlyReply)) turn.Emit(Request.Response, result);
            else turn.Reply(message, Request.Response, result);
            turn.Record($"admission:{result.Frame}:{result.Id}:{result.Result}:{result.Order}");
        }
        // The held suffix alone sleeps; one persisted wake also lets existing authored agenda work progress.
        if (turn.Inbox.Any(m => m.Kind != ReceiptQuery.Kind)) turn.Send(turn.Instance.Ref, NativeKinds.Continue);
        return true;
    }

    internal ValueTask MetadataAsync()
    {
        if (AdmissionHas(AdmissionFlaw.DropMetadata)) return ValueTask.CompletedTask;
        if (ended is { } status && turn.Clock.Resolve(WorkflowCommitClock.Terminal) is not null)
            InvocationLifetime.Terminate(_s, WorkflowCommitClock.Terminal, status switch
            { InstanceStatus.Finished => "completed", InstanceStatus.Cancelled => "cancelled", _ => "faulted" }, AdmissionHas(AdmissionFlaw.KeepPayload));
        foreach (var message in turn.Inbox.Where(model.HandlesMetadata))
        {
            Admission result;
            try { result = Reduce(message); }
            catch (Exception error) when (error is JsonException or EncoderFallbackException)
            { result = new("", "", "Invalid", Actor: turn.Instance.Ref.Id); }
            turn.Reply(message, Request.Response, result);
        }
        return ValueTask.CompletedTask;
    }

    private Admission Reduce(Message message) => message.Payload is null ? new("", "", "Invalid", Actor: turn.Instance.Ref.Id)
        : message.Kind == ReceiptQuery.Kind ? Query(message.Read<ReceiptQuery>()) : Admit(message.Read<InvocationInput>());

    private Admission Query(ReceiptQuery? query)
    {
        if (query is null || string.IsNullOrEmpty(query.Frame) || string.IsNullOrEmpty(query.Id) || query.Id.Length > 128)
            return new("", "", "Invalid", Actor: turn.Instance.Ref.Id);
        if (_s.Inboxes?.GetValueOrDefault(query.Frame) is not { } inbox)
            return new(query.Frame, query.Id, "UnknownInvocation", Actor: turn.Instance.Ref.Id);
        InvocationLifetime.Clean(_s, inbox, turn.Now, turn.Clock, AdmissionHas(AdmissionFlaw.ReleaseClockEarly));
        var receipt = inbox.Receipts.GetValueOrDefault(query.Id);
        return Receipt(query.Frame, query.Id, ended is not null && inbox.Closed is null ? "TerminalMetadataUnresolved"
            : receipt is null ? "MissingOrExpired" : "Found", receipt);
    }

    private Admission Receipt(string frame, string id, string result, InputReceipt? receipt)
    {
        var finalized = receipt?.Clock is { } reference ? turn.Clock.Resolve(reference) : null;
        var until = receipt is null ? null : InvocationLifetime.Deadline(receipt, turn.Clock);
        if (AdmissionHas(AdmissionFlaw.Restamp) && finalized is not null && receipt?.Retention is { } duration)
            until = checked(turn.Now + duration);
        return new(frame, id, result, receipt?.Order, receipt?.Outcome, turn.Instance.Ref.Id, receipt?.Status, until, finalized);
    }

    private Admission Admit(InvocationInput? input)
    {
        if (input is null) return new("", "", "Invalid", Actor: turn.Instance.Ref.Id);
        Admission Result(string result, InputReceipt? receipt = null) =>
            Receipt(input.Frame, input.Id, result, receipt);
        if (string.IsNullOrEmpty(input.Frame) || string.IsNullOrEmpty(input.Id) || input.Id.Length > 128 ||
            string.IsNullOrEmpty(input.Name) || input.Name.Length > 128 || input.Value?.Length > 1024)
            return Result("Invalid");
        if (_s.Inboxes?.GetValueOrDefault(input.Frame) is not { } inbox) return Result("UnknownInvocation");
        InvocationLifetime.Clean(_s, inbox, turn.Now, turn.Clock, AdmissionHas(AdmissionFlaw.ReleaseClockEarly));
        if (AdmissionHas(AdmissionFlaw.Hydrate))
            Hydrator.Create(_wf.Nodes[_s.Frames[input.Frame].Node], binding => Evaluate(binding, new FrameScope(this, _s.Frames[input.Frame])));
        var digest = Digest(input);
        if (!AdmissionHas(AdmissionFlaw.ForgetIds) && inbox.Receipts.TryGetValue(input.Id, out var existing))
            return Result(existing.Digest == digest ? "Duplicate" : "Conflict", existing);
        if (ended is not null && inbox.Closed is null) return Result("TerminalMetadataUnresolved");
        if (inbox.Closed is not null || ended is not null) return Result("Ended");
        if (!_s.Frames.ContainsKey(input.Frame)) return Result("UnknownInvocation");
        if (inbox.Retention is null) return Result("UnsupportedPolicy");
        if (inbox.Next == long.MaxValue) return Result("OrderExhausted");
        if (!AdmissionHas(AdmissionFlaw.IgnoreCapacity) && inbox.Pending.Count >= inbox.Capacity) return Result("Full");
        if (inbox.Receipts.Count >= inbox.ReceiptCapacity) return Result("ReceiptFull");
        var receipt = new InputReceipt(digest, ++inbox.Next, Retention: inbox.Retention);
        inbox.Receipts[input.Id] = receipt;
        inbox.Pending.Add(input);
        return Result("Accepted", receipt);
    }

    private static string Digest(InvocationInput input)
    {
        using var bytes = new MemoryStream();
        using var writer = new BinaryWriter(bytes, new UTF8Encoding(false, true), leaveOpen: true);
        writer.Write(1); // prototype fingerprint version, exact string envelope only
        writer.Write(input.Name);
        writer.Write(input.Value is not null);
        if (input.Value is not null) writer.Write(input.Value);
        writer.Flush();
        return "v1:" + Convert.ToHexString(SHA256.HashData(bytes.ToArray()));
    }

    private void CloseInbox(string frame, string reason)
    {
        if (_s.Inboxes?.GetValueOrDefault(frame) is not { Closed: null } inbox) return;
        var reference = inbox.Receipts.Values.Any(r => r.Retention is not null && (r.Status is "Queued" or "Selected"))
            ? turn.Clock.Capture() : null;
        InvocationLifetime.Close(_s, frame, reason, reference, AdmissionHas(AdmissionFlaw.KeepPayload));
    }

    internal void RegisterInbox(Frame frame, int capacity, int receipts, TimeSpan? retention)
    {
        if (capacity < 1 || receipts < capacity) throw new ArgumentOutOfRangeException(nameof(capacity));
        var duration = retention is null ? (long?)null : checked((long)Math.Ceiling(retention.Value.TotalMilliseconds));
        if (duration is <= 0) throw new ArgumentOutOfRangeException(nameof(retention));
        if (duration is not null) turn.Clock.Require(); // opt in before any promised window, even while all inputs are pending
        var inboxes = _s.Inboxes ??= new(StringComparer.Ordinal);
        if (!inboxes.TryAdd(frame.Id, new InvocationInbox { Capacity = capacity, ReceiptCapacity = receipts, Retention = duration }))
            throw new InvalidOperationException("Invocation inbox already registered.");
    }

    internal InvocationInput? SelectInput(Frame frame)
    {
        var inbox = _s.Inboxes![frame.Id];
        var input = inbox.Pending.FirstOrDefault(i => inbox.Receipts[i.Id].Status == "Queued");
        if (input is not null) inbox.Receipts[input.Id] = inbox.Receipts[input.Id] with { Status = "Selected" };
        return input;
    }

    internal void FinishInput(Frame frame, string id, string outcome)
    {
        var inbox = _s.Inboxes![frame.Id];
        if (!inbox.Receipts.TryGetValue(id, out var receipt) || receipt.Status != "Selected")
            throw new InvalidOperationException("Only a selected input may finish.");
        inbox.Pending.RemoveAll(i => i.Id == id);
        inbox.Receipts[id] = InvocationLifetime.Final(receipt, "Completed", outcome, receipt.Retention is not null ? turn.Clock.Capture() : null);
    }
}

public sealed partial class CompositeContext
{
    public void RegisterInputInbox(int capacity, int receiptCapacity, TimeSpan? retention = null) =>
        _burst.RegisterInbox(_frame, capacity, receiptCapacity, retention);
    public InvocationInput? SelectInput() => _burst.SelectInput(_frame);
    public void FinishInput(string id, string outcome) => _burst.FinishInput(_frame, id, outcome);
}
