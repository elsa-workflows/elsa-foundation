using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Workflows.Publishing.Releases.Core;

namespace Elsa.Workflows.Publishing.Releases.Tests;

/// <summary>Synthetic transport documents, deliberately not owner-verified workflow/ETL artifacts.</summary>
internal sealed class ReleaseBundleFixture
{
    internal JsonObject Manifest { get; }
    internal Dictionary<string, ReadOnlyMemory<byte>> Blobs { get; } = new(StringComparer.Ordinal);
    internal byte[] Bytes => Encoding.UTF8.GetBytes(Manifest.ToJsonString());
    internal static JsonObject Key(int index) => new() { ["kind"] = index == 0 ? "workflow-executable" : "etl-artifact", ["identity"] = "member-" + index };
    internal ReleaseBundleFixture(int count = 2)
    {
        var members = new JsonArray();
        var edges = new JsonArray();
        for (var i = 0; i < count; i++)
        {
            var payload = Encoding.UTF8.GetBytes("{\"number\":" + i + ",\"opaque\":{\"z\":1,\"a\":2}}");
            var digest = ReleaseManifest.Hash(payload);
            Blobs.Add(digest, payload);
            members.Add(new JsonObject
            {
                ["key"] = Key(i), ["codec"] = "fixture-json", ["codecVersion"] = 1,
                ["ownerVersion"] = i == 0 ? new JsonObject { ["scheme"] = "semver", ["value"] = "1.0.0" }
                    : new JsonObject { ["scheme"] = "integer", ["value"] = 17 },
                ["payloadDigest"] = digest, ["byteLength"] = payload.Length
            });
            if (i != 0) edges.Add(Edge(i - 1, i));
        }
        Manifest = new JsonObject
        {
            ["format"] = "elsa.workflow-release", ["formatVersion"] = 1, ["profile"] = ReleaseManifest.Profile,
            ["roots"] = new JsonArray(new JsonObject { ["member"] = Key(0), ["content"] = Key(0), ["action"] = "activate-serving" }),
            ["members"] = members, ["edges"] = edges, ["prerequisites"] = new JsonArray(), ["resourceRequirements"] = new JsonArray()
        };
    }
    internal static JsonObject Edge(int from, int to, string relation = "dependency") => new()
    { ["from"] = Key(from), ["to"] = Key(to), ["relation"] = relation, ["occurrence"] = "node-" + to };
    internal IReleaseBundleTransport Validate(ReleaseBundleLimits? limits = null) => new ReleaseBundleCodec().Validate(Bytes, Blobs, limits);
    internal byte[] Zip(params (string Path, byte[] Bytes, int Attributes)[] extra)
    {
        var entries = new List<(string, byte[], int)> { ("manifest.json", Bytes, 0) };
        entries.AddRange(Blobs.Select(x => ("blobs/" + x.Key[7..] + ".json", x.Value.ToArray(), 0)));
        entries.AddRange(extra);
        return Archive(entries);
    }
    internal static byte[] Archive(IEnumerable<(string Path, byte[] Bytes, int Attributes)> entries)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (path, bytes, attributes) in entries)
            {
                var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
                entry.ExternalAttributes = attributes;
                using var stream = entry.Open(); stream.Write(bytes);
            }
        return memory.ToArray();
    }
    internal void WriteDirectory(string directory)
    {
        Directory.CreateDirectory(Path.Combine(directory, "blobs"));
        File.WriteAllBytes(Path.Combine(directory, "manifest.json"), Bytes);
        foreach (var (digest, bytes) in Blobs) File.WriteAllBytes(Path.Combine(directory, "blobs", digest[7..] + ".json"), bytes.ToArray());
    }
}
