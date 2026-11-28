using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.EntityFrameworkCore.Update;
using Microsoft.EntityFrameworkCore.Update.Internal;

namespace Kernel.EntityFramework;

// EF uses this comparer to break ties between commands whose dependencies have been met. A promotion
// can send an older timer (#9.0001) before a new one (#10.0000); ordering those inserts by ID reverses their seqs.
#pragma warning disable EF1001 // Reuse EF Core 10.0.15's ordering for other tables, states and keys.
internal sealed class SendOrderComparer : ModificationCommandComparer
{
    public override int Compare(IReadOnlyModificationCommand? x, IReadOnlyModificationCommand? y)
    {
        if (x is { EntityState: EntityState.Added, Entries: [InternalEntityEntry { Entity: MessageEntity first }] }
            && y is { EntityState: EntityState.Added, Entries: [InternalEntityEntry { Entity: MessageEntity second }] })
        {
            var order = first.SendOrdinal.CompareTo(second.SendOrdinal);
            if (order != 0) return order;
        }

        return base.Compare(x, y);
    }
}
#pragma warning restore EF1001
