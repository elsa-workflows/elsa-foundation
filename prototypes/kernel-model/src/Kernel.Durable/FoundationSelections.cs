using System.Buffers.Binary;
using System.Text.Json;

namespace Kernel.Durable;

public sealed partial class InMemoryStore
{
    private const int SelectionBytes = 1 + sizeof(long) * 2;

    internal string? SealFoundationSelection(string tenant, ActorCheckpoint actor)
    {
        Span<byte> mark = stackalloc byte[SelectionBytes];
        return TryEncodeSelectionMark(actor, mark)
            ? SealReadCursor(SelectionContext(tenant, actor.Reference), mark)
            : null;
    }

    internal bool MatchesFoundationSelection(string tenant, string? reference, string? token, bool requireTerminal = false)
    {
        lock (_gate)
        {
            if (BoundActor(tenant, reference) is not { } actor) return false;
            Span<byte> presented = stackalloc byte[SelectionBytes];
            if (!TryOpenReadCursor(SelectionContext(tenant, actor.Binding.Reference), token, presented)) return false;
            Span<byte> committed = stackalloc byte[SelectionBytes];
            var checkpoint = Checkpoint(actor.Row, actor.Binding);
            return (!requireTerminal || checkpoint.Terminal is not null) &&
                TryEncodeSelectionMark(checkpoint, committed) && presented.SequenceEqual(committed);
        }
    }

    internal bool AuthorizesEndedRecoverThenDelete(InstanceRow row, Message input)
    {
        lock (_gate)
        {
            if (row is not { Status: not InstanceStatus.Running, ParkRecovery: not null } ||
                !MatchesThenDeleteTarget(row, input)) return false;
            return input.Operator!.Selection is not { } selection || MatchesRecoveryPark(row, selection);
        }
    }

    private bool MatchesRecoveryPark(InstanceRow row, string selection)
    {
        var identity = FoundationIdentity(row.Id, row.Creation);
        if (_foundationParks.GetValueOrDefault(new(row.Id, row.Creation)) is not { } park) return false;
        Span<byte> mark = stackalloc byte[SelectionBytes];
        return TryOpenReadCursor(SelectionContext(identity.Tenant, identity.Reference), selection, mark) && mark[0] == 1 &&
            BinaryPrimitives.ReadInt64LittleEndian(mark[1..]) == park.Revision &&
            BinaryPrimitives.ReadInt64LittleEndian(mark[(1 + sizeof(long))..]) == park.At;
    }

    private bool NamesFoundationPark(string tenant, string reference, string token)
    {
        Span<byte> mark = stackalloc byte[SelectionBytes];
        return TryOpenReadCursor(SelectionContext(tenant, reference), token, mark) && mark[0] == 1;
    }

    private static byte[] SelectionContext(string tenant, string reference) =>
        JsonSerializer.SerializeToUtf8Bytes(new { Read = "operator-selection", Tenant = tenant, Actor = reference });

    private static bool TryEncodeSelectionMark(ActorCheckpoint actor, Span<byte> mark)
    {
        if (actor.Park is { } park && (actor.Row.Status == InstanceStatus.Running || actor.Row.ParkRecovery is null))
        {
            mark[0] = 1;
            BinaryPrimitives.WriteInt64LittleEndian(mark[1..], park.Revision);
            BinaryPrimitives.WriteInt64LittleEndian(mark[(1 + sizeof(long))..], park.At);
            return true;
        }

        if (actor.Row.Status == InstanceStatus.Running)
        {
            mark.Clear();
            return true;
        }
        if (actor.Terminal is not { Revision: > 0 } terminal) return false;

        mark[0] = 2;
        BinaryPrimitives.WriteInt64LittleEndian(mark[1..], terminal.Revision);
        BinaryPrimitives.WriteInt64LittleEndian(mark[(1 + sizeof(long))..], terminal.At);
        return true;
    }
}
