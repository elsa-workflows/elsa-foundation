using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using System.Security.Cryptography;
using Kernel.Runtime;

namespace Kernel.Durable;

public sealed partial class InMemoryStore
{
    private sealed record ReferenceBinding(string Tenant, string Reference, CreationAddress Address, InboxKey Creator, string Fingerprint);
    internal sealed record FoundationParkMark(long Revision, long At);
    internal sealed record FoundationTerminalMark(long Revision, long At);
    internal sealed record ActorCheckpoint(string Reference, InstanceRow Row, FoundationTerminalMark? Terminal, FoundationHeaderChange Header, FoundationParkMark? Park)
    {
        internal long? EndedAt => Terminal?.At;
    }
    internal sealed record ActorRead(long AsOf, ActorCheckpoint? Actor);
    internal sealed record ActorReadSet(long AsOf, IReadOnlyList<ActorCheckpoint> Actors);
    internal sealed record ActorJournalRead(long AsOf, IReadOnlyList<JournalRow> Rows);

    private readonly Lazy<byte[]> _referenceKey = new(() => RandomNumberGenerator.GetBytes(32));
    private readonly Dictionary<string, ReferenceBinding> _referenceBindings = new(StringComparer.Ordinal);
    private readonly Dictionary<CreationAddress, ReferenceBinding> _referenceBirths = [];
    private readonly Dictionary<CreationAddress, FoundationTerminalMark> _foundationEnds = [];
    internal TimeSpan IngressDeduplicationWindow { get; init; } = TimeSpan.FromMinutes(1);
    private readonly Dictionary<CreationAddress, FoundationHeaderChange> _foundationHeaders = [];
    private readonly Dictionary<CreationAddress, FoundationParkMark> _foundationParks = [];

    // A turn may form the ref for a prospective child before commit. Until the creator is stored, it has no binding.
    internal string ReferenceFor(string tenant, CreationAddress address)
    {
        return FoundationRootCreator.ReferenceFor(_referenceKey.Value, tenant, address);
    }

    internal bool HasFoundationTimer(InboxKey key, string actor, long creation)
    {
        lock (_gate)
            return Messages.GetValueOrDefault(key) is { Done: false, Message: { } message } &&
                message.ActorAuthored && message.Sender == actor && message.SenderCreation == creation &&
                message.Target.Id == actor && message.TargetCreation == creation;
    }

    internal bool HasStoredJobDeadline(string actor, long creation, string id)
    {
        lock (_gate)
            return Messages.GetValueOrDefault(InboxKey.Issued(id)) is { Done: false, Message: { Kind: Kinds.JobDeadline } timer } &&
                timer.Target.Id == actor && timer.TargetCreation == creation;
    }

    internal bool HasFoundationJobResult(string actor, long creation, string key, long? generation = null)
    {
        lock (_gate)
        {
            var row = Instances.GetValueOrDefault(actor);
            var current = generation ?? (row?.Creation == creation ? JobRegistry.Generation(row, key) : null) ?? 0;
            return Messages.GetValueOrDefault(InboxKey.JobResult(actor, key, creation, current)) is
                { Done: false, Message: { Kind: Kinds.JobFinished, FoundationJobResult: true } };
        }
    }

    internal (JobTicket? Ticket, int Attempts) FoundationJob(string actor, long creation, string key, long generation)
    {
        lock (_gate)
            return Messages.GetValueOrDefault(InboxKey.Ticket(actor, key, creation, generation)) is { Done: false, Message: { Kind: Kinds.Job } ticket } stored
                ? (ticket.Read<JobTicket>(), stored.Attempts) : default;
    }

    internal ActorCheckpoint FoundationTurnHeader(InstanceRow row)
    {
        lock (_gate)
        {
            if (row.FoundationInlineBirth is { } birth)
                return new(ReferenceFor(birth.Tenant, new(row.Id, row.Creation)), row, null,
                    row.FoundationInlineHeader ?? new(row.Definition, FrozenDictionary<string, string>.Empty), null);
            return Checkpoint(row, _referenceBirths[new(row.Id, row.Creation)]);
        }
    }

    internal (string Tenant, string Reference) FoundationIdentity(string id, long creation)
    {
        lock (_gate)
        {
            var binding = _referenceBirths[new(id, creation)];
            return (binding.Tenant, binding.Reference);
        }
    }

    string? IFoundationSchedulingStore.FoundationScheduleTenant(CreationAddress address)
    {
        if (address.Creation <= 0) return null;
        lock (_gate) return _referenceBirths.GetValueOrDefault(address)?.Tenant;
    }

    internal string? FoundationReferenceOf(string tenant, string? id, long creation)
    {
        lock (_gate)
            return id is not null && _referenceBirths.TryGetValue(new(id, creation), out var binding) && binding.Tenant == tenant
                ? binding.Reference : null;
    }

    internal CreationAddress? FoundationTarget(string tenant, string? reference)
    {
        lock (_gate)
            return reference is not null && _referenceBindings.TryGetValue(reference, out var binding) && binding.Tenant == tenant
                ? binding.Address : null;
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
            InsertReference(binding);
            Messages.Add(creator.InboxKey, StoreMessage(creator, ++_seq, 0));
            return reference;
        }
    }

    private void InsertReference(ReferenceBinding binding)
    {
        _referenceBindings.Add(binding.Reference, binding);
        _referenceBirths.Add(binding.Address, binding);
    }

    private ReferenceBinding BindInlineRoot(InstanceRow row)
    {
        var (tenant, creator) = row.FoundationInlineBirth!.Value;
        var address = new CreationAddress(row.Id, row.Creation);
        var reference = ReferenceFor(tenant, address);
        if (row is not { Creation: 1, Parent: null, Retired: false, StartIntent: not null } ||
            Instances.ContainsKey(row.Id) || _referenceBirths.ContainsKey(address) || _referenceBindings.ContainsKey(reference))
            throw new StoreException(Failure.Definite, "An inline actor can publish its root binding once.");
        return new(tenant, reference, address, creator, row.StartIntent);
    }

    private ReferenceBinding BindChild(TurnCommit turn, InstanceRow? previous, Message creator, ReferenceBinding? inlineRoot)
    {
        var parentCreation = turn.Row?.Creation ?? previous?.Creation;
        var parent = inlineRoot ?? (parentCreation is > 0 ? _referenceBirths.GetValueOrDefault(new(turn.Instance, parentCreation.Value)) : null);
        if (parentCreation is not > 0 || parent is null ||
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
            return BoundActor(tenant, reference) is { } found
                ? new(now, Checkpoint(found.Row, found.Binding))
                : new(now, null);
        }
    }

    // The same commit cannot retire the life or replace any of its rows between resolution and the read.
    internal ActorJournalRead ReadFoundationJournal(string tenant, string? reference, long atOrAfter, int takeRows)
    {
        lock (_gate)
        {
            var now = clock();
            if (BoundActor(tenant, reference) is not { } found) return new(now, []);
            var address = found.Binding.Address;
            return new(now, Journal.Values.Where(row => row.Instance == address.Id &&
                    row.FoundationCreation == address.Creation && row.Position >= atOrAfter)
                .OrderBy(row => row.Position).Take(takeRows).ToArray());
        }
    }

    internal sealed record ActorDelivery(long Seq, Message Message, bool Held, int Attempts, string? Sender,
        ParkKeepReason? KeptBy, string Handle)
    {
        internal long Order => Message.DeliveryOrder(Seq);
    }
    internal sealed record ActorInboxRead(long AsOf, IReadOnlyList<ActorDelivery> Rows);

    internal ActorInboxRead ReadFoundationInbox(string tenant, string? reference, long after, int take)
    {
        lock (_gate)
        {
            var now = clock();
            if (BoundActor(tenant, reference) is not { } found) return new(now, []);
            var address = found.Binding.Address;
            var hasPark = _foundationParks.ContainsKey(address);
            var rows = Messages.Values.Where(row => (!row.Done || hasPark && row.KeptBy is not null) &&
                    row.Message.DeliveryOrder(row.Seq) > after && row.Message.Target.Id == address.Id &&
                    row.Message.TargetCreation == address.Creation)
                .OrderBy(row => row.Message.DeliveryOrder(row.Seq)).Take(take).Select(row =>
                {
                    var message = row.Message;
                    var kept = row.Done ? row.KeptBy : null;
                    var refId = found.Binding.Reference;
                    // Seal a retry handle under the same lock as the Park and row read.
                    var handle = kept is not null ? SealFoundationKeptHandle(tenant, refId, message.InboxKey) : null;
                    var sender = message.Sender is { } from &&
                        _referenceBirths.TryGetValue(new(from, message.SenderCreation), out var binding) && binding.Tenant == tenant
                        ? binding.Reference : null;
                    return new ActorDelivery(row.Seq, message, row.Held, row.Attempts, sender, kept,
                        handle ?? DeliveryHandle(refId, row.Seq, message.InboxKey));
                }).ToArray();
            return new(now, rows);
        }
    }

    internal string MessageHandle(string id) =>
        FoundationSelfSend.Handle(_referenceKey.Value, id);

    internal string DeliveryHandle(string reference, long seq, InboxKey key) =>
        MessageHandle("delivery:" + Json.Write(new
        {
            Ref = reference,
            Seq = seq,
            Id = Convert.ToBase64String(DurableKeyJson.CodeUnits(key.Id)),
            key.Scope,
            key.Match
        }));

    private (ReferenceBinding Binding, InstanceRow Row)? BoundActor(string tenant, string? reference)
    {
        if (reference is null || !_referenceBindings.TryGetValue(reference, out var binding) || binding.Tenant != tenant ||
            Instances.GetValueOrDefault(binding.Address.Id) is not { Retired: false } row || row.Creation != binding.Address.Creation ||
            !HasCheckpointScope(row))
            return null;
        return (binding, row);
    }

    internal ActorReadSet ReadFoundationActors(string tenant)
    {
        lock (_gate)
        {
            var asOf = clock();
            var actors = new List<ActorCheckpoint>();
            foreach (var row in Instances.Values)
                if (!row.Retired && _referenceBirths.TryGetValue(new(row.Id, row.Creation), out var binding) && binding.Tenant == tenant && HasCheckpointScope(row))
                    actors.Add(Checkpoint(row, binding));
            return new(asOf, actors);
        }
    }

    private ActorCheckpoint Checkpoint(InstanceRow row, ReferenceBinding binding) =>
        new(binding.Reference, row, _foundationEnds.GetValueOrDefault(binding.Address),
            _foundationHeaders.GetValueOrDefault(binding.Address) ?? LegacyProjection(row),
            _foundationParks.GetValueOrDefault(binding.Address));

    private static FoundationHeaderChange LegacyProjection(InstanceRow row) => new(row.Definition,
        new Dictionary<string, string>
        {
            ["definition"] = row.Definition,
            ["version"] = row.StateVersion.ToString(CultureInfo.InvariantCulture)
        }.ToFrozenDictionary(StringComparer.Ordinal));

    private static void ValidateFoundationHeader(TurnCommit turn)
    {
        if (turn.FoundationHeader is not { } header) return;
        if (turn.Row is not { Retired: false } || header.Labels.Count > 4 ||
            header.Labels.Any(label => Encoding.UTF8.GetByteCount(label.Key) > 1024 || Encoding.UTF8.GetByteCount(label.Value) > 1024))
            throw new StoreException(Failure.Definite, "Actor headers carry at most four labels, with each name and value at most 1,024 bytes.");
    }

    private void RecordFoundationMetadata(TurnCommit turn, InstanceRow row, InstanceRow? previous, long now, Message? firstExpiry)
    {
        if (_referenceBirths.Count == 0) return;
        var address = new CreationAddress(row.Id, row.Creation);
        if (!_referenceBirths.ContainsKey(address)) return;
        if (row.Retired)
        {
            RemoveFoundationMetadata(address);
            return;
        }

        if (turn.FoundationHeader is { } replacement)
            _foundationHeaders[address] = new(replacement.TypeData, replacement.Labels.ToFrozenDictionary(StringComparer.Ordinal));

        if (row.Status == InstanceStatus.Running)
        {
            _foundationEnds.Remove(address);
            if (row.ParkRecovery is null && row.ParkDiscard?.Kind != ParkClearKind.Terminate) _foundationParks.Remove(address);
        }
        else
        {
            var continuingPark = row.Status == InstanceStatus.Faulted && row.ParkRecovery is null && !row.ParkDiscarded
                ? _foundationParks.GetValueOrDefault(address) : null;
            if (continuingPark is not null)
                _foundationEnds.TryAdd(address, new(continuingPark.Revision, continuingPark.At));
            else if (previous is null or { Retired: true } or { Status: InstanceStatus.Running })
                _foundationEnds.TryAdd(address, new(row.Revision, now));
        }
        if (previous?.ParkRecovery is not null && row.ParkRecovery is null &&
            row.ParkDiscard?.Kind != ParkClearKind.Terminate && turn.FoundationKept is null)
        {
            _foundationParks.Remove(address);
            _foundationParkExpiries.Remove(address);
        }
        else RecordParkExpiry(turn, row, address, firstExpiry);
        if (row.ParkDiscarded && _foundationParks.Remove(address))
            _foundationEnds[address] = new(row.Revision, now);
        if (turn.OpensPark)
            _foundationParks[address] = new(row.Revision, now);
    }

    private void RemoveFoundationMetadata(CreationAddress address)
    {
        _foundationEnds.Remove(address);
        _foundationHeaders.Remove(address);
        _foundationParks.Remove(address);
        _foundationParkExpiries.Remove(address);
    }

    internal static bool IsOutsideMessageId(string id) => FoundationMetadataLimits.IsOutsideMessageId(id);


    private static string FoundationSendReceipt(string reference, string id) =>
        reference + "/" + Convert.ToHexString(SHA256.HashData(DurableKeyJson.CodeUnits(id)));

    /// <summary>Resolve an outside exact send and insert its delivery and deduplication marker in one operation.</summary>
    /// <returns>Null for an unavailable reference, false for a duplicate, true for a new delivery.</returns>
    internal bool? AddFoundationMessage(string tenant, string? reference, string id, string kind, string? payload = null,
        long? dueAt = null, TimeSpan? keepFor = null, bool keyed = false, string? replyTo = null, FoundationOperatorCommand? @operator = null)
    {
        Kinds.RequireApplicationSend(kind);
        if (!keyed && FoundationMetadataLimits.ReservedMessageId(id))
            throw new ArgumentException("The foundation reserves the issued ID namespace.", nameof(id));
        if ((!keyed && !FoundationMetadataLimits.FitsName(id)) || !FoundationMetadataLimits.FitsKind(kind) || !FoundationMetadataLimits.FitsContent(payload))
            return null;
        var window = FoundationIngressWindow(keepFor);
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

            // The receipt expires independently of delivery. A delayed input can still be pending when the id is reused.
            var operatorKey = @operator is not null ? FoundationIngressAddress.Operation(tenant, id) : null;
            var receipt = operatorKey is null ? FoundationSendReceipt(binding.Reference, id) : "operator/" + operatorKey;
            var now = clock();
            if (operatorKey is null && _foundationSendReceipts.TryGetValue(receipt, out var previousUntil) && previousUntil >= now)
                return false;

            var occurrence = checked(_seq + 1);
            var until = checked(now + (long)window.TotalMilliseconds);
            var message = FoundationIngressAddress.Occurrence(id, receipt, occurrence.ToString(System.Globalization.CultureInfo.InvariantCulture),
                new InstanceRef(address.Id), address.Creation, kind, payload, dueAt, until, replyTo, @operator);
            if (!CreationIdentityAvailable) CreationProtocol.RejectUnsupported(message);
            Messages.Add(message.InboxKey, StoreMessage(message, occurrence, 0));
            _seq = occurrence;
            if (operatorKey is null) _foundationSendReceipts[receipt] = until;
            else _foundationOperatorReceipts[operatorKey] = (reference, until);
            return true;
        }
    }
}
