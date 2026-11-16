using System.Security.Cryptography;
using System.Text;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Runtime.Http;
using Elsa.Etl.Runtime.Output;

namespace Elsa.Etl.Tests;

public sealed class EtlHttpPageCacheTests : IDisposable
{
    private const long PageSequence = 0;
    private static readonly Guid FirstAttempt = Guid.Parse("65a0e0be-f89b-4c10-8cf8-4878bffbc10c");
    private static readonly Guid SecondAttempt = Guid.Parse("82b82449-0870-4d8b-b67a-1a96eaf88ef0");
    private static readonly DateTimeOffset ReceivedAt = new(2026, 10, 21, 12, 30, 0, TimeSpan.Zero);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "elsa-etl-http-page-cache-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Reopens_verified_raw_page_and_keeps_body_descriptor_distinct_from_manifest()
    {
        var admission = Admission("cache-reopen", new EtlScope("tenant-a", "partition-a"));
        var request = Request();
        var body = ValidBody();
        var first = await Cache(admission).PublishAsync(PageSequence, request, FirstAttempt, 3, ReceivedAt, body);

        var reopened = await Cache(admission).ReadAsync(PageSequence, request);

        Assert.NotNull(reopened);
        Assert.Equal(body.Length, first.Descriptor.Body.ByteLength);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant(), first.Descriptor.Body.ContentHash);
        Assert.Equal(body, await File.ReadAllBytesAsync(Path.Combine(_root, first.Descriptor.Body.Key)));
        Assert.Equal(1, first.Descriptor.RecordCount);
        Assert.Equal("cursor-1", first.Descriptor.NextContinuation);
        Assert.Equal(first.Descriptor, reopened.Descriptor);
        Assert.Equivalent(first.DecodedPage.Records, reopened.DecodedPage.Records, strict: true);
        Assert.Equal(first.DecodedPage.Continuation, reopened.DecodedPage.Continuation);
        Assert.Equal("65a0e0bef89b4c108cf84878bffbc10c", reopened.Provenance.AttemptId);
        Assert.Equal(3, reopened.Provenance.Fence);
        Assert.Equal(ReceivedAt, reopened.Provenance.ResponseReceivedAtUtc);
        Assert.NotEqual(first.Descriptor.Body.Key, ManifestPath(admission, request));
        Assert.Equal(body, reopened.RawBody.ToArray());
    }

    [Fact]
    public async Task Malformed_page_publishes_neither_body_nor_manifest()
    {
        var admission = Admission("cache-malformed", new EtlScope("tenant-a", "malformed"));
        var cache = Cache(admission);

        var error = await Assert.ThrowsAsync<EtlHttpPageCacheException>(() =>
            cache.PublishAsync(PageSequence, Request(), FirstAttempt, 1, ReceivedAt, "{not-json}"u8.ToArray()).AsTask());

        Assert.Equal("etl.http.page-invalid", error.Code);
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task Exact_page_byte_boundary_is_accepted_and_the_next_byte_is_refused_before_publication()
    {
        var body = ValidBody();
        var exactPipeline = EtlV2TestPipelines.Create() with
        {
            Http = EtlV2TestPipelines.Create().Http with { MaxPageBytes = body.Length }
        };
        var exactAdmission = Admission("cache-exact-boundary", new EtlScope("tenant-a", "exact"), exactPipeline);
        var accepted = await Cache(exactAdmission).PublishAsync(PageSequence, Request(), FirstAttempt, 1, ReceivedAt, body);
        Assert.Equal(body.Length, accepted.Descriptor.Body.ByteLength);

        var tooSmallPipeline = exactPipeline with
        {
            Http = exactPipeline.Http with { MaxPageBytes = body.Length - 1 }
        };
        var tooSmallAdmission = Admission("cache-over-boundary", new EtlScope("tenant-a", "over"), tooSmallPipeline);
        var tooSmallRoot = Path.Combine(_root, "too-small");
        var error = await Assert.ThrowsAsync<EtlHttpPageCacheException>(() =>
            Cache(tooSmallAdmission, tooSmallRoot).PublishAsync(PageSequence, Request(), FirstAttempt, 1, ReceivedAt, body).AsTask());

        Assert.Equal("etl.http.page-too-large", error.Code);
        Assert.False(Directory.Exists(tooSmallRoot));
    }

    [Fact]
    public async Task A_changed_body_for_an_existing_request_is_a_conflict_and_keeps_the_original()
    {
        var admission = Admission("cache-content-conflict", new EtlScope("tenant-a", "conflict"));
        var cache = Cache(admission);
        var originalBody = ValidBody();
        var original = await cache.PublishAsync(PageSequence, Request(), FirstAttempt, 1, ReceivedAt, originalBody);

        var error = await Assert.ThrowsAsync<EtlHttpPageCacheException>(() =>
            cache.PublishAsync(PageSequence, Request(), SecondAttempt, 2, ReceivedAt.AddMinutes(1), DifferentValidBody()).AsTask());

        Assert.Equal("etl.http.page-cache-conflict", error.Code);
        Assert.Equal(originalBody, await File.ReadAllBytesAsync(Path.Combine(_root, original.Descriptor.Body.Key)));
        Assert.Single(Directory.GetFiles(_root, "*.page"));
        Assert.Single(Directory.GetFiles(_root, "*.json"));
    }

    [Fact]
    public async Task Identical_body_from_a_later_attempt_converges_to_original_provenance()
    {
        var admission = Admission("cache-attempt-replay", new EtlScope("tenant-a", "replay"));
        var cache = Cache(admission);
        var body = ValidBody();
        var first = await cache.PublishAsync(PageSequence, Request(), FirstAttempt, 7, ReceivedAt, body);

        var replay = await cache.PublishAsync(PageSequence, Request(), SecondAttempt, 8, ReceivedAt.AddMinutes(1), body);

        Assert.Equal(first.Descriptor, replay.Descriptor);
        Assert.Equal(first.Provenance, replay.Provenance);
        Assert.Equal("65a0e0bef89b4c108cf84878bffbc10c", replay.Provenance.AttemptId);
        Assert.Single(Directory.GetFiles(_root, "*.page"));
        Assert.Single(Directory.GetFiles(_root, "*.json"));
    }

    [Fact]
    public async Task A_body_without_its_manifest_is_not_a_recoverable_page()
    {
        var admission = Admission("cache-body-only", new EtlScope("tenant-a", "body-only"));
        var body = ValidBody();
        var bodyHash = Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();
        var bodyIdentity = "etl-http-page-body-v1:" + bodyHash;
        var bodyStore = new FileChunkStore(".json");
        var chunk = await bodyStore.PublishAsync(_root, bodyIdentity, body.Length,
            (stream, ct) => stream.WriteAsync(body, ct));
        var cache = Cache(admission);

        Assert.Null(await cache.ReadAsync(PageSequence, Request()));
        var captured = await cache.PublishAsync(PageSequence, Request(), FirstAttempt, 1, ReceivedAt, body);

        Assert.Equal(chunk.Key, captured.Descriptor.Body.Key);
        Assert.Single(Directory.GetFiles(_root, "*.page"));
        Assert.Single(Directory.GetFiles(_root, "*.json"));
    }

    [Fact]
    public async Task Corrupted_raw_payload_is_refused_without_exposing_bytes_or_root()
    {
        var admission = Admission("cache-body-corruption", new EtlScope("tenant-a", "body-corrupt"));
        var page = await Cache(admission).PublishAsync(PageSequence, Request(), FirstAttempt, 1, ReceivedAt, ValidBody());
        var payloadPath = Path.Combine(_root, page.Descriptor.Body.Key);
        var originalBytes = await File.ReadAllBytesAsync(payloadPath);
        var corrupted = Encoding.UTF8.GetString(originalBytes);
        Assert.Contains("Ada", corrupted, StringComparison.Ordinal);
        var corruptedBytes = Encoding.UTF8.GetBytes(corrupted.Replace("Ada", "Eva", StringComparison.Ordinal));
        Assert.Equal(page.Descriptor.Body.ByteLength, corruptedBytes.LongLength);
        using var document = System.Text.Json.JsonDocument.Parse(corruptedBytes);
        Assert.Equal(System.Text.Json.JsonValueKind.Object, document.RootElement.ValueKind);
        await File.WriteAllBytesAsync(payloadPath, corruptedBytes);

        var error = await Assert.ThrowsAsync<EtlHttpPageCacheException>(() => Cache(admission).ReadAsync(PageSequence, Request()).AsTask());

        Assert.Equal("etl.http.page-cache-corrupt", error.Code);
        Assert.DoesNotContain(_root, error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Ada", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Manifest_identity_mismatch_is_corruption()
    {
        var admission = Admission("cache-header-corruption", new EtlScope("tenant-a", "header-corrupt"));
        await Cache(admission).PublishAsync(PageSequence, Request(), FirstAttempt, 1, ReceivedAt, ValidBody());
        var path = ManifestFilePath(admission, Request());
        var manifest = await File.ReadAllBytesAsync(path);
        manifest[8] = manifest[8] == (byte)'0' ? (byte)'1' : (byte)'0';
        await File.WriteAllBytesAsync(path, manifest);

        var error = await Assert.ThrowsAsync<EtlHttpPageCacheException>(() => Cache(admission).ReadAsync(PageSequence, Request()).AsTask());

        Assert.Equal("etl.http.page-cache-corrupt", error.Code);
        Assert.DoesNotContain(_root, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Manifest_provenance_mutation_with_valid_timestamp_is_corruption()
    {
        var admission = Admission("cache-provenance-corruption", new EtlScope("tenant-a", "provenance-corrupt"));
        await Cache(admission).PublishAsync(PageSequence, Request(), FirstAttempt, 1, ReceivedAt, ValidBody());
        var path = ManifestFilePath(admission, Request());
        var manifest = await File.ReadAllBytesAsync(path);
        const int receivedTicksOffset = 8 + 64 + 32 + sizeof(long);
        manifest[receivedTicksOffset] ^= 0x01;
        var mutatedTicks = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(manifest.AsSpan(receivedTicksOffset, sizeof(long)));
        var mutatedReceivedAt = new DateTimeOffset(mutatedTicks, TimeSpan.Zero);
        Assert.NotEqual(ReceivedAt, mutatedReceivedAt);
        await File.WriteAllBytesAsync(path, manifest);

        var error = await Assert.ThrowsAsync<EtlHttpPageCacheException>(() => Cache(admission).ReadAsync(PageSequence, Request()).AsTask());

        Assert.Equal("etl.http.page-cache-corrupt", error.Code);
        Assert.DoesNotContain(_root, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Oversized_payload_is_rejected_before_read_allocation()
    {
        var body = ValidBody();
        var pipeline = EtlV2TestPipelines.Create() with
        {
            Http = EtlV2TestPipelines.Create().Http with { MaxPageBytes = body.Length }
        };
        var admission = Admission("cache-oversized-stored-body", new EtlScope("tenant-a", "oversized"), pipeline);
        var page = await Cache(admission).PublishAsync(PageSequence, Request(), FirstAttempt, 1, ReceivedAt, body);
        await using (var file = new FileStream(Path.Combine(_root, page.Descriptor.Body.Key), FileMode.Append, FileAccess.Write))
            await file.WriteAsync(new byte[] { (byte)' ' });

        var error = await Assert.ThrowsAsync<EtlHttpPageCacheException>(() => Cache(admission).ReadAsync(PageSequence, Request()).AsTask());

        Assert.Equal("etl.http.page-cache-corrupt", error.Code);
    }

    [Fact]
    public async Task Missing_manifest_is_absent_but_missing_body_and_unavailable_root_are_failures()
    {
        var admission = Admission("cache-missing-data", new EtlScope("tenant-a", "missing"));
        var cache = Cache(admission);
        var page = await cache.PublishAsync(PageSequence, Request(), FirstAttempt, 1, ReceivedAt, ValidBody());
        File.Delete(ManifestFilePath(admission, Request()));

        Assert.Null(await cache.ReadAsync(PageSequence, Request()));
        var republished = await cache.PublishAsync(PageSequence, Request(), FirstAttempt, 1, ReceivedAt, ValidBody());
        File.Delete(Path.Combine(_root, republished.Descriptor.Body.Key));
        var missingBody = await Assert.ThrowsAsync<EtlHttpPageCacheException>(() => cache.ReadAsync(PageSequence, Request()).AsTask());
        Assert.Equal("etl.http.page-cache-corrupt", missingBody.Code);

        Directory.Delete(_root, recursive: true);
        var unavailable = await Assert.ThrowsAsync<EtlHttpPageCacheException>(() => cache.ReadAsync(PageSequence, Request()).AsTask());
        Assert.Equal("etl.http.page-cache-unavailable", unavailable.Code);
        Assert.DoesNotContain(_root, unavailable.ToString(), StringComparison.Ordinal);
        Assert.NotNull(page);
    }

    [Fact]
    public async Task Scope_and_source_revision_separate_request_page_identities()
    {
        var body = ValidBody();
        var scopeA = Admission("cache-scope-a", new EtlScope("tenant-a", "partition-a"));
        var scopeB = Admission("cache-scope-b", new EtlScope("tenant-b", "partition-a"));
        var sourcePinB = scopeA with
        {
            Bindings = scopeA.Bindings with
            {
                Source = scopeA.Bindings.Source with
                {
                    Revision = scopeA.Bindings.Source.Revision with { Revision = "source-revision-2" }
                }
            }
        };

        var first = await Cache(scopeA).PublishAsync(PageSequence, Request(), FirstAttempt, 1, ReceivedAt, body);
        var second = await Cache(scopeB).PublishAsync(PageSequence, Request(), FirstAttempt, 1, ReceivedAt, body);
        var third = await Cache(sourcePinB).PublishAsync(PageSequence, Request(), FirstAttempt, 1, ReceivedAt, body);

        Assert.NotEqual(ManifestPath(scopeA, Request()), ManifestPath(scopeB, Request()));
        Assert.NotEqual(ManifestPath(scopeA, Request()), ManifestPath(sourcePinB, Request()));
        Assert.Equal(first.Descriptor.Body, second.Descriptor.Body);
        Assert.Equal(first.Descriptor.Body, third.Descriptor.Body);
        Assert.Equal(3, Directory.GetFiles(_root, "*.page").Length);
        Assert.Single(Directory.GetFiles(_root, "*.json"));
    }

    [Fact]
    public async Task Constructor_snapshots_shards_and_requests_must_match_the_partition_pin()
    {
        var mutableShards = new[] { "shard-a", "shard-b" };
        var admission = Admission("cache-shards", new EtlScope("tenant-a", "shards"), shardIds: mutableShards);
        var cache = new EtlHttpPageCache(_root, admission, 1);
        mutableShards[1] = "changed-after-admission";

        var valid = await cache.PublishAsync(PageSequence, new EtlHttpRequest("shard-b", null), FirstAttempt, 1, ReceivedAt, ValidBody());
        var error = await Assert.ThrowsAsync<EtlHttpPageCacheException>(() =>
            cache.ReadAsync(PageSequence, Request()).AsTask());

        Assert.Equal("etl.http.page-request-invalid", error.Code);
        Assert.Equal(1, Directory.GetFiles(_root, "*.page").Length);
        Assert.Equal(1, valid.Descriptor.RecordCount);
    }

    private EtlHttpPageCache Cache(EtlConnectorRunAdmission admission, string? root = null, int partitionOrdinal = 0) =>
        new(root ?? _root, admission, partitionOrdinal);

    private EtlConnectorRunAdmission Admission(string runId, EtlScope scope, EtlPipelineV2? pipeline = null,
        IReadOnlyList<string>? shardIds = null)
    {
        pipeline ??= EtlV2TestPipelines.Create();
        var document = EtlArtifactV2Json.Write(pipeline);
        var hash = EtlArtifactJson.Hash(document);
        var artifact = new EtlArtifact(EtlArtifactV2Json.FormatVersion,
            EtlArtifactJson.CreateArtifactId(EtlArtifactV2Json.FormatVersion, "orders", 1, hash), hash, "orders", 1, document);
        var invocation = EtlRuntimeFixtures.Admission(runId, scope).Invocation with { Artifact = artifact };
        var bindings = new EtlConnectorRunBindings(
            Binding(EtlConnectorKind.Http, ((EtlHttpJsonSource)pipeline.Source).Binding, "source-revision-1"), null,
            Binding(EtlConnectorKind.Http, "output", "destination-revision-1"),
            Binding(EtlConnectorKind.Http, "errors", "destination-revision-1"), shardIds ?? ["shard-a"]);
        return new EtlConnectorRunAdmission(invocation, bindings);
    }

    private static EtlConnectorBindingRevision Binding(EtlConnectorKind kind, string alias, string revision) =>
        new(kind, new EtlBindingRevision(alias, revision, alias + "-handle", null));

    private string ManifestPath(EtlConnectorRunAdmission admission, EtlHttpRequest request) =>
        new FileChunkStore(".page").KeyFor(EtlRuntimeIdentity.ConnectorHttpPageId(admission, 0, PageSequence, request));

    private string ManifestFilePath(EtlConnectorRunAdmission admission, EtlHttpRequest request) =>
        Path.Combine(_root, ManifestPath(admission, request));

    private static EtlHttpRequest Request() => new("shard-a", null);

    private static byte[] ValidBody() => Encoding.UTF8.GetBytes("{\"records\":[{\"id\":1,\"name\":\"Ada\"}],\"next\":\"cursor-1\"}");

    private static byte[] DifferentValidBody() => Encoding.UTF8.GetBytes("{\"records\":[{\"id\":2,\"name\":\"Grace\"}],\"next\":\"cursor-2\"}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
