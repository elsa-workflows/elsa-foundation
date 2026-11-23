using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

internal sealed class StoreVersionEntity
{
    internal const int MarkerId = 1;
    internal const int StoreSchemaVersion = 1;
    internal const int MessageProtocolVersion = 1;
    internal const int JournalCodecVersion = 1;

    public int Id { get; set; }
    public int StoreSchema { get; set; }
    public int MessageProtocol { get; set; }
    public int JournalCodec { get; set; }
}

public sealed partial class EfStore
{
    private static async Task RequireStoreVersionsAsync(KernelDb db, CancellationToken cancellationToken)
    {
        var stored = await db.StoreVersions.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == StoreVersionEntity.MarkerId, cancellationToken);
        if (stored is null)
            throw new StoreException(Failure.Definite, "The kernel store version marker is missing.");

        if (stored.StoreSchema != StoreVersionEntity.StoreSchemaVersion ||
            stored.MessageProtocol != StoreVersionEntity.MessageProtocolVersion ||
            stored.JournalCodec != StoreVersionEntity.JournalCodecVersion)
            throw new StoreException(Failure.Definite,
                $"Unsupported kernel store versions. Stored schema={stored.StoreSchema}, message protocol={stored.MessageProtocol}, journal codec={stored.JournalCodec}. " +
                $"This build reads schema={StoreVersionEntity.StoreSchemaVersion}, message protocol={StoreVersionEntity.MessageProtocolVersion}, journal codec={StoreVersionEntity.JournalCodecVersion}.");
    }
}
