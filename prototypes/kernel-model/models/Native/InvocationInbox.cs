using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Kernel.Native;

// Bounded M0 research envelope. Not the production JSON/admission API or retention contract.
public sealed record InvocationInput(string Frame, string Id, string Name, string? Value = null)
{
    public const string Kind = "native.invocation-input";
}
public sealed record Admission(string Frame, string Id, string Result, long? Order = null, string? Outcome = null);
public sealed record InputReceipt(string Digest, long Order, string Status = "Queued", string? Outcome = null);
public sealed class InvocationInbox
{
    public int Capacity { get; set; }
    public int ReceiptCapacity { get; set; }
    public long Next { get; set; }
    public Dictionary<string, InputReceipt> Receipts { get; set; } = new(StringComparer.Ordinal);
    public List<InvocationInput> Pending { get; set; } = [];
}

[Flags]
public enum AdmissionFlaw { None = 0, Hydrate = 1, ForgetIds = 2, IgnoreCapacity = 4, EarlyReply = 8 }

internal sealed partial class Burst
{
    private bool AdmissionHas(AdmissionFlaw flaw) => (model.Options.AdmissionFlaws & flaw) != 0;

    private bool AdmitPrefix()
    {
        if (turn.Inbox.Count == 0 || turn.Inbox[0].Kind != InvocationInput.Kind) return false;
        var prefix = true;
        foreach (var message in turn.Inbox)
        {
            prefix &= message.Kind == InvocationInput.Kind;
            if (!prefix) { turn.Hold(message); continue; }
            Admission result;
            try { result = Admit(message.Read<InvocationInput>()); }
            catch (Exception error) when (error is JsonException or EncoderFallbackException)
            { result = new("", "", "Invalid"); }
            if (AdmissionHas(AdmissionFlaw.EarlyReply)) turn.Emit(Request.Response, result);
            else turn.Reply(message, Request.Response, result);
            turn.Record($"admission:{result.Frame}:{result.Id}:{result.Result}:{result.Order}");
        }
        // The held suffix alone sleeps; one persisted wake also lets existing authored agenda work progress.
        turn.Send(turn.Instance.Ref, NativeKinds.Continue);
        return true;
    }

    private Admission Admit(InvocationInput? input)
    {
        if (input is null) return new("", "", "Invalid");
        Admission Result(string result, InputReceipt? receipt = null) =>
            new(input.Frame, input.Id, result, receipt?.Order, receipt?.Outcome);
        if (string.IsNullOrEmpty(input.Frame) || string.IsNullOrEmpty(input.Id) || input.Id.Length > 128 ||
            string.IsNullOrEmpty(input.Name) || input.Name.Length > 128 || input.Value?.Length > 1024)
            return Result("Invalid");
        if (!_s.Frames.TryGetValue(input.Frame, out var frame) || _s.Inboxes?.GetValueOrDefault(input.Frame) is not { } inbox)
            return Result("UnknownInvocation");
        if (AdmissionHas(AdmissionFlaw.Hydrate))
            Hydrator.Create(_wf.Nodes[frame.Node], binding => Evaluate(binding, new FrameScope(this, frame)));
        var digest = Digest(input);
        if (!AdmissionHas(AdmissionFlaw.ForgetIds) && inbox.Receipts.TryGetValue(input.Id, out var existing))
            return Result(existing.Digest == digest ? "Duplicate" : "Conflict", existing);
        if (!AdmissionHas(AdmissionFlaw.IgnoreCapacity) && inbox.Pending.Count >= inbox.Capacity) return Result("Full");
        if (inbox.Receipts.Count >= inbox.ReceiptCapacity) return Result("ReceiptFull");
        var receipt = new InputReceipt(digest, ++inbox.Next);
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

    internal void RegisterInbox(Frame frame, int capacity, int receipts)
    {
        if (capacity < 1 || receipts < capacity) throw new ArgumentOutOfRangeException(nameof(capacity));
        var inboxes = _s.Inboxes ??= new(StringComparer.Ordinal);
        if (!inboxes.TryAdd(frame.Id, new InvocationInbox { Capacity = capacity, ReceiptCapacity = receipts }))
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
        inbox.Receipts[id] = receipt with { Status = "Completed", Outcome = outcome };
    }
}

public sealed partial class CompositeContext
{
    public void RegisterInputInbox(int capacity, int receiptCapacity) => _burst.RegisterInbox(_frame, capacity, receiptCapacity);
    public InvocationInput? SelectInput() => _burst.SelectInput(_frame);
    public void FinishInput(string id, string outcome) => _burst.FinishInput(_frame, id, outcome);
}
