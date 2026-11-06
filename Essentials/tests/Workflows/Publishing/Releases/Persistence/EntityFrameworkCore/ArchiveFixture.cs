using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Persistence.Core;
using Elsa.Workflows.Publishing.Releases;
using Elsa.Workflows.Publishing.Releases.Core;
using Elsa.Workflows.Publishing.Releases.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Publishing.Releases.Persistence.EntityFrameworkCore.Tests;

internal sealed class ArchiveFixture
{
    internal static SourceScope DefaultScope => new("application-partition", "tenant", "workflow-release-archive", "source-environment");
    internal byte[] Payload { get; }
    internal string BlobDigest { get; }
    internal JsonObject Manifest { get; }
    internal byte[] Attachment { get; } = [0, 255, 10, 128];
    internal byte[] ManifestBytes => Encoding.UTF8.GetBytes(Manifest.ToJsonString());

    internal ArchiveFixture(int value = 1)
    {
        Payload = Encoding.UTF8.GetBytes("{\"value\":" + value + ",\"opaque\":{\"z\":1,\"a\":2}}");
        BlobDigest = "sha256:" + Convert.ToHexString(SHA256.HashData(Payload)).ToLowerInvariant();
        static JsonObject Key() => new() { ["kind"] = "workflow-executable", ["identity"] = "fixture-artifact" };
        Manifest = new JsonObject
        {
            ["format"] = "elsa.workflow-release", ["formatVersion"] = 1, ["profile"] = "native-bpmn-etl/1",
            ["roots"] = new JsonArray(new JsonObject { ["member"] = Key(), ["content"] = Key(), ["action"] = "activate-serving" }),
            ["members"] = new JsonArray(new JsonObject
            {
                ["key"] = Key(), ["codec"] = "fixture-json", ["codecVersion"] = 1,
                ["ownerVersion"] = new JsonObject { ["scheme"] = "semver", ["value"] = "1.0.0" },
                ["payloadDigest"] = BlobDigest, ["byteLength"] = Payload.Length
            }),
            ["edges"] = new JsonArray(), ["prerequisites"] = new JsonArray(), ["resourceRequirements"] = new JsonArray()
        };
    }

    internal byte[] Zip(bool alternateFormat = false, byte[]? attachment = null, string attachmentPath = "attachments/receipt.bin", byte[]? blobOverride = null)
    {
        var manifest = alternateFormat
            ? Encoding.UTF8.GetBytes(new JsonObject(Manifest.Reverse().Select(x => KeyValuePair.Create(x.Key, x.Value?.DeepClone())))
                .ToJsonString(new JsonSerializerOptions { WriteIndented = true }))
            : ManifestBytes;
        var entries = new List<(string Path, byte[] Bytes)>
        {
            ("manifest.json", manifest), ("blobs/" + BlobDigest[7..] + ".json", blobOverride ?? Payload), (attachmentPath, attachment ?? Attachment)
        };
        if (alternateFormat) entries.Reverse();
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (path, bytes) in entries)
            {
                var entry = zip.CreateEntry(path, alternateFormat ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
                entry.ExternalAttributes = 0;
                using var stream = entry.Open();
                stream.Write(bytes);
            }
        return output.ToArray();
    }

    internal static ServiceProvider Services(string connectionString, SourceScope? sourceScope = null,
        MutableAccess? access = null, IInterceptor? interceptor = null, ReleaseBundleLimits? limits = null, bool requireDurable = true)
    {
        var selected = sourceScope ?? DefaultScope;
        var services = new ServiceCollection();
        new WorkflowReleaseTransportFeature().ConfigureServices(services);
        services.AddSingleton<IPersistenceAccessContextAccessor>(access ?? new MutableAccess(PersistenceAccessContext.Scoped(new PersistenceScope(selected.Partition))));
        services.AddDbContext<ConsumerContext>(options =>
        {
            options.UseSqlite(connectionString);
            if (interceptor is not null) options.AddInterceptors(interceptor);
        });
        services.AddReleaseArchiveStore<ConsumerContext>(_ => selected, limits, requireDurable);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}

public sealed class ConsumerContext(DbContextOptions<ConsumerContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddReleaseArchiveModel(Database.ProviderName!);
        modelBuilder.Entity<UnrelatedItem>().HasKey(x => x.Id);
    }
}

public sealed class UnrelatedItem { public string Id { get; set; } = ""; }

internal sealed class MutableAccess(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
{
    public PersistenceAccessContext Current { get; set; } = current;
}
