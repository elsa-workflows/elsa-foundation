using System.Buffers.Binary;
using System.Text.Json;

namespace Kernel.Durable;

public sealed partial class InMemoryStore
{
    private const int SelectionBytes = 1 + sizeof(long) * 2;

    internal string SealFoundationSelection(string tenant, ActorCheckpoint actor)
    {
        Span<byte> mark = stackalloc byte[SelectionBytes];
        EncodeSelectionMark(actor, mark);
        return SealReadCursor(SelectionContext(tenant, actor.Reference), mark);
    }

    internal bool MatchesFoundationSelection(string tenant, string? reference, string? token)
    {
        lock (_gate)
        {
            if (BoundActor(tenant, reference) is not { } actor) return false;
            Span<byte> presented = stackalloc byte[SelectionBytes];
            if (!TryOpenReadCursor(SelectionContext(tenant, actor.Binding.Reference), token, presented)) return false;
            Span<byte> committed = stackalloc byte[SelectionBytes];
            EncodeSelectionMark(Checkpoint(actor.Row, actor.Binding), committed);
            return presented.SequenceEqual(committed);
        }
    }

    private bool NamesFoundationPark(string tenant, string reference, string token)
    {
        Span<byte> mark = stackalloc byte[SelectionBytes];
        return TryOpenReadCursor(SelectionContext(tenant, reference), token, mark) && mark[0] == 1;
    }

    private static byte[] SelectionContext(string tenant, string reference) =>
        JsonSerializer.SerializeToUtf8Bytes(new { Read = "operator-selection", Tenant = tenant, Actor = reference });

    private static void EncodeSelectionMark(ActorCheckpoint actor, Span<byte> mark)
    {
        if (actor.Park is { } park)
        {
            mark[0] = 1;
            BinaryPrimitives.WriteInt64LittleEndian(mark[1..], park.Revision);
            BinaryPrimitives.WriteInt64LittleEndian(mark[(1 + sizeof(long))..], park.At);
            return;
        }

        mark[0] = actor.Row.Status == InstanceStatus.Running ? (byte)0 : (byte)2;
        BinaryPrimitives.WriteInt64LittleEndian(mark[1..], 0);
        BinaryPrimitives.WriteInt64LittleEndian(mark[(1 + sizeof(long))..], actor.EndedAt ?? 0);
    }
}
