using System.Buffers;
using System.Text;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Runtime.Bindings;

namespace Elsa.Etl.Runtime.Http;

/// <summary>A value-free refusal while constructing a request under a pinned HTTP endpoint policy.</summary>
internal sealed class EtlHttpRequestBuilderException(string code)
    : Exception("The ETL HTTP source request is invalid or outside its host-approved endpoint policy.")
{
    public string Code { get; } = code;
}

/// <summary>Builds bounded source request URIs using only the admitted endpoint policy and shard seeds.</summary>
/// <remarks>The resolved binding is copied during construction; this builder never retains its connection or authenticator.</remarks>
internal sealed class EtlHttpRequestBuilder
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly EtlHttpJsonSource _source;
    private readonly EtlHttpPolicy _policy;
    private readonly HttpEndpointPolicy _endpointPolicy;
    private readonly IReadOnlyDictionary<string, Uri> _shards;
    private readonly string[] _shardIds;
    private readonly string _bindingRevision;
    private readonly string _bindingHandle;

    public EtlHttpRequestBuilder(EtlHttpJsonSource source, HostHttpResolvedBinding binding, EtlHttpPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(policy);
        if (!StringComparer.Ordinal.Equals(source.Binding, binding.Alias) ||
            !StringComparer.Ordinal.Equals(binding.Role, "Source") ||
            string.IsNullOrWhiteSpace(binding.Revision) || string.IsNullOrWhiteSpace(binding.Handle) ||
            policy.MaxContinuationBytes <= 0 || policy.MaxContinuationBytes > EtlHttpPolicy.MaximumSupportedContinuationBytes)
            throw Refused("etl.http.request-binding-invalid");

        try
        {
            _source = source with { Fields = Array.AsReadOnly(source.Fields?.ToArray() ?? throw InvalidBinding()) };
            _policy = policy with { };
            _endpointPolicy = new HttpEndpointPolicy(binding.EndpointPolicy.Endpoint.AbsoluteUri,
                binding.EndpointPolicy.AllowedPaths.ToArray(), binding.EndpointPolicy.AllowHttp);

            var shards = binding.Shards?.ToArray() ?? throw InvalidBinding();
            if (shards.Length == 0 || shards.Any(shard => shard is null || string.IsNullOrWhiteSpace(shard.Id) || shard.SeedUri is null) ||
                shards.Select(shard => shard.Id).Distinct(StringComparer.Ordinal).Count() != shards.Length)
                throw InvalidBinding();

            var copiedShards = new Dictionary<string, Uri>(shards.Length, StringComparer.Ordinal);
            foreach (var shard in shards)
            {
                _endpointPolicy.Validate(shard.SeedUri);
                copiedShards.Add(shard.Id, new Uri(shard.SeedUri.AbsoluteUri, UriKind.Absolute));
            }

            _shards = new System.Collections.ObjectModel.ReadOnlyDictionary<string, Uri>(copiedShards);
            _shardIds = shards.Select(shard => shard.Id).ToArray();
            _bindingRevision = binding.Revision;
            _bindingHandle = binding.Handle;
        }
        catch (EtlHttpRequestBuilderException)
        {
            throw;
        }
        catch (EtlBindingException exception)
        {
            throw Refused(exception.Code);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NullReferenceException)
        {
            throw InvalidBinding();
        }
    }

    /// <summary>Returns whether this builder was made from the exact source pin, policy, and ordered shard IDs in an admission.</summary>
    public bool Matches(EtlHttpJsonSource source, EtlHttpPolicy policy, EtlConnectorBindingRevision binding,
        IReadOnlyList<string> shardIds)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(shardIds);

        return binding.Kind == EtlConnectorKind.Http &&
               StringComparer.Ordinal.Equals(source.Binding, binding.Revision.Alias) &&
               StringComparer.Ordinal.Equals(binding.Revision.Revision, _bindingRevision) &&
               StringComparer.Ordinal.Equals(binding.Revision.Handle, _bindingHandle) &&
               policy == _policy && SameSource(source, _source) &&
               shardIds.SequenceEqual(_shardIds, StringComparer.Ordinal);
    }

    /// <summary>Builds the exact request URI for one pinned source shard and continuation.</summary>
    public Uri RequestUri(EtlHttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ShardId) || !_shards.TryGetValue(request.ShardId, out var seed))
            throw Refused("etl.http.request-invalid");

        try
        {
            switch (_source.Pagination)
            {
                case EtlSinglePagePagination:
                    if (request.Continuation is not null)
                        throw Refused("etl.http.request-invalid");
                    return seed;

                case EtlTokenPagination token:
                    if (request.Continuation is null)
                    {
                        if (TokenParameterIndexes(seed, token.QueryParameter).Count > 1)
                            throw Refused("etl.http.query-parameter-ambiguous");
                        return seed;
                    }
                    ValidateContinuationLength(request.Continuation);
                    return BuildTokenUri(seed, token.QueryParameter, request.Continuation);

                case EtlNextLinkPagination:
                    if (request.Continuation is null)
                        return seed;
                    ValidateContinuationLength(request.Continuation);
                    if (!Uri.TryCreate(request.Continuation, UriKind.Absolute, out var retainedUri) ||
                        !StringComparer.Ordinal.Equals(retainedUri.AbsoluteUri, request.Continuation))
                        throw Refused("etl.http.request-invalid");
                    _endpointPolicy.Validate(retainedUri);
                    return retainedUri;

                default:
                    throw Refused("etl.http.request-invalid");
            }
        }
        catch (EtlHttpRequestBuilderException)
        {
            throw;
        }
        catch (EtlBindingException exception)
        {
            throw Refused(exception.Code);
        }
        catch (Exception exception) when (exception is ArgumentException or UriFormatException)
        {
            throw Refused("etl.http.request-invalid");
        }
    }

    /// <summary>Normalizes a decoded page continuation for durable storage under this exact request.</summary>
    public string? NextContinuation(EtlHttpRequest currentRequest, string? decodedContinuation)
    {
        ArgumentNullException.ThrowIfNull(currentRequest);
        if (string.IsNullOrEmpty(decodedContinuation))
            return null;

        switch (_source.Pagination)
        {
            case EtlSinglePagePagination:
                throw Refused("etl.http.continuation-invalid");

            case EtlTokenPagination token:
                ValidateContinuationLength(decodedContinuation);
                var tokenCurrentUri = RequestUri(currentRequest);
                var tokenSeed = _shards[currentRequest.ShardId];
                var tokenNextUri = BuildTokenUri(tokenSeed, token.QueryParameter, decodedContinuation);
                if (tokenNextUri.Equals(tokenCurrentUri) || tokenNextUri.Equals(tokenSeed))
                    throw Refused("etl.http.pagination-cycle");
                return decodedContinuation;

            case EtlNextLinkPagination:
                try
                {
                    var currentUri = RequestUri(currentRequest);
                    var nextUri = _endpointPolicy.Resolve(decodedContinuation, currentUri);
                    if (nextUri.Equals(currentUri) || nextUri.Equals(_shards[currentRequest.ShardId]))
                        throw Refused("etl.http.pagination-cycle");
                    var normalized = nextUri.AbsoluteUri;
                    ValidateContinuationLength(normalized);
                    return normalized;
                }
                catch (EtlHttpRequestBuilderException)
                {
                    throw;
                }
                catch (EtlBindingException exception)
                {
                    throw Refused(exception.Code);
                }
                catch (Exception exception) when (exception is ArgumentException or UriFormatException)
                {
                    throw Refused("etl.http.continuation-invalid");
                }

            default:
                throw Refused("etl.http.continuation-invalid");
        }
    }

    private Uri BuildTokenUri(Uri seed, string parameterName, string token)
    {
        var rawQuery = seed.GetComponents(UriComponents.Query, UriFormat.UriEscaped);
        var parameters = rawQuery.Length == 0 ? new List<string>() : rawQuery.Split('&').ToList();
        var matchingIndexes = TokenParameterIndexes(seed, parameterName);

        if (matchingIndexes.Count > 1)
            throw Refused("etl.http.query-parameter-ambiguous");

        var encodedToken = Uri.EscapeDataString(token);
        if (matchingIndexes.Count == 1)
        {
            var index = matchingIndexes[0];
            var equals = parameters[index].IndexOf('=');
            var rawName = equals < 0 ? parameters[index] : parameters[index][..equals];
            parameters[index] = rawName + "=" + encodedToken;
        }
        else
            parameters.Add(Uri.EscapeDataString(parameterName) + "=" + encodedToken);

        var uriText = seed.GetLeftPart(UriPartial.Path) + "?" + string.Join("&", parameters);
        var uri = new Uri(uriText, UriKind.Absolute);
        _endpointPolicy.Validate(uri);
        return uri;
    }

    private static List<int> TokenParameterIndexes(Uri seed, string parameterName)
    {
        var rawQuery = seed.GetComponents(UriComponents.Query, UriFormat.UriEscaped);
        if (rawQuery.Length == 0)
            return [];

        var parameters = rawQuery.Split('&');
        var matchingIndexes = new List<int>();
        for (var index = 0; index < parameters.Length; index++)
        {
            var equals = parameters[index].IndexOf('=');
            var rawName = equals < 0 ? parameters[index] : parameters[index][..equals];
            if (StringComparer.Ordinal.Equals(DecodeQueryName(rawName), parameterName))
                matchingIndexes.Add(index);
        }
        return matchingIndexes;
    }

    private static string DecodeQueryName(string encoded)
    {
        var bytes = new byte[StrictUtf8.GetMaxByteCount(encoded.Length)];
        var count = 0;
        for (var index = 0; index < encoded.Length;)
        {
            if (encoded[index] == '+')
            {
                // Query parameters use application/x-www-form-urlencoded name decoding: raw '+' is a space.
                // An encoded '%2B' still decodes to a literal plus in the percent-escape branch below.
                bytes[count++] = (byte)' ';
                index++;
                continue;
            }

            if (encoded[index] == '%')
            {
                if (index + 2 >= encoded.Length)
                    throw Refused("etl.http.query-invalid");
                var high = HexValue(encoded[index + 1]);
                var low = HexValue(encoded[index + 2]);
                if (high < 0 || low < 0)
                    throw Refused("etl.http.query-invalid");
                bytes[count++] = (byte)((high << 4) | low);
                index += 3;
                continue;
            }

            var status = Rune.DecodeFromUtf16(encoded.AsSpan(index), out var rune, out var consumed);
            if (status != OperationStatus.Done)
                throw Refused("etl.http.query-invalid");
            count += rune.EncodeToUtf8(bytes.AsSpan(count));
            index += consumed;
        }

        try
        {
            return StrictUtf8.GetString(bytes, 0, count);
        }
        catch (DecoderFallbackException)
        {
            throw Refused("etl.http.query-invalid");
        }
    }

    private void ValidateContinuationLength(string value)
    {
        int byteCount;
        try
        {
            byteCount = StrictUtf8.GetByteCount(value);
        }
        catch (EncoderFallbackException)
        {
            throw Refused("etl.http.continuation-invalid");
        }

        if (byteCount == 0 || byteCount > _policy.MaxContinuationBytes)
            throw Refused("etl.http.continuation-too-large");
    }

    private static int HexValue(char value) => value switch
    {
        >= '0' and <= '9' => value - '0',
        >= 'a' and <= 'f' => value - 'a' + 10,
        >= 'A' and <= 'F' => value - 'A' + 10,
        _ => -1
    };

    private static bool SameSource(EtlHttpJsonSource left, EtlHttpJsonSource right) =>
        StringComparer.Ordinal.Equals(left.Binding, right.Binding) &&
        StringComparer.Ordinal.Equals(left.RecordsPointer, right.RecordsPointer) &&
        Equals(left.Pagination, right.Pagination) && left.Fields.SequenceEqual(right.Fields);

    private static EtlHttpRequestBuilderException InvalidBinding() => Refused("etl.http.request-binding-invalid");
    private static EtlHttpRequestBuilderException Refused(string code) => new(code);
}
