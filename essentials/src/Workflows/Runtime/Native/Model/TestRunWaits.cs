using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native;

/// <summary>Actor-local Test Run controls. The model verifies the committed Test Run scope before handling them.</summary>
public static class NativeTestRunWaitControl
{
    public const string Read = "native.test-run.waits";
    public const string Deliver = "native.test-run.deliver";
    public const int ReceiptLimit = 256;

    internal static bool Handles(string kind) => kind is Read or Deliver;
}

public sealed record NativeTestRunWaitQuery(string ScopeId);
public sealed record NativeTestRunWait(string WaitId, string FrameId, string NodeId, string Event);
public sealed record NativeTestRunWaitPage(IReadOnlyList<NativeTestRunWait> Waits);
public sealed record NativeTestRunEvent(string ScopeId, string WaitId, Guid DeliveryId, string ConfigurationDigest,
    string? StimulusDigest, JsonElement? Payload);
public sealed record NativeTestRunEventResult(string Status);
public sealed record NativeTestRunEventReceipt(string WaitId, string ConfigurationDigest, string StimulusDigest);

public sealed partial class NativeState
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<Guid, NativeTestRunEventReceipt>? TestRunEvents { get; set; }
}

internal sealed partial class Burst
{
    private void HandleTestRunWait(Contract.Message message)
    {
        if (message.Kind == NativeTestRunWaitControl.Read)
        {
            var request = Decode<NativeTestRunWaitQuery>(message);
            var waits = request is not null && OwnsTestRun(request.ScopeId) && ended is null
                ? ReadActiveTestRunWaits() : [];
            turn.Reply(message, new NativeTestRunWaitPage(waits));
        }
        else
        {
            var request = Decode<NativeTestRunEvent>(message);
            turn.Reply(message, new NativeTestRunEventResult(DeliverTestRunEvent(request)));
        }
    }

    private string DeliverTestRunEvent(NativeTestRunEvent? request)
    {
        if (request is null || !OwnsTestRun(request.ScopeId) || !ValidDigest(request.ConfigurationDigest))
            return "Rejected";
        if (_s.TestRunEvents?.GetValueOrDefault(request.DeliveryId) is { } existing)
            return existing.WaitId == request.WaitId && existing.ConfigurationDigest == request.ConfigurationDigest ? "AlreadyDelivered" : "Conflict";
        if (request.StimulusDigest is null) return "Unseen";
        if (!ValidDigest(request.StimulusDigest)) return "Rejected";
        if (_s.TestRunEvents?.Count >= NativeTestRunWaitControl.ReceiptLimit)
            return "Limit";
        if (ended is not null || !ReadActiveTestRunWaits().Any(wait => wait.WaitId == request.WaitId))
            return "NoLongerWaiting";
        var receipt = new NativeTestRunEventReceipt(request.WaitId, request.ConfigurationDigest, request.StimulusDigest);
        (_s.TestRunEvents ??= []).Add(request.DeliveryId, receipt);
        Resume(request.WaitId, request.Payload is { } payload ? JsonNode.Parse(payload.GetRawText()) : null);
        return "Delivered";
    }

    private NativeTestRunWait[] ReadActiveTestRunWaits()
    {
        var queued = _s.Agenda.Where(item => item.Resume).Select(item => item.Wait).ToHashSet(StringComparer.Ordinal);
        var active = new List<NativeTestRunWait>();
        foreach (var (id, wait) in _s.Waits)
        {
            const string prefix = "event:";
            if (wait.Kind != "event" || !wait.Key.StartsWith(prefix, StringComparison.Ordinal) || queued.Contains(id) ||
                !_s.Frames.TryGetValue(wait.Frame, out var frame) || _wf.Nodes.GetValueOrDefault(frame.Node)?.Type != typeof(Event))
                continue;
            active.Add(new(id, frame.Id, frame.Node, wait.Key[prefix.Length..]));
        }
        return [.. active];
    }

    private bool OwnsTestRun(string scope) => turn.TestScopeId is { } current && current == scope;

    private static bool ValidDigest(string? digest) =>
        digest is { Length: 64 } && digest.AsSpan().IndexOfAnyExcept("0123456789abcdef") < 0;

    private static T? Decode<T>(Contract.Message message) where T : class
    {
        try { return message.Payload is { } payload ? JsonSerializer.Deserialize<T>(payload) : null; }
        catch (JsonException) { return null; }
    }
}
