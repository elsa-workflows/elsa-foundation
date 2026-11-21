using System.Security.Cryptography;
using Kernel.Runtime;

namespace Kernel.Durable;

public sealed partial class InMemoryStore
{
    internal sealed record FoundationStartReceipt(string Reference, bool Duplicate);
    private sealed record CreatedRoot(string Reference, long DeduplicateUntil);
    private readonly Dictionary<(string Tenant, string Operation), CreatedRoot> _foundationStarts = [];
    private readonly Dictionary<string, long> _foundationSendReceipts = new(StringComparer.Ordinal);

    internal FoundationStartReceipt AdmitGeneratedFoundationStart(string tenant, string id, StartSpec spec,
        long? due, TimeSpan? keepFor, string? replyTo = null, Worker? inlineOwner = null)
    {
        if (spec.Parent is not null || spec.Creation is not null)
            throw new ArgumentException("The foundation assigns the generated actor's creation.", nameof(spec));
        var partition = inlineOwner?.Fences.FirstOrDefault()?.Partition;
        var window = FoundationIngressWindow(keepFor);
        var operation = Convert.ToHexString(SHA256.HashData(DurableKeyJson.CodeUnits(id)));
        lock (_gate)
        {
            var now = clock();
            if (_foundationStarts.GetValueOrDefault((tenant, operation)) is { } prior && now <= prior.DeduplicateUntil)
                return new(prior.Reference, true);

            var name = tenant + "_" + Guid.NewGuid().ToString("N");
            var address = InstanceRef.In(InstanceRef.Spread(name, partitions), name);
            var exact = spec with { Creation = new CreationStart(1, 0, operation) };
            var creator = new Message(id, address, Kinds.Start, spec.Input, Start: exact, Due: due, ReplyTo: replyTo, TargetCreation: 1)
            {
                StorageId = "root:" + address.Id
            };
            var until = checked(now + (long)window.TotalMilliseconds);
            var reference = partition is { } local
                ? TryInlineRoot(inlineOwner!, local, name, tenant, creator)
                : null;
            reference ??= AdmitFoundationStart(tenant, creator) ??
                throw new StoreException(Failure.Definite, "A generated actor address already has a creator.");
            _foundationStarts[(tenant, operation)] = new(reference, until);
            return new(reference, false);
        }
    }

    private string? TryInlineRoot(Worker owner, int partition, string name, string tenant, Message creator)
    {
        var address = InstanceRef.In(partition, name);
        var local = creator with
        {
            Target = address, StorageId = "root:" + address.Id,
            Start = creator.Start! with { FoundationInlineTenant = tenant }
        };
        return owner.TryQueueInlineStart(local) ? ReferenceFor(tenant, new(address.Id, 1)) : null;
    }

    private TimeSpan FoundationIngressWindow(TimeSpan? keepFor)
    {
        var window = keepFor is { } requested && requested < IngressDeduplicationWindow ? requested : IngressDeduplicationWindow;
        ArgumentOutOfRangeException.ThrowIfLessThan(window, TimeSpan.Zero, nameof(keepFor));
        return window;
    }

    private int SweepFoundationIngressReceipts(long now)
    {
        var expired = _foundationStarts.Where(pair => pair.Value.DeduplicateUntil < now).Select(pair => pair.Key).ToArray();
        foreach (var key in expired) _foundationStarts.Remove(key);
        var sends = _foundationSendReceipts.Where(pair => pair.Value < now).Select(pair => pair.Key).ToArray();
        foreach (var key in sends) _foundationSendReceipts.Remove(key);
        return expired.Length + sends.Length;
    }
}
