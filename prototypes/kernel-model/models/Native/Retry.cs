using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Kernel.Native;

// Private ordinary recovery control; not chart input or a product operator API.
internal sealed record NativeRetry(string Id, string Source, string Incident, string Reason)
{
    internal const string Kind = "native.retry-intervention";
}
internal sealed record RetryChild(string Source, string Successor, string Node, string? Tag);
internal sealed record NativeRetryReceipt(NativeRetry Request, Frame Source, string Successor, string Resolution = "Retried");
internal sealed record NativeRetryResult(string Id, string Result, string? Successor = null);

public sealed partial class NativeState
{
    [JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    internal Dictionary<string, NativeRetryReceipt>? Retries { get; set; }
}
public sealed partial class Frame
{
    [JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    internal JsonObject? RecoveryMaterial { get; set; }
    [JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    internal string? RetrySource { get; set; }
}
public sealed partial class Work
{
    [JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    internal JsonObject? RecoveryInputs { get; set; }
    [JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    internal JsonObject? RecoveryMaterial { get; set; }
    [JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    internal string? OperationKey { get; set; }
    [JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    internal string? RetrySource { get; set; }
}

internal static class NativeRetries
{
    internal static bool Present(NativeState s) => s.Retries is not null || s.Frames.Values.Any(f => f.RecoveryMaterial is not null || f.RetrySource is not null) ||
        s.Agenda.Any(w => w.RecoveryInputs is not null || w.RecoveryMaterial is not null || w.OperationKey is not null || w.RetrySource is not null);
    internal static void Validate(NativeState s)
    {
        var successors = new HashSet<string>(StringComparer.Ordinal); var sources = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (id, r) in s.Retries ?? [])
            if (r is null || r.Request is null || string.IsNullOrEmpty(id) || id != r.Request.Id || r.Source is null || r.Source.Id != r.Request.Source ||
                r.Source.Intervention?.Id != r.Request.Incident || r.Source.RecoveryInputs is null || r.Source.RecoveryMaterial is null || string.IsNullOrEmpty(r.Source.OperationKey) ||
                r.Resolution != "Retried" || string.IsNullOrEmpty(r.Successor) || r.Successor == r.Source.Id || !successors.Add(r.Successor) || !sources.Add(r.Source.Id) || s.Frames.ContainsKey(r.Source.Id))
                throw new InvalidOperationException("Invalid native retry lineage.");
        NativeRetryReceipt Lineage(string source, string successor) => (s.Retries?.Values.SingleOrDefault(r => r.Source.Id == source && r.Successor == successor))
            ?? throw new InvalidOperationException("Missing native retry lineage.");
        foreach (var f in s.Frames.Values)
        {
            if (f.RecoveryMaterial is not null && f.RecoveryInputs is null) throw new InvalidOperationException("Invalid native recovery material ownership.");
            if (f.RetrySource is { } source)
            {
                var old = Lineage(source, f.Id).Source;
                if (f.Node != old.Node || f.Parent != old.Parent || f.Tag != old.Tag || f.OperationKey != old.OperationKey ||
                    !JsonNode.DeepEquals(f.RecoveryInputs, old.RecoveryInputs) || !JsonNode.DeepEquals(f.RecoveryMaterial, old.RecoveryMaterial))
                    throw new InvalidOperationException("Invalid native successor frame provenance.");
            }
        }
        foreach (var w in s.Agenda)
        {
            if (w.RetrySource is null)
            {
                if (w.RecoveryInputs is not null || w.RecoveryMaterial is not null || w.OperationKey is not null) throw new InvalidOperationException("Invalid native successor work provenance.");
                continue;
            }
            var old = Lineage(w.RetrySource, w.Frame).Source;
            if (w.Resume || w.Node != old.Node || w.Parent != old.Parent || w.Tag != old.Tag || w.OperationKey != old.OperationKey || w.Attempt is < 0 or > 1 ||
                !JsonNode.DeepEquals(w.RecoveryInputs, old.RecoveryInputs) || !JsonNode.DeepEquals(w.RecoveryMaterial, old.RecoveryMaterial))
                throw new InvalidOperationException("Invalid native successor work provenance.");
        }
    }
}

internal sealed partial class Burst
{
    private bool _retryAccepted;
    private NativeRetryResult Retry(Message message)
    {
        NativeRetry? request;
        try { request = message.Payload is null ? null : message.Read<NativeRetry>(); }
        catch (System.Text.Json.JsonException) { return new("", "Invalid"); }
        NativeRetryResult Result(string result, string? successor = null) => new(request?.Id ?? "", result, successor);
        if (request is null || string.IsNullOrEmpty(request.Id) || request.Id.Length > 128 || string.IsNullOrEmpty(request.Source) ||
            string.IsNullOrEmpty(request.Incident) || string.IsNullOrEmpty(request.Reason) || request.Reason.Length > 1024) return Result("Invalid");
        if (message.TargetCreation != turn.Instance.Creation || turn.Instance.Creation <= 0) return Result("CreationMismatch");
        if (_s.Retries?.GetValueOrDefault(request.Id) is { } previous)
            return previous.Request == request ? Result("Duplicate", previous.Successor) : Result("Conflict");
        if (!_s.InterventionEnabled || !_s.Frames.TryGetValue(request.Source, out var source) || source.Intervention is not { Outcome: "WaitForIntervention" } incident || incident.Id != request.Incident)
            return Result("NotEligible");
        if (source.RecoveryMaterial is null) return Result("MaterialIdentityUnavailable"); // v5 incidents are not blessed by the newest registry
        if (source.Parent is null || !_s.Frames.TryGetValue(source.Parent, out var parent) || Blocked(parent.Id)) return Result("ParentUnavailable");
        if (!JsonNode.DeepEquals(source.RecoveryMaterial, NativeRecoveryMaterial.Describe(_wf))) return Result("MaterialUnavailable");
        if (!_wf.Nodes.TryGetValue(source.Node, out var node) || !typeof(ILeaf).IsAssignableFrom(node.Type) || node.Background || source.RecoveryInputs is null ||
            Hydrator.Of(node.Type).Any(p => !source.RecoveryInputs.ContainsKey(p.Name)) || !InterventionParent(source)) return Result("UnsupportedBoundary");
        if (_s.Waits.Values.Any(w => w.Frame == source.Id) || _s.Frames.Values.Any(f => f.Parent == source.Id) ||
            _s.Agenda.Any(w => w.Frame == source.Id || w.Parent == source.Id) || (_s.LexicalScopes?.Values.Any(s => s.Owner == source.Id) ?? false)) return Result("SourceBusy");
        if (source.LexicalScope is { } scope) _ = LexicalScopes.Accessible(_s, parent.Id, scope);
        if (_s.Next == long.MaxValue) return Result("IdentityExhausted");
        var successor = "f" + (_s.Next + 1);
        if (_s.Frames.ContainsKey(successor) || _s.Agenda.Any(w => w.Frame == successor)) return Result("IdentityConflict");
        var parentNode = _wf.Nodes[parent.Node]; JsonNode? prepared;
        try
        {
            // Known structural parents have pure, value-only preparation; no source or parent input binding is evaluated.
            prepared = ((IInterventionParent)Activator.CreateInstance(parentNode.Type)!).PrepareRetry(parentNode, parent.State?.DeepClone(), new(source.Id, successor, source.Node, source.Tag));
        }
        catch (Exception error) when (error is InvalidOperationException or System.Text.Json.JsonException) { return Result("ParentObligationUnavailable"); }
        var archived = Json.Read<Frame>(Json.Write(source));
        var work = new Work { Frame = successor, Node = source.Node, Parent = source.Parent, Tag = source.Tag, LexicalScope = source.LexicalScope,
            Scope = source.Scope?.ToDictionary(p => p.Key, p => p.Value?.DeepClone(), StringComparer.Ordinal), RetrySource = source.Id,
            RecoveryInputs = (JsonObject)source.RecoveryInputs.DeepClone(), RecoveryMaterial = (JsonObject)source.RecoveryMaterial.DeepClone(), OperationKey = source.OperationKey };
        // All preparation completed. One normal checkpoint owns resolution, parent rebind and the owed successor.
        (_s.Retries ??= new(StringComparer.Ordinal)).Add(request.Id, new(request, archived, successor));
        _s.Frames.Remove(source.Id); parent.State = prepared; _s.Next++; _s.Agenda.Add(work); _retryAccepted = true;
        turn.Record($"retry:{request.Id}:{source.Id}:{successor}");
        return Result("Accepted", successor);
    }
}
