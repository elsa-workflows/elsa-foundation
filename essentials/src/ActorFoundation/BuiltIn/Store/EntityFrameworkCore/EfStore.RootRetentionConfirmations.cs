using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Kernel.Durable;
using Kernel.Foundation.Contracts;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private sealed class RetentionApproval(string tenant, string type, byte[] issuerKey)
    {
        private const int Size = 6 * sizeof(long);
        private readonly FoundationReadCursor cursor = new(HMACSHA256.HashData(issuerKey, "elsa/retention-confirmation/1"u8));
        private readonly byte[] context = Encoding.UTF8.GetBytes(DurableKeyJson.Write(new { Tenant = tenant, Type = type }));

        internal string Seal(long revision, ActorRetentionWindows proposed, ActorRetentionQuote quote)
        {
            Span<long> values = [
                revision, quote.AsOf, quote.NewlyEligible, proposed.Finished?.Ticks ?? -1,
                proposed.Cancelled?.Ticks ?? -1, proposed.Faulted?.Ticks ?? -1
            ];
            Span<byte> bytes = stackalloc byte[Size];
            for (var index = 0; index < values.Length; index++)
                BinaryPrimitives.WriteInt64LittleEndian(bytes[(index * sizeof(long))..], values[index]);
            return cursor.Seal(context, bytes);
        }

        internal long ReviewedCount(string? receipt, long revision, ActorRetentionWindows proposed)
        {
            Span<byte> bytes = stackalloc byte[Size];
            if (!cursor.TryOpen(context, receipt, bytes)) return 0;
            Span<long> values = stackalloc long[6];
            for (var index = 0; index < values.Length; index++)
                values[index] = BinaryPrimitives.ReadInt64LittleEndian(bytes[(index * sizeof(long))..]);

            return values[0] == revision && values[1] > 0 && values[2] >= 0 &&
                values[3] == (proposed.Finished?.Ticks ?? -1) && values[4] == (proposed.Cancelled?.Ticks ?? -1) &&
                values[5] == (proposed.Faulted?.Ticks ?? -1) ? values[2] : 0;
        }
    }
}
