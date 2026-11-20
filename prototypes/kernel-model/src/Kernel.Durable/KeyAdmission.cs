using System.Security.Cryptography;
using System.Text;
using Kernel.Runtime;

namespace Kernel.Durable;

/// <summary>Optional memory-only ingress experiment, not a default store/provider guarantee.
/// KeepForMillis is the proposed reuse window; Due is absolute store time.</summary>
public sealed record KeyRequest(string Key, string Operation, string Kind, string? Payload = null,
    StartSpec? Start = null, long KeepForMillis = 0, long? Due = null)
{
    public bool Creates => Start is not null;
    // Encode exact UTF-16 units before JSON so even malformed caller strings retain distinct identities.
    public string WireId => "key/" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Json.Write(new[] {
        Convert.ToBase64String(DurableKeyJson.CodeUnits(Key)), Convert.ToBase64String(DurableKeyJson.CodeUnits(Operation)) }))));
    public string EncodedKey => Exact(Key)!;
    public string EncodedOperation => Exact(Operation)!;
    private static string? Exact(string? value) => value is null ? null : Convert.ToBase64String(DurableKeyJson.CodeUnits(value));
    public string Digest => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Json.Write(new {
        Key = Exact(Key), Operation = Exact(Operation), Kind = Exact(Kind), Payload = Exact(Payload), KeepForMillis, Due,
        Start = Start is null ? null : new object?[] { Exact(Start.Model), Exact(Start.Definition), Exact(Start.Input),
            Exact(Start.Parent), Start.Ephemeral, Start.History, Start.ParentCreation, Start.ShortWait?.Ticks }
    }))));
    public void Validate()
    {
        if (string.IsNullOrEmpty(Key) || string.IsNullOrEmpty(Operation) || string.IsNullOrEmpty(Kind) ||
            Creates != (Kind == Kinds.Start) || (Creates ? KeepForMillis <= 0 : KeepForMillis != 0))
            throw new ArgumentException("A keyed start needs a start spec and positive reuse window; a command needs its kind.");
        if (Kind is Kinds.Job or Kinds.JobFinished or Kinds.Meet or Kinds.Publish or Kinds.Event or "child-finished")
            throw new ArgumentException("Internal deliveries cannot be admitted as keyed commands.");
        if (Start is { } spec && (spec.Parent is not null || spec.ParentCreation != 0 || spec.Creation is not null))
            throw new ArgumentException("Key admission owns root creation authority.");
    }
}

public sealed record KeyDecision(string Outcome, string? RunId, long? KeepUntil);
public interface IKeyAdmission
{
    ValueTask<KeyDecision> AcceptAsync(KeyRequest request, CancellationToken cancellationToken = default);
}

public sealed partial class InMemoryStore : IKeyAdmission
{
    private long _keySerial;
    private readonly Dictionary<string, (string Run, long Until)> _keyBindings = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Key, string Operation), (KeyRequest Request, KeyDecision Decision)> _keyReceipts = [];

    public ValueTask<KeyDecision> AcceptAsync(KeyRequest request, CancellationToken cancellationToken = default)
    {
        request.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var id = (request.Key, request.Operation);
            if (_keyReceipts.TryGetValue(id, out var receipt))
                return new(receipt.Request == request ? receipt.Decision : new KeyDecision("conflict", null, null));
            var assigned = _keyBindings.TryGetValue(request.Key, out var binding);
            var retired = assigned && Instances.GetValueOrDefault(binding.Run) is { Retired: true };
            var now = clock();
            if (request.Creates)
            {
                if (assigned && (now < binding.Until || !retired)) return new(new KeyDecision("in-use", null, null));
                var until = checked(now + request.KeepForMillis);
                // Separate from g*/child/* and e* event addresses. Store lifetime and checked serial forbid reuse.
                var target = FormattableString.Invariant($"g{InstanceRef.Spread(request.Key, Partitions.Length)}/key/{checked(++_keySerial)}");
                binding = (target, until);
            }
            else if (!assigned || retired) return new(new KeyDecision("missing", null, null));

            var start = request.Start;
            var message = new Message(request.WireId, new(binding.Run), request.Kind, request.Payload,
                Start: start, Due: request.Due);
            if (request.Creates) message = message.WithInboxScope(InboxKey.ChildCreatorScope);
            // Public Add/Post always strip supplied scopes; only this atomic authority inserts the creator.
            if (Messages.ContainsKey(message.InboxKey)) throw new InvalidOperationException("Key admission message identity already exists.");
            var decision = new KeyDecision("accepted", binding.Run, binding.Until);
            var seq = checked(_seq + 1);
            Messages.Add(message.InboxKey, new MessageRow(message, seq, 0));
            _seq = seq;
            _keyBindings[request.Key] = binding;
            _keyReceipts.Add(id, (request, decision));
            return new(decision);
        }
    }
}
