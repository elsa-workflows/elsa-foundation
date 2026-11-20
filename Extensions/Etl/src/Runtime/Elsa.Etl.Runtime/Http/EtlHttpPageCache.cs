using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Runtime.Output;
using Elsa.Etl.Runtime.Transformation;

namespace Elsa.Etl.Runtime.Http;

/// <summary>The capture provenance retained with one immutable HTTP source page.</summary>
/// <remarks>ResponseReceivedAtUtc means the complete bounded response body was received before the allocated attempt expired.</remarks>
internal sealed record EtlHttpPageProvenance(string AttemptId, long Fence, DateTimeOffset ResponseReceivedAtUtc);

/// <summary>A verified request-addressed page with its raw bytes, decoded records, and original capture provenance.</summary>
internal sealed record EtlCachedHttpPage(EtlRetainedHttpPage Descriptor, EtlJsonPage DecodedPage,
    EtlHttpPageProvenance Provenance, ReadOnlyMemory<byte> RawBody);

/// <summary>A value-free failure while reading or publishing a retained HTTP source page.</summary>
internal sealed class EtlHttpPageCacheException(string code)
    : Exception("The retained ETL HTTP source page is invalid, unavailable, or conflicting.")
{
    public string Code { get; } = code;
}

/// <summary>Retains bounded HTTP response bytes and publishes one request-addressed manifest as their authority point.</summary>
/// <remarks>
/// The raw JSON object is content-addressed and published first. The fixed-size <c>.page</c> manifest is
/// published create-only after it and is the only object that makes those bytes a recoverable page. An unreferenced
/// body after cancellation or a competing publication is therefore not a page and is intentionally left for the
/// runtime's later cleanup policy. Manifest reads never refetch. The caller still validates the original HTTP permit
/// and its response provenance when attaching the returned descriptor to durable SQL state.
/// </remarks>
internal sealed class EtlHttpPageCache
{
    private const string BodyIdentityPrefix = "etl-http-page-body-v1:";
    private const string PageBodyIdentityPrefix = "etl-http-page-body-v2:";
    private const int MagicLength = 8;
    private const int IdentityLength = 64;
    private const int AttemptIdLength = 32;
    private const int BodyKeyLength = 69;
    private const int HashLength = 64;
    private const int NumberLength = sizeof(long);
    private const int ManifestPayloadLength = MagicLength + IdentityLength + AttemptIdLength + NumberLength + NumberLength +
                                              BodyKeyLength + HashLength + NumberLength;
    private const int ManifestHashLength = 32;
    private const int ManifestLength = ManifestPayloadLength + ManifestHashLength;
    private static readonly byte[] Magic = "ETLHPG01"u8.ToArray();
    private static readonly Encoding StrictAscii = Encoding.GetEncoding("us-ascii",
        EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

    private readonly string _approvedRoot;
    private readonly EtlConnectorRunAdmission _admission;
    private readonly int _partitionOrdinal;
    private readonly string[] _shardIds;
    private readonly EtlHttpJsonSource _source;
    private readonly EtlConnectorLimits _limits;
    private readonly EtlHttpPolicy _policy;
    private readonly EtlHttpRequestBuilder? _requestBuilder;
    private readonly long _pollGeneration;
    private readonly bool _continuous;
    private readonly FileChunkStore _bodyStore = new(".json");
    private readonly FileChunkStore _manifestStore = new(".page");

    /// <summary>Creates a page cache from one admitted connector snapshot and the host-approved absolute root.</summary>
    /// <param name="pollGeneration">The poll generation of the position this cache serves; above 0 only in a continuous run.</param>
    public EtlHttpPageCache(string approvedRoot, EtlConnectorRunAdmission admission, int partitionOrdinal,
        EtlHttpRequestBuilder? requestBuilder = null, long pollGeneration = 0)
    {
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentNullException.ThrowIfNull(admission.Invocation);
        ArgumentNullException.ThrowIfNull(admission.Invocation.Artifact);
        ArgumentNullException.ThrowIfNull(admission.Bindings);
        if (string.IsNullOrWhiteSpace(approvedRoot) || !Path.IsPathFullyQualified(approvedRoot))
            throw Refused("etl.http.page-cache-root-invalid");
        if (partitionOrdinal < 0)
            throw Refused("etl.http.page-request-invalid");

        EtlPipelineV2 pipeline;
        try
        {
            pipeline = EtlArtifactV2Json.Read(admission.Invocation.Artifact.Pipeline);
        }
        catch (Exception exception) when (exception is ArgumentException or System.Text.Json.JsonException or NotSupportedException)
        {
            throw Refused("etl.http.page-admission-invalid");
        }

        if (pipeline.Source is not EtlHttpJsonSource http || pipeline.Limits is null || pipeline.Http is null)
            throw Refused("etl.http.page-admission-invalid");

        var sourceBinding = admission.Bindings.Source;
        if (sourceBinding is null || sourceBinding.Kind != EtlConnectorKind.Http || sourceBinding.Revision is null ||
            !StringComparer.Ordinal.Equals(sourceBinding.Revision.Alias, http.Binding))
            throw Refused("etl.http.page-admission-invalid");

        var shardIds = admission.Bindings.HttpShardIds?.ToArray()
            ?? throw Refused("etl.http.page-admission-invalid");
        if (shardIds.Length == 0 || shardIds.Any(string.IsNullOrWhiteSpace) ||
            shardIds.Distinct(StringComparer.Ordinal).Count() != shardIds.Length || partitionOrdinal >= shardIds.Length)
            throw Refused("etl.http.page-admission-invalid");
        // A continuous run's final page normalizes its unmoved cursor away, which needs the exact request URIs.
        if ((http.Pagination is EtlNextLinkPagination || pipeline.Continuous is not null) && requestBuilder is null ||
            requestBuilder is not null && !requestBuilder.Matches(http, pipeline.Http, sourceBinding, shardIds))
            throw Refused("etl.http.page-admission-invalid");

        var policy = pipeline.Http;
        if (policy.MaxPageBytes <= 0 || policy.MaxPageBytes > int.MaxValue ||
            policy.MaxRetainedInputBytes <= 0 || policy.MaxPageBytes > policy.MaxRetainedInputBytes ||
            policy.MaxPageRecords <= 0 || policy.MaxPagesPerPartition <= 0 ||
            policy.MaxContinuationBytes <= 0 ||
            policy.MaxContinuationBytes > EtlHttpPolicy.MaximumSupportedContinuationBytes)
            throw Refused("etl.http.page-policy-invalid");

        try
        {
            _approvedRoot = Path.GetFullPath(approvedRoot);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            throw Refused("etl.http.page-cache-root-invalid");
        }

        _partitionOrdinal = partitionOrdinal;
        _shardIds = shardIds;
        _source = http;
        _limits = pipeline.Limits;
        _policy = policy;
        _requestBuilder = requestBuilder;
        _continuous = pipeline.Continuous is not null;
        if (pollGeneration < 0 || pollGeneration > 0 && !_continuous)
            throw Refused("etl.http.page-request-invalid");
        _pollGeneration = pollGeneration;
        // Do not retain the caller-owned shard list. It is part of the admission pin used by callers
        // that may share mutable collection implementations across physical attempts.
        _admission = admission with
        {
            Bindings = admission.Bindings with { HttpShardIds = Array.AsReadOnly(shardIds) }
        };
    }

    /// <summary>Returns the verified captured page, or null only when its exact request manifest is absent.</summary>
    public async ValueTask<EtlCachedHttpPage?> ReadAsync(long pageSequence, EtlHttpRequest request,
        CancellationToken cancellationToken = default)
    {
        var (identity, requestHash) = RequestIdentity(pageSequence, request);
        var manifest = await ReadManifestBytesAsync(identity, cancellationToken);
        if (manifest is null)
            return null;

        var metadata = ParseManifest(manifest, identity);
        var bodyIdentity = metadata.BodyIdentity;
        byte[]? body;
        try
        {
            body = await _bodyStore.ReadAsync(_approvedRoot, bodyIdentity, _policy.MaxPageBytes,
                metadata.BodyByteLength, cancellationToken);
        }
        catch (EtlEffectException exception)
        {
            throw exception.Code == "etl.output-read-failed" ? Unavailable() : Corrupt();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw Unavailable();
        }

        if (body is null)
            throw Corrupt();
        if (body.LongLength != metadata.BodyByteLength || !FixedTimeEqualsSha256(body, metadata.BodyHash))
            throw Corrupt();

        EtlJsonPage decoded;
        try
        {
            decoded = EtlJsonPageReader.Read(body, _source, _limits, _policy);
        }
        catch (EtlJsonPageReadException)
        {
            throw Corrupt();
        }

        decoded = NormalizeContinuation(request, decoded, reading: true);

        var descriptor = new EtlRetainedHttpPage(pageSequence, requestHash,
            new EtlObjectReference(metadata.BodyKey, metadata.BodyHash, metadata.BodyByteLength),
            decoded.Records.Count, decoded.Continuation);
        return new EtlCachedHttpPage(descriptor, decoded, metadata.Provenance, body);
    }

    /// <summary>Publishes a decoded response body and then its immutable request manifest.</summary>
    /// <remarks>The response timestamp is captured after the complete bounded body is received, before attempt expiry.</remarks>
    public async ValueTask<EtlCachedHttpPage> PublishAsync(long pageSequence, EtlHttpRequest request, Guid attemptId,
        long fence, DateTimeOffset responseReceivedAtUtc, ReadOnlyMemory<byte> rawBody,
        CancellationToken cancellationToken = default)
    {
        if (fence <= 0 || responseReceivedAtUtc.Offset != TimeSpan.Zero)
            throw Refused("etl.http.page-provenance-invalid");
        if (rawBody.Length > _policy.MaxPageBytes)
            throw Refused("etl.http.page-too-large");
        cancellationToken.ThrowIfCancellationRequested();

        // Own the bytes before decoding, hashing, or awaiting storage so a caller cannot change the
        // response between validation and publication.
        var ownedBody = rawBody.ToArray();
        var (identity, requestHash) = RequestIdentity(pageSequence, request);
        EtlJsonPage decoded;
        try
        {
            decoded = EtlJsonPageReader.Read(ownedBody, _source, _limits, _policy);
        }
        catch (EtlJsonPageReadException exception)
        {
            throw new EtlHttpPageCacheException(exception.Code);
        }
        decoded = NormalizeContinuation(request, decoded, reading: false);

        if (Directory.Exists(_approvedRoot))
        {
            var existing = await ReadAsync(pageSequence, request, cancellationToken);
            if (existing is not null)
                return SameBody(existing, ownedBody) ? existing : throw Conflict();
        }

        var bodyHash = Convert.ToHexString(SHA256.HashData(ownedBody)).ToLowerInvariant();
        // A continuous run deletes each page's body once the shard leaves it, so its body is never shared (spec 1196).
        var bodyIdentity = _continuous ? PageBodyIdentity(identity, bodyHash) : BodyIdentity(bodyHash);
        EtlChunk bodyChunk;
        try
        {
            bodyChunk = await _bodyStore.PublishAsync(_approvedRoot, bodyIdentity, _policy.MaxPageBytes,
                (stream, ct) => stream.WriteAsync(ownedBody, ct), cancellationToken);
        }
        catch (EtlEffectException exception)
        {
            throw exception.Code == "etl.effect-conflict" ? Conflict() : WriteFailed();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw WriteFailed();
        }

        if (bodyChunk.Key != _bodyStore.KeyFor(bodyIdentity) || bodyChunk.ByteLength != ownedBody.LongLength ||
            bodyChunk.ContentHash != bodyHash)
            throw Corrupt();

        cancellationToken.ThrowIfCancellationRequested();
        var provenance = new EtlHttpPageProvenance(attemptId.ToString("N").ToLowerInvariant(), fence, responseReceivedAtUtc);
        var manifestBytes = WriteManifest(identity, provenance, bodyChunk.Key, bodyHash, bodyChunk.ByteLength);
        try
        {
            await _manifestStore.PublishAsync(_approvedRoot, identity, ManifestLength,
                (stream, ct) => stream.WriteAsync(manifestBytes, ct), cancellationToken);
        }
        catch (EtlEffectException exception) when (exception.Code == "etl.effect-conflict")
        {
            var competing = await ReadAsync(pageSequence, request, cancellationToken);
            if (competing is null)
                throw Conflict();
            return SameBody(competing, ownedBody) ? competing : throw Conflict();
        }
        catch (EtlEffectException)
        {
            throw WriteFailed();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw WriteFailed();
        }

        return new EtlCachedHttpPage(
            new EtlRetainedHttpPage(pageSequence, requestHash,
                new EtlObjectReference(bodyChunk.Key, bodyHash, bodyChunk.ByteLength), decoded.Records.Count, decoded.Continuation),
            decoded, provenance, ownedBody);
    }

    /// <summary>
    /// Removes a continuous page's manifest and per-page body once its shard has left it (spec 1196). A missing page is
    /// not an error, and a legacy shared body is left in place because another page may still reference it.
    /// </summary>
    public async ValueTask DeleteAsync(long pageSequence, EtlHttpRequest request, CancellationToken cancellationToken = default)
    {
        if (!_continuous)
            throw Refused("etl.http.page-request-invalid");
        var (identity, _) = RequestIdentity(pageSequence, request);
        var manifest = await ReadManifestBytesAsync(identity, cancellationToken);
        if (manifest is null)
            return;
        var metadata = ParseManifest(manifest, identity);
        try
        {
            // The manifest goes first: a crash in between leaves an unreferenced body, never a manifest without one.
            _manifestStore.Delete(_approvedRoot, identity);
            if (StringComparer.Ordinal.Equals(metadata.BodyIdentity, PageBodyIdentity(identity, metadata.BodyHash)))
                _bodyStore.Delete(_approvedRoot, metadata.BodyIdentity);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw WriteFailed();
        }
    }

    private async ValueTask<byte[]?> ReadManifestBytesAsync(string identity, CancellationToken cancellationToken)
    {
        try
        {
            return await _manifestStore.ReadAsync(_approvedRoot, identity, ManifestLength, ManifestLength, cancellationToken);
        }
        catch (EtlEffectException exception)
        {
            throw exception.Code == "etl.output-read-failed" ? Unavailable() : Corrupt();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw Unavailable();
        }
    }

    private (string Identity, string RequestHash) RequestIdentity(long pageSequence, EtlHttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        // A continuous shard's retained window bounds its pages instead of its page sequence (spec 1196).
        if (pageSequence < 0 || !_continuous && pageSequence >= _policy.MaxPagesPerPartition ||
            !StringComparer.Ordinal.Equals(request.ShardId, _shardIds[_partitionOrdinal]))
            throw Refused("etl.http.page-request-invalid");

        try
        {
            _requestBuilder?.RequestUri(request);
            return (EtlRuntimeIdentity.ConnectorHttpPageId(_admission, _partitionOrdinal, pageSequence, request, _pollGeneration),
                EtlRuntimeIdentity.HttpRequestHash(request));
        }
        catch (ArgumentException)
        {
            throw Refused("etl.http.page-request-invalid");
        }
    }

    private EtlJsonPage NormalizeContinuation(EtlHttpRequest request, EtlJsonPage decoded, bool reading)
    {
        if (_requestBuilder is null)
            return decoded;

        try
        {
            // Only a continuous run retains a caught-up page (as its final page); its unmoved cursor ends the shard.
            if (_continuous && _requestBuilder.IsCaughtUp(request, decoded.Records.Count, decoded.Continuation))
                return decoded with { Continuation = null };
            return decoded with { Continuation = _requestBuilder.NextContinuation(request, decoded.Continuation) };
        }
        catch (EtlHttpRequestBuilderException exception)
        {
            if (reading)
                throw Corrupt();
            throw Refused(exception.Code);
        }
    }

    private PageManifest ParseManifest(byte[] bytes, string expectedIdentity)
    {
        if (bytes.Length != ManifestLength || !bytes.AsSpan(0, MagicLength).SequenceEqual(Magic))
            throw Corrupt();

        var expectedManifestHash = SHA256.HashData(bytes.AsSpan(0, ManifestPayloadLength));
        if (!CryptographicOperations.FixedTimeEquals(expectedManifestHash,
                bytes.AsSpan(ManifestPayloadLength, ManifestHashLength)))
            throw Corrupt();

        var offset = MagicLength;
        var identity = ReadAscii(bytes, ref offset, IdentityLength);
        var attemptIdText = ReadAscii(bytes, ref offset, AttemptIdLength);
        var fence = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(offset, NumberLength));
        offset += NumberLength;
        var receivedTicks = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(offset, NumberLength));
        offset += NumberLength;
        var bodyKey = ReadAscii(bytes, ref offset, BodyKeyLength);
        var bodyHash = ReadAscii(bytes, ref offset, HashLength);
        var bodyByteLength = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(offset, NumberLength));

        if (!StringComparer.Ordinal.Equals(identity, expectedIdentity) || !IsLowerSha256(identity) ||
            !Guid.TryParseExact(attemptIdText, "N", out var attemptId) ||
            !StringComparer.Ordinal.Equals(attemptId.ToString("N"), attemptIdText) ||
            fence <= 0 || receivedTicks < DateTimeOffset.MinValue.UtcTicks || receivedTicks > DateTimeOffset.MaxValue.UtcTicks ||
            !IsLowerSha256(bodyHash) || bodyByteLength <= 0 || bodyByteLength > _policy.MaxPageBytes)
            throw Corrupt();
        // A continuous page published before spec 1196 keeps its shared body, which is read but never deleted.
        var bodyIdentity = _continuous && StringComparer.Ordinal.Equals(bodyKey, _bodyStore.KeyFor(PageBodyIdentity(identity, bodyHash)))
            ? PageBodyIdentity(identity, bodyHash)
            : BodyIdentity(bodyHash);
        if (!StringComparer.Ordinal.Equals(bodyKey, _bodyStore.KeyFor(bodyIdentity)))
            throw Corrupt();

        DateTimeOffset receivedAt;
        try
        {
            receivedAt = new DateTimeOffset(receivedTicks, TimeSpan.Zero);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw Corrupt();
        }

        return new PageManifest(bodyIdentity, bodyKey, bodyHash, bodyByteLength,
            new EtlHttpPageProvenance(attemptIdText, fence, receivedAt));
    }

    private static byte[] WriteManifest(string identity, EtlHttpPageProvenance provenance,
        string bodyKey, string bodyHash, long bodyByteLength)
    {
        var bytes = new byte[ManifestLength];
        var offset = 0;
        Magic.CopyTo(bytes, offset);
        offset += MagicLength;
        WriteAscii(bytes, ref offset, identity);
        WriteAscii(bytes, ref offset, provenance.AttemptId);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(offset, NumberLength), provenance.Fence);
        offset += NumberLength;
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(offset, NumberLength), provenance.ResponseReceivedAtUtc.UtcTicks);
        offset += NumberLength;
        WriteAscii(bytes, ref offset, bodyKey);
        WriteAscii(bytes, ref offset, bodyHash);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(offset, NumberLength), bodyByteLength);
        var manifestHash = SHA256.HashData(bytes.AsSpan(0, ManifestPayloadLength));
        manifestHash.AsSpan().CopyTo(bytes.AsSpan(ManifestPayloadLength, ManifestHashLength));
        return bytes;
    }

    private static string ReadAscii(byte[] bytes, ref int offset, int byteLength)
    {
        string value;
        try
        {
            value = StrictAscii.GetString(bytes, offset, byteLength);
        }
        catch (DecoderFallbackException)
        {
            throw Corrupt();
        }
        offset += byteLength;
        return value;
    }

    private static void WriteAscii(byte[] bytes, ref int offset, string value)
    {
        var written = StrictAscii.GetBytes(value.AsSpan(), bytes.AsSpan(offset));
        offset += written;
    }

    private static bool IsLowerSha256(string value) => value.Length == HashLength &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool FixedTimeEqualsSha256(byte[] bytes, string expectedHash)
    {
        var expected = Convert.FromHexString(expectedHash);
        var actual = SHA256.HashData(bytes);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static string BodyIdentity(string hash) => BodyIdentityPrefix + hash;

    private static string PageBodyIdentity(string pageIdentity, string hash) => PageBodyIdentityPrefix + pageIdentity + ":" + hash;

    private static bool SameBody(EtlCachedHttpPage existing, byte[] candidate) =>
        existing.RawBody.Span.SequenceEqual(candidate);

    private static EtlHttpPageCacheException Refused(string code) => new(code);
    private static EtlHttpPageCacheException Corrupt() => new("etl.http.page-cache-corrupt");
    private static EtlHttpPageCacheException Unavailable() => new("etl.http.page-cache-unavailable");
    private static EtlHttpPageCacheException Conflict() => new("etl.http.page-cache-conflict");
    private static EtlHttpPageCacheException WriteFailed() => new("etl.http.page-cache-write-failed");

    private sealed record PageManifest(string BodyIdentity, string BodyKey, string BodyHash, long BodyByteLength,
        EtlHttpPageProvenance Provenance);
}
