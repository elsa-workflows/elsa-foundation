using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native;

/// <summary>Actor-local Test Run controls. The model verifies the committed Test Run scope before handling them.</summary>
public static class NativeTestRunWaitControl
{
    public const string Deliver = "native.test-run.deliver";
    public const int ReceiptLimit = 256;

    public static IReadOnlyList<NativeTestRunWait> ReadActive(NativeState state)
    {
        var queued = state.Agenda.Where(item => item.Resume).Select(item => item.Wait).ToHashSet(StringComparer.Ordinal);
        var active = new List<NativeTestRunWait>();
        foreach (var (id, wait) in state.Waits)
        {
            const string prefix = "event:";
            if (wait.Kind != "event" || !wait.Key.StartsWith(prefix, StringComparison.Ordinal) || queued.Contains(id) ||
                !state.Frames.TryGetValue(wait.Frame, out var frame))
                continue;
            active.Add(new(id, frame.Id, frame.Node, wait.Key[prefix.Length..]));
        }
        return active;
    }
}

public sealed record NativeTestRunWait(string WaitId, string FrameId, string NodeId, string Event);
public sealed record NativeTestRunEvent(string ScopeId, string WaitId, Guid DeliveryId, string ConfigurationDigest,
    string StimulusDigest, JsonElement? Payload);
public sealed record NativeTestRunEventReceipt(string WaitId, string ConfigurationDigest, string StimulusDigest);

public sealed partial class NativeState
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<Guid, NativeTestRunEventReceipt>? TestRunEvents { get; set; }
}

internal sealed partial class Burst
{
    private void AcceptTestRunWait(Contract.Message message)
    {
        if (turn.TestScopeId is not { } scope || Decode(message) is not { } request || request.ScopeId != scope ||
            !ValidDigest(request.ConfigurationDigest) || !ValidDigest(request.StimulusDigest) ||
            _s.TestRunEvents?.ContainsKey(request.DeliveryId) == true || _s.TestRunEvents?.Count >= NativeTestRunWaitControl.ReceiptLimit)
            return;
        if (NativeTestRunWaitControl.ReadActive(_s).SingleOrDefault(wait => wait.WaitId == request.WaitId) is not { } selected ||
            _wf.Nodes.GetValueOrDefault(selected.NodeId)?.Type != typeof(Event))
            return;
        var receipt = new NativeTestRunEventReceipt(request.WaitId, request.ConfigurationDigest, request.StimulusDigest);
        (_s.TestRunEvents ??= []).Add(request.DeliveryId, receipt);
        Resume(request.WaitId, request.Payload is { } payload ? JsonNode.Parse(payload.GetRawText()) : null);
    }

    private static bool ValidDigest(string? digest) =>
        digest is { Length: 64 } && digest.AsSpan().IndexOfAnyExcept("0123456789abcdef") < 0;

    private static NativeTestRunEvent? Decode(Contract.Message message)
    {
        try { return message.Payload is { } payload ? JsonSerializer.Deserialize<NativeTestRunEvent>(payload) : null; }
        catch (JsonException) { return null; }
    }
}
