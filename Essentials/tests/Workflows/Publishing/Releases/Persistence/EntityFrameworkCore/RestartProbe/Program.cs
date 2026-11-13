using Elsa.Workflows.Publishing.Releases.Persistence.Core;
using Elsa.Workflows.Publishing.Releases.Persistence.EntityFrameworkCore.Tests;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

if (args.Length != 3 || args[0] is not ("interrupt" or "read")) return 2;
await using var services = ArchiveFixture.Services("Data Source=" + args[1] + ";Pooling=False;Default Timeout=2",
    interceptor: args[0] == "interrupt" ? new PauseAfterHeader(args[2]) : null);
await using var scope = services.CreateAsyncScope();
if (args[0] == "interrupt")
{
    await scope.ServiceProvider.GetRequiredService<IReleaseArchiveWriter>().CreateAsync("process-capture", new MemoryStream(new ArchiveFixture().Zip()));
    return 3; // The parent must kill the process before it can commit.
}
var snapshot = await scope.ServiceProvider.GetRequiredService<IReleaseArchiveReader>().FindAsync("process-capture");
if (snapshot is null) { Console.WriteLine("missing"); return 0; }
var fixture = new ArchiveFixture();
if (!snapshot.Transport.GetManifestBytes().AsSpan().SequenceEqual(fixture.ManifestBytes) ||
    !snapshot.Transport.GetBlobBytes(fixture.BlobDigest).AsSpan().SequenceEqual(fixture.Payload) ||
    !snapshot.Transport.GetAttachmentBytes("attachments/receipt.bin").AsSpan().SequenceEqual(fixture.Attachment)) return 4;
if (snapshot.ValidationLevel != ReleaseArchiveValidationLevel.TransportOnly ||
    !(await scope.ServiceProvider.GetRequiredService<IReleaseArchiveStoreCapabilities>().GetCapabilitiesAsync()).DurableCapture) return 5;
Console.WriteLine("reopened-original-bytes");
return 0;

internal sealed class PauseAfterHeader(string signal) : SaveChangesInterceptor
{
    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        await File.WriteAllTextAsync(signal, "header-written-without-commit", cancellationToken);
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return result;
    }
}
