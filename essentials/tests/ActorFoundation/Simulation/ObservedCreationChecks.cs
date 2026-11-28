using System.Runtime.CompilerServices;

namespace Kernel.Sim;

public sealed partial class World
{
    private ConditionalWeakTable<Reply, Request>? _creationRefusals;
    private Dictionary<CreationAddress, BirthWitness>? _births;
    private Dictionary<CreationAddress, RetirementWitness>? _retirements;
    private Dictionary<string, InstanceRow?>? _physicalCheckpoints;
    private List<JournalRow>? _deletionReceipts;
    private ConditionalWeakTable<Message, Request>? _httpCancelInputs;
    private Dictionary<string, InstanceRow>? _cancelledHttp;
    private Dictionary<string, InstanceRow>? _settledHttpDeletes;
    private sealed record ActiveSample(InstanceRow Row);
    private Dictionary<string, ActiveSample>? _sampledActive;
    private sealed record HttpEndObligation(InstanceRow Row, Request? Starter, string Mode, bool Cancel);
    private Dictionary<InstanceRow, HttpEndObligation>? _retiredHttpEnds;
    private Dictionary<string, Request>? _httpStarters;
    private sealed record IssuedHttpEnd(InstanceRow Row, Request Starter);
    private ConditionalWeakTable<Reply, IssuedHttpEnd>? _issuedHttpEnds;
    private ConditionalWeakTable<Request, InstanceRow>? _acceptedHttpEnds;
    private string? _generationError;
    private sealed record BirthWitness(InstanceRow? PriorMarker, InstanceRow Latest);
    private sealed record RetirementWitness(InstanceRow? Predecessor, InstanceRow Marker, JournalRow Receipt);

    private void ObserveCreationRefusal(string node, Reply reply, Message refused)
    {
        if (reply is not { Channel: Request.Response, Item: CreationResult result }) return;
        var caller = Asked.FindLast(c => c.Process == node && c.Request.Registration == reply.Request);
        if (caller.Request is null || result.Actor != caller.Instance) return;
        var issued = _callerCheckpoints?.GetValueOrDefault(caller.Request)?.Input;
        if (ReferenceEquals(issued, refused) && result.Requested == (refused.TargetCreation ?? refused.Start?.Creation?.Generation))
        {
            (_creationRefusals ??= new()).Add(reply, caller.Request);
        }
    }

    internal void ReadLifecycleChanges()
    {
        foreach (var change in Store.Lifecycle!.Drain())
        {
            if (change.Refused is not null)
                foreach (var refused in change.Refused) Oracle.RefusedHttp(refused);
            if (change.Turn is not { } turn || turn.Row is null && !turn.Delete) continue;
            var before = change.Before;
            var input = change.Input;
            if (before is null && input?.Start is not null && _sampledActive?.ContainsKey(turn.Instance) == true &&
                !(_physicalCheckpoints?.TryGetValue(turn.Instance, out var absent) == true && absent is null))
                _generationError ??= $"I10 {turn.Instance} replaced a different checkpoint at its fenced input";
            if (turn.Row is { Model: "http", Status: InstanceStatus.Cancelled, Retired: false } cancelled && change.AppliedCancel &&
                (before is null || before is { Retired: false, Status: InstanceStatus.Running } active && SameHeader(active, cancelled)))
                (_cancelledHttp ??= [])[turn.Instance] = cancelled;
            else if (!turn.Delete && turn.Row is { Retired: false } next && _cancelledHttp?.GetValueOrDefault(turn.Instance) is { } last &&
                (next.Status != InstanceStatus.Cancelled || !SameHeader(last, next) || next.Revision <= last.Revision))
                _cancelledHttp.Remove(turn.Instance);

            if (_physicalCheckpoints?.TryGetValue(turn.Instance, out var expectedPrior) == true &&
                (before is null ? expectedPrior is not null : ConflictsWithFencedHeader(before)))
                _generationError ??= $"I10 {turn.Instance} replaced a different checkpoint at its fenced input";

            if (before is not null && _creations.TryGetValue(before.Id, out var seen) && seen.Generation == before.Creation &&
                _known.TryGetValue(before.Id, out var sampleBefore) && (ContradictsSample(before, sampleBefore) ||
                    !before.Retired && ConflictsWithSampledHeader(before)))
                _generationError ??= $"I10 {before.Id} replaced a different checkpoint at its fenced input";

            if (input is not null && input.Target.Id == turn.Instance && turn.Expected == before?.Revision)
            {
                if (turn.Delete && input.Kind == Kinds.Delete && input.TargetCreation is null &&
                    before is { Retired: false, Creation: 0, Status: not InstanceStatus.Running } && turn.Row is null &&
                    !CreationProtocol.IsPhysicalAddress(turn.Instance) && turn.Journal == """["deleted"]""" &&
                    change.Receipt is { } removed)
                {
                    if (_known.TryGetValue(turn.Instance, out var sample) && _creations.GetValueOrDefault(turn.Instance).Generation == 0 &&
                        (sample.Revision > before.Revision || sample.Status != InstanceStatus.Running && sample.Status != before.Status))
                        _generationError ??= $"I10 {turn.Instance} retired below its observed named checkpoint";
                    if (Store.Journal.GetValueOrDefault((removed.Partition, removed.Position)) != removed)
                        _generationError ??= $"I22 {before.Id} lost the checkpoint or receipt of its observed named deletion";
                    RememberNamedDelete(before, removed);
                    continue;
                }
                if (before is null && turn.Row is { Retired: false, Revision: > 0 } initial && input.Start is { } creator &&
                    initial.Model == creator.Model && (creator.Creation is null ? initial.Creation == 0 :
                        initial is { Creation: 1, Revision: 1 } && creator.Creation.Generation == 1 && initial.StartIntent == CreationProtocol.Fingerprint(creator, input.Kind, input.Payload)))
                {
                    var followsDelete = false;
                    if (_physicalCheckpoints?.TryGetValue(turn.Instance, out var expected) == true)
                    {
                        if (expected is not null) _generationError ??= $"I10 {turn.Instance} lost its retirement marker before a fenced birth";
                        else
                        {
                            followsDelete = true;
                            ResetNamedSample(turn.Instance);
                        }
                    }
                    RememberHttpStarter(initial, input);
                    if (initial.Creation > 0 || initial.Model == "http" || followsDelete)
                    {
                        (_births ??= [])[new(turn.Instance, initial.Creation)] = new(null, initial);
                        (_physicalCheckpoints ??= [])[turn.Instance] = initial;
                    }
                    continue;
                }
            }
            if (change.After is not { } row || turn.Row is null) continue;
            var address = new CreationAddress(row.Id, row.Creation);
            var born = _births?.GetValueOrDefault(address);
            if (before is not null && turn.Expected == before.Revision && input is not null && input.Target.Id == row.Id)
            {
                if (turn.Delete && input.Kind == Kinds.Delete && (before.Creation > 0 ? input.TargetCreation == before.Creation :
                    before.Creation == 0 && input.TargetCreation is null && CreationProtocol.IsPhysicalAddress(before.Id)) &&
                    before is { Retired: false, Status: not InstanceStatus.Running } && row == CreationProtocol.Retire(before) &&
                    turn.Journal == """["deleted"]""" && change.Receipt is { } receipt)
                {
                    if (born is not null && (born.Latest.Revision != before.Revision || !SameHeader(born.Latest, before)) || _retirements?.ContainsKey(address) == true)
                        _generationError ??= $"I10 {row.Id} retired a different occurrence of creation {row.Creation}";
                    RetireHttp(before);
                    (_retirements ??= [])[address] = new(before, row, receipt);
                    (_physicalCheckpoints ??= [])[row.Id] = row;
                    (_deletionReceipts ??= []).Add(receipt);
                    continue;
                }
                if (before.Retired && before.Creation > 0 && before.Creation < long.MaxValue && row.Creation == before.Creation + 1 &&
                    row is { Revision: 1, Retired: false } && input.Start?.Creation?.Generation == row.Creation &&
                    row.StartIntent == CreationProtocol.Fingerprint(input.Start, input.Kind, input.Payload))
                {
                    if (_sampledActive?.GetValueOrDefault(row.Id) is { } sampled && sampled.Row.Creation == before.Creation &&
                        _retirements?.ContainsKey(new(row.Id, before.Creation)) != true)
                        _generationError ??= $"I10 {row.Id} replaced a different checkpoint at its fenced input";
                    if (!CanonicalMarker(before) || !ValidMarkerRevision(before)) _generationError ??= $"I10 {row.Id} replaced a different checkpoint at its fenced input";
                    if (born is not null || _retirements?.GetValueOrDefault(new(row.Id, before.Creation)) is { } retired && retired.Marker != before)
                        _generationError ??= $"I10 {row.Id} replaced a different retirement before creation {row.Creation}";
                    (_physicalCheckpoints ??= [])[row.Id] = row;
                    (_births ??= [])[address] = new(before, row);
                    RememberHttpStarter(row, input);
                    continue;
                }
            }
            if (born is null && before is { Retired: false } && !row.Retired && before.Creation == row.Creation &&
                turn.Expected == before.Revision && _sampledActive?.ContainsKey(row.Id) == true)
            {
                if (!SameHeader(before, row) || ChangedTerminal(before, row))
                    _generationError ??= $"I10 {row.Id} changed the fenced header of creation {row.Creation}";
                else (_physicalCheckpoints ??= [])[row.Id] = row;
            }
            if (born is not null && !row.Retired && turn.Expected == born.Latest.Revision)
            {
                if (!SameHeader(born.Latest, row) || ChangedTerminal(born.Latest, row))
                    _generationError ??= $"I10 {row.Id} changed the fenced header of creation {row.Creation}";
                else
                {
                    _births![address] = born with { Latest = row };
                    _physicalCheckpoints![row.Id] = row;
                }
            }
        }
    }

    private static bool CanonicalMarker(InstanceRow row) =>
        (row.Creation > 0 || row.Creation == 0 && CreationProtocol.IsPhysicalAddress(row.Id)) && row.Status != InstanceStatus.Running &&
        row == new InstanceRow(row.Id, "", "", row.Status, "null", row.Revision, null, History: History.Minimal, Creation: row.Creation, Retired: true);

    // Qualified creation starts at revision one. A witnessed retirement can also identify an
    // imported signed-revision predecessor exactly, even when its resulting marker is zero or one.
    private bool ValidMarkerRevision(InstanceRow marker) => marker.Revision >= 2 ||
        _retirements?.GetValueOrDefault(new(marker.Id, marker.Creation)) is { Predecessor: { } prior } retirement &&
        retirement.Marker == marker && marker == CreationProtocol.Retire(prior);

    private bool ConflictsWithSampledHeader(InstanceRow row) =>
        _sampledActive?.GetValueOrDefault(row.Id) is { Row: var previous } && previous.Creation == row.Creation &&
        !SameHeader(previous, row);

    private static bool ContradictsSample(InstanceRow row, (long Revision, InstanceStatus Status) known) =>
        row.Revision < known.Revision || row.Status != known.Status && (row.Revision == known.Revision || known.Status != InstanceStatus.Running);

    private void RememberHttpStarter(InstanceRow row, Message input)
    {
        _httpStarters?.Remove(row.Id);
        if (row.Model != "http") return;
        _settledHttpDeletes?.Remove(row.Id);
        if (input.ReplyTo is null || _callerCheckpoints is null) return;
        foreach (var (request, checkpoint) in _callerCheckpoints)
            if (ReferenceEquals(checkpoint.Input, input))
            {
                (_httpStarters ??= [])[row.Id] = request;
                return;
            }
    }

    // Capture the actual issued carrier while the terminal row still exists; an inferred or copied end cannot close the obligation.
    private void ObserveHostEnd(string node, Reply reply)
    {
        if (_httpStarters is null || reply is not { Channel: null, Status: InstanceStatus.Cancelled, Error: null }) return;
        var (_, caller, id) = Asked.FindLast(c => c.Process == node && c.Request.Registration == reply.Request);
        if (caller is null || caller.Done || _httpStarters.GetValueOrDefault(id) != caller ||
            Store.Instances.GetValueOrDefault(id) is not { Model: "http", Status: InstanceStatus.Cancelled, Retired: false } row ||
            reply.Key != CreationProtocol.Origin(id, row.Creation) + "#end") return;
        (_issuedHttpEnds ??= new()).Add(reply, new(row, caller));
    }

    private void DeliverTo(Requests process, Reply reply)
    {
        if (process.TryReceive(reply) && _issuedHttpEnds?.TryGetValue(reply, out var issued) == true)
            (_acceptedHttpEnds ??= new()).AddOrUpdate(issued.Starter, issued.Row);
    }

    private bool AcceptedHostEnd(InstanceRow row, Request? starter) =>
        starter is not null && _acceptedHttpEnds?.TryGetValue(starter, out var actual) == true &&
        actual.Id == row.Id && actual.Revision == row.Revision && SameHeader(actual, row) && actual.Status == row.Status;

    private void RetireHttp(InstanceRow predecessor)
    {
        if (predecessor.Model != "http") return;
        (_settledHttpDeletes ??= [])[predecessor.Id] = predecessor;
        if (predecessor.Status != InstanceStatus.Cancelled || _cancelledHttp?.GetValueOrDefault(predecessor.Id) is { } applied &&
            SameHeader(applied, predecessor) && applied.Revision == predecessor.Revision || !Oracle.Https.TryGetValue(predecessor.Id, out var request)) return;
        var starter = _httpStarters?.GetValueOrDefault(predecessor.Id);
        if (!AcceptedHostEnd(predecessor, starter))
            (_retiredHttpEnds ??= new(ReferenceEqualityComparer.Instance)).TryAdd(predecessor, new(predecessor, starter, request.Mode, request.Cancel));
    }

    private bool SettledHttpDelete(string id, InstanceRow? current)
    {
        if (_settledHttpDeletes?.GetValueOrDefault(id) is not { } retired || _physicalCheckpoints is null ||
            !_physicalCheckpoints.TryGetValue(id, out var expected)) return false;
        if (current is null) return expected is null && retired.Creation == 0;
        if (expected is null || expected.Creation != current.Creation || ConflictsWithFencedHeader(current)) return false;
        return current.Retired || current.Model != "http" && _births?.ContainsKey(new(id, current.Creation)) == true;
    }

    private bool MissingRetirement(InstanceRow marker)
    {
        // No selected Delete is known for an active sample from this occurrence. An older
        // position for the same address cannot make a later, physically invented marker real.
        return _sampledActive?.GetValueOrDefault(marker.Id) is { } sample && sample.Row.Creation == marker.Creation &&
            _retirements?.ContainsKey(new(marker.Id, marker.Creation)) != true;
    }

    private void RememberMarker(InstanceRow marker, JournalRow receipt)
    {
        var address = new CreationAddress(marker.Id, marker.Creation);
        if (_retirements?.ContainsKey(address) == true) return;
        Store.Lifecycle!.Watch(marker.Id);
        (_retirements ??= [])[address] = new(null, marker, receipt);
        (_physicalCheckpoints ??= [])[marker.Id] = marker;
        (_deletionReceipts ??= []).Add(receipt);
    }

    private void RememberActiveSample(InstanceRow row)
    {
        Store.Lifecycle!.Watch(row.Id);
        (_sampledActive ??= [])[row.Id] = new(row);
    }

    private void ResetNamedSample(string id)
    {
        _sampledActive?.Remove(id);
        _known.Remove(id); _creations.Remove(id); _versions.Remove(id);
        _gone.Remove(id); _late.Remove(id);
    }

    private void RememberNamedDelete(InstanceRow before, JournalRow receipt)
    {
        (_physicalCheckpoints ??= [])[before.Id] = null;
        (_deletionReceipts ??= []).Add(receipt);
        _births?.Remove(new(before.Id, 0));
        RetireHttp(before);
        _cancelledHttp?.Remove(before.Id);
    }

    // The header facts the store refuses on every write and that need no creator or earlier commit prefix: the kernel's
    // own rule (CreationProtocol.HeaderError), not a second list that could drift from it. A cold row is judged as the
    // store would judge a birth: every row the store has born satisfied these facts then and kept them since.
    private static bool InvalidCreationMetadata(InstanceRow row) => CreationProtocol.HeaderError(row, born: true) is not null;

    private static bool ChangedTerminal(InstanceRow known, InstanceRow row) =>
        known.Status != InstanceStatus.Running && known.Status != row.Status;

    private static bool SameHeader(InstanceRow written, InstanceRow row) =>
        written.Creation == row.Creation && written.StartIntent == row.StartIntent && written.Model == row.Model &&
        written.Parent == row.Parent && written.ParentCreation == row.ParentCreation;

    private bool ConflictsWithFencedHeader(InstanceRow row)
    {
        if (_physicalCheckpoints?.TryGetValue(row.Id, out var expected) != true) return false;
        return expected is null || (expected.Retired ? expected != row :
            row.Retired || expected.Revision != row.Revision || expected.Status != row.Status || !SameHeader(expected, row));
    }

    private string FencedRowError(InstanceRow row)
    {
        if (_gone.Contains(row.Id) && _physicalCheckpoints?.TryGetValue(row.Id, out var absent) == true && absent is null)
            return $"I22 {row.Id} was deleted and has a row again";
        if (_physicalCheckpoints?.GetValueOrDefault(row.Id) is { Retired: false } known && known.Creation == row.Creation)
        {
            if (known.Revision != row.Revision) return $"I10 {row.Id} is at revision {row.Revision} after fenced revision {known.Revision}";
            if (known.Status != row.Status) return $"I10 {row.Id} went from revision {known.Revision} {known.Status} to revision {row.Revision} {row.Status}";
        }
        return $"I10 {row.Id} changed the fenced header of creation {row.Creation}";
    }

    private bool ValidSuccessor(InstanceRow row, long generation, bool markerSampled, Func<RetirementWitness, bool> authorized)
    {
        if (generation <= 0 || row.Creation <= generation || ConflictsWithFencedHeader(row)) return false;
        for (var previous = generation; previous < row.Creation; previous++)
        {
            var retired = _retirements?.GetValueOrDefault(new(row.Id, previous));
            var birth = _births?.GetValueOrDefault(new(row.Id, previous + 1));
            if (retired is null || birth is null || birth.PriorMarker != retired.Marker || !Retained(retired) || !authorized(retired)) return false;
            if (previous == generation && _known.TryGetValue(row.Id, out var sample) &&
                ((markerSampled ? retired.Marker : retired.Predecessor) is not { } prior || prior.Revision < sample.Revision)) return false;
        }
        if (row.Retired)
        {
            var endpoint = _retirements?.GetValueOrDefault(new(row.Id, row.Creation));
            return endpoint is not null && endpoint.Marker == row && Retained(endpoint) && authorized(endpoint);
        }
        return _births![new(row.Id, row.Creation)].Latest.Revision == row.Revision;
    }

    private string? RetainedDeletionError()
    {
        if (_physicalCheckpoints is not null)
            foreach (var (id, known) in _physicalCheckpoints)
                if (known is not null && !Store.Instances.ContainsKey(id))
                    return $"I10 the fenced row of {id} disappeared";
        if (_deletionReceipts is not null)
            foreach (var entry in _deletionReceipts)
                if (!Store.Journal.TryGetValue((entry.Partition, entry.Position), out var current) || entry != current)
                    return $"I22 {entry.Instance} changed its fenced deletion receipt at {entry.Partition}/{entry.Position}";
        return null;
    }

    private bool Retained(RetirementWitness retired) =>
        Store.Journal.TryGetValue((retired.Receipt.Partition, retired.Receipt.Position), out var actual) &&
        actual == retired.Receipt && actual.Instance == retired.Marker.Id && actual.Revision == -1 && actual.Entries == """["deleted"]""";
}
