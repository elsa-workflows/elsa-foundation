using System.Security.Cryptography;
using Kernel.Runtime;

namespace Kernel.Durable;

public sealed partial class InMemoryStore
{
    private sealed record ReferenceBinding(string Tenant, string Reference, CreationAddress Address, InboxKey Creator, string Fingerprint);
    internal sealed record ActorCheckpoint(string Reference, InstanceRow Row, long? EndedAt);
    internal sealed record ActorRead(long AsOf, ActorCheckpoint? Actor);
    internal sealed record ActorReadSet(long AsOf, IReadOnlyList<ActorCheckpoint> Actors);

    private readonly Lazy<byte[]> _referenceKey = new(() => RandomNumberGenerator.GetBytes(32));
    private readonly Dictionary<string, ReferenceBinding> _referenceBindings = new(StringComparer.Ordinal);
    private readonly Dictionary<CreationAddress, ReferenceBinding> _referenceBirths = [];
    private readonly Dictionary<CreationAddress, long> _foundationEnds = [];

    // A turn may form the ref for a prospective child before commit. Until the creator is stored, it has no binding.
    internal string ReferenceFor(string tenant, CreationAddress address)
    {
        var id = Convert.ToBase64String(DurableKeyJson.CodeUnits(address.Id));
        var encoded = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { Tenant = tenant, Id = id, address.Creation });
        return Convert.ToHexString(HMACSHA256.HashData(_referenceKey.Value, encoded));
    }

    /// <summary>Store a qualified root creator and its exact reference under the same lock.</summary>
    internal string? AdmitFoundationStart(string tenant, Message creator)
    {
        if (creator.Start is not { Creation: { Generation: > 0 } creation, Parent: null } start ||
            creator.Sender is not null || creator.TargetCreation != creation.Generation)
            throw new ArgumentException("A foundation root start needs an exact creation and no parent or sender.", nameof(creator));

        creator = creator.AsOrdinary();
        var fingerprint = CreationProtocol.Fingerprint(start, creator.Kind, creator.Payload)!;
        var address = new CreationAddress(creator.Target.Id, creation.Generation);
        var reference = ReferenceFor(tenant, address);
        lock (_gate)
        {
            if (!CreationIdentityAvailable) CreationProtocol.RejectUnsupported(creator);
            if (_referenceBirths.TryGetValue(address, out var birth))
                return birth.Tenant == tenant && birth.Creator == creator.InboxKey && birth.Fingerprint == fingerprint ? reference : null;

            var previous = Instances.GetValueOrDefault(address.Id);
            if (previous is { Creation: > 0 } &&
                (!_referenceBirths.TryGetValue(new(address.Id, previous.Creation), out var predecessor) || predecessor.Tenant != tenant))
                return null;
            if (CreationProtocol.StartError(previous, start, creator.Kind, creator.Payload, creator.InboxKey) is not null ||
                Messages.ContainsKey(creator.InboxKey) || _referenceBindings.ContainsKey(reference))
                return null;

            var binding = new ReferenceBinding(tenant, reference, address, creator.InboxKey, fingerprint);
            Messages.Add(creator.InboxKey, new MessageRow(creator, ++_seq, 0));
            InsertReference(binding);
            return reference;
        }
    }

    private void InsertReference(ReferenceBinding binding)
    {
        _referenceBindings.Add(binding.Reference, binding);
        _referenceBirths.Add(binding.Address, binding);
    }

    private ReferenceBinding BindChild(TurnCommit turn, InstanceRow? previous, Message creator)
    {
        var parentCreation = turn.Row?.Creation ?? previous?.Creation;
        if (parentCreation is not > 0 || !_referenceBirths.TryGetValue(new(turn.Instance, parentCreation.Value), out var parent) ||
            creator.Sender != turn.Instance || creator.SenderCreation != parentCreation ||
            creator.InboxScope != InboxKey.ChildCreatorScope || creator.Kind != Kinds.Start ||
            creator.Start is not { Creation: { Generation: 1, Previous: 0 } } start || creator.TargetCreation != 1 ||
            !creator.Target.ChildAddress || !CreationProtocol.IsPhysicalAddress(creator.Target.Id) || Instances.ContainsKey(creator.Target.Id) ||
            (start.Parent is null ? start.ParentCreation != 0 : start.Parent != turn.Instance || start.ParentCreation != parentCreation))
            throw new StoreException(Failure.Definite, "An exact child creator must come from its bound issuing actor.");

        var address = new CreationAddress(creator.Target.Id, 1);
        return new ReferenceBinding(parent.Tenant, ReferenceFor(parent.Tenant, address), address, creator.InboxKey,
            CreationProtocol.Fingerprint(start, creator.Kind, creator.Payload)!);
    }

    internal ActorRead ReadFoundationActor(string tenant, string? reference)
    {
        lock (_gate)
        {
            var now = clock();
            if (reference is null || !_referenceBindings.TryGetValue(reference, out var binding) || binding.Tenant != tenant ||
                Instances.GetValueOrDefault(binding.Address.Id) is not { Retired: false } row || row.Creation != binding.Address.Creation)
                return new(now, null);

            return new(now, Checkpoint(row, binding));
        }
    }

    internal ActorReadSet ReadFoundationActors(string tenant)
    {
        lock (_gate)
        {
            var asOf = clock();
            var actors = new List<ActorCheckpoint>();
            foreach (var row in Instances.Values)
                if (!row.Retired && _referenceBirths.TryGetValue(new(row.Id, row.Creation), out var binding) && binding.Tenant == tenant)
                    actors.Add(Checkpoint(row, binding));
            return new(asOf, actors);
        }
    }

    private ActorCheckpoint Checkpoint(InstanceRow row, ReferenceBinding binding) =>
        new(binding.Reference, row, _foundationEnds.TryGetValue(binding.Address, out var endedAt) ? endedAt : null);

    private void RecordFoundationEnd(InstanceRow row, long now)
    {
        if (_referenceBirths.Count == 0) return;
        var address = new CreationAddress(row.Id, row.Creation);
        if (row.Retired || row.Status == InstanceStatus.Running)
            _foundationEnds.Remove(address);
        else if (_referenceBirths.ContainsKey(address))
            _foundationEnds.TryAdd(address, now);
    }

    /// <summary>Resolve an outside exact send and insert its delivery in one operation.
    /// The caller supplies an id already scoped for storage.</summary>
    /// <returns>Null for an unavailable reference, false for a duplicate, true for a new delivery.</returns>
    internal bool? AddFoundationMessage(string tenant, string? reference, string id, string kind, string? payload = null, long? dueAt = null)
    {
        Kinds.RequireApplicationSend(kind);
        lock (_gate)
        {
            if (reference is null || !_referenceBindings.TryGetValue(reference, out var binding) || binding.Tenant != tenant)
                return null;

            var address = binding.Address;
            var row = Instances.GetValueOrDefault(address.Id);
            var live = row is { Retired: false } && row.Creation == address.Creation;
            var previous = row is null ? 0 : row.Retired ? row.Creation : -1;
            var starting = previous == address.Creation - 1 && Messages.TryGetValue(binding.Creator, out var creator) &&
                !creator.Done && creator.Message.Target.Id == address.Id && creator.Message.Start?.Creation?.Generation == address.Creation;
            if (!live && !starting) return null;

            var message = new Message(id, new InstanceRef(address.Id), kind, payload, Due: dueAt, TargetCreation: address.Creation);
            if (!CreationIdentityAvailable) CreationProtocol.RejectUnsupported(message);
            if (Messages.ContainsKey(message.InboxKey)) return false;
            Messages.Add(message.InboxKey, new MessageRow(message, ++_seq, 0));
            return true;
        }
    }
}
