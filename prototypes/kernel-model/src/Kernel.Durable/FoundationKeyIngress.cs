using System.Security.Cryptography;
using Kernel.Runtime;

namespace Kernel.Durable;

public sealed partial class InMemoryStore
{
    internal enum KeyAdmissionOutcome { Accepted, Duplicate, Repeat, Missing, Conflict, ObligationOutstanding }
    internal sealed record KeyAdmissionReceipt(KeyAdmissionOutcome Outcome, string? Reference = null);

    private sealed record CallerBinding(string Name, InstanceRef Address, string Reference, long ReuseAfter);
    private sealed record CallerReceipt(string Digest, string Reference);

    // The in-memory profile folds case. The relational profiles apply their pinned collation at the binding row.
    private readonly Dictionary<string, CallerBinding> _foundationCallerBindings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Actor, string Operation), CallerReceipt> _foundationCallerReceipts = [];

    internal KeyAdmissionReceipt AdmitFoundationKey(string tenant, string key, string id, string kind, string? payload,
        StartSpec? spec, long? due, TimeSpan? keepFor, string? replyTo = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (!FoundationMetadataLimits.FitsName(key) || !FoundationMetadataLimits.FitsName(id, preservesCodeUnits: true) ||
            (spec is null ? !FoundationMetadataLimits.FitsKind(kind) || !FoundationMetadataLimits.FitsContent(payload) :
                !FoundationMetadataLimits.FitsContent(spec.Input, FoundationMetadataLimits.StartContentBytes)))
            return new(KeyAdmissionOutcome.Conflict);
        if (spec is null) Kinds.RequireApplicationSend(kind);
        var window = FoundationIngressWindow(keepFor);
        var qualified = tenant + "_" + key;
        var operation = Convert.ToHexString(SHA256.HashData(DurableKeyJson.CodeUnits(id)));
        lock (_gate)
        {
            var binding = _foundationCallerBindings.GetValueOrDefault(qualified);
            var name = binding?.Name ?? qualified;
            var address = binding?.Address ?? InstanceRef.In(InstanceRef.Spread(name, partitions), name);
            var receiptKey = (address.Id, operation);
            var request = new KeyRequest(address.Id, id, spec is null ? kind : Kinds.Start, spec?.Input ?? payload,
                spec, (long)window.TotalMilliseconds, due);

            if (_foundationCallerReceipts.TryGetValue(receiptKey, out var receipt))
                return receipt.Digest == request.Digest ? new(KeyAdmissionOutcome.Duplicate, receipt.Reference) : new(KeyAdmissionOutcome.Conflict);

            string reference;
            if (spec is null)
            {
                if (binding is null || AddFoundationMessage(tenant, binding.Reference, request.WireId,
                    kind, payload, due, keepFor, keyed: true, replyTo: replyTo) is null)
                    return new(KeyAdmissionOutcome.Missing);
                reference = binding.Reference;
            }
            else
            {
                var now = clock();
                var previous = binding is null ? null : Instances.GetValueOrDefault(binding.Address.Id);
                if (previous is { Retired: true, Cascade: not null })
                    return new(KeyAdmissionOutcome.ObligationOutstanding);
                if (binding is not null && (previous is not { Retired: true } || now < binding.ReuseAfter))
                    return new(KeyAdmissionOutcome.Repeat);

                var generation = checked((previous?.Creation ?? 0) + 1);
                var until = checked(now + (long)window.TotalMilliseconds);
                var start = spec with { Creation = new CreationStart(generation, generation - 1, operation) };
                var creator = new Message(request.WireId, address, Kinds.Start, start.Input, Start: start, Due: due, ReplyTo: replyTo,
                    TargetCreation: generation) { StoreStartInputOnce = true };
                var created = AdmitFoundationStart(tenant, creator);
                if (created is null) return new(KeyAdmissionOutcome.Conflict);

                reference = created;
                _foundationCallerBindings[qualified] = new(name, address, reference, until);
            }

            _foundationCallerReceipts.Add(receiptKey, new(request.Digest, reference));
            return new(KeyAdmissionOutcome.Accepted, reference);
        }
    }
}
