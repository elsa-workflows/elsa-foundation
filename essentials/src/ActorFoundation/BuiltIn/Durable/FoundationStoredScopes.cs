namespace Kernel.Durable;

internal enum FoundationScopeCheck { Load, Poll, Commit, Sweep }

public sealed partial class InMemoryStore
{
    // In-memory representation of the checkpoint's persisted tenant field. The exact-creation
    // binding is the independent authority used to check it.
    internal Dictionary<CreationAddress, string> FoundationCheckpointTenants { get; } = [];
    internal Action<FoundationScopeCheck>? OnInvalidStoredScope { get; set; }

    private MessageRow StoreMessage(Message message, long seq, long seen)
    {
        var tenant = _referenceBirths.GetValueOrDefault(MessageAddress(message))?.Tenant;
        return new(message, seq, seen) { Scoped = tenant is not null, Tenant = tenant };
    }

    private static CreationAddress MessageAddress(Message message) => new(message.Target.Id, message.TargetCreation ?? 0);

    private bool HasCheckpointScope(InstanceRow instance)
    {
        var address = new CreationAddress(instance.Id, instance.Creation);
        return !_referenceBirths.TryGetValue(address, out var binding) ||
            FoundationCheckpointTenants.GetValueOrDefault(address) == binding.Tenant;
    }

    private bool HasDeliveryScope(MessageRow delivery, FoundationScopeCheck check)
    {
        if (!delivery.Scoped) return true; // the prototype also hosts its unqualified compatibility workloads
        var address = MessageAddress(delivery.Message);
        var actual = _referenceBirths.GetValueOrDefault(address)?.Tenant;
        if (actual is not null && delivery.Tenant == actual &&
            (Instances.GetValueOrDefault(address.Id) is not { } current || current.Creation != address.Creation || HasCheckpointScope(current)))
            return true;
        OnInvalidStoredScope?.Invoke(check);
        return false;
    }

    private bool HasCheckpointScope(InstanceRow instance, FoundationScopeCheck check)
    {
        if (HasCheckpointScope(instance)) return true;
        OnInvalidStoredScope?.Invoke(check);
        return false;
    }
}
