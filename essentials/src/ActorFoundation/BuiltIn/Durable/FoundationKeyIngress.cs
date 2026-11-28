using static Kernel.Durable.FoundationKeyAdmission;

namespace Kernel.Durable;

public sealed partial class InMemoryStore
{
    private sealed record CallerReceipt(string Digest, string Reference);

    // The in-memory profile folds case. The relational profiles apply their pinned collation at the binding row.
    private readonly Dictionary<string, Binding> _foundationCallerBindings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Actor, string Operation), CallerReceipt> _foundationCallerReceipts = [];

    internal Receipt AdmitFoundationKey(string tenant, string key, string id, string kind, string? payload,
        StartSpec? spec, long? due, TimeSpan? keepFor, string? replyTo = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (!FitsRequest(key, id, kind, payload, spec)) return new(Outcome.Invalid);
        if (spec is null) Kinds.RequireApplicationSend(kind);
        var window = FoundationIngressWindow(keepFor);
        var qualified = QualifiedName(tenant, key);
        var operation = FoundationRootCreator.OperationDigest(id);
        lock (_gate)
        {
            var binding = _foundationCallerBindings.GetValueOrDefault(qualified);
            var address = binding?.Address ?? ProposedAddress(qualified, partitions);
            var receiptKey = (address.Id, operation);
            var request = new KeyRequest(address.Id, id, spec is null ? kind : Kinds.Start, spec?.Input ?? payload,
                spec, (long)window.TotalMilliseconds, due);

            if (_foundationCallerReceipts.TryGetValue(receiptKey, out var receipt))
                return receipt.Digest == request.Digest ? new(Outcome.Duplicate, receipt.Reference) : new(Outcome.Conflict);

            string reference;
            if (spec is null)
            {
                if (binding is null || AddFoundationMessage(tenant, binding.Reference, request.WireId,
                    kind, payload, due, keepFor, keyed: true, replyTo: replyTo) is null)
                    return new(Outcome.Missing);
                reference = binding.Reference;
            }
            else
            {
                var previous = binding is null ? null : Instances.GetValueOrDefault(binding.Address.Id);
                Head? head = previous is null ? null : new(previous.Creation, previous.Retired, previous.HasCascadeObligation);
                var plan = PlanStart(binding, head, clock(), request.KeepForMillis, operation);
                if (plan.Creation is not { } creation) return new(plan.Outcome);

                var start = spec with { Creation = creation };
                var creator = new Message(request.WireId, address, Kinds.Start, start.Input, Start: start, Due: due, ReplyTo: replyTo,
                    TargetCreation: creation.Generation) { StoreStartInputOnce = true };
                var created = AdmitFoundationStart(tenant, creator);
                if (created is null) return new(Outcome.Conflict);

                reference = created;
                _foundationCallerBindings[qualified] = new(address, creation.Generation, reference, plan.ReuseAfter);
            }

            _foundationCallerReceipts.Add(receiptKey, new(request.Digest, reference));
            return new(Outcome.Accepted, reference);
        }
    }
}
