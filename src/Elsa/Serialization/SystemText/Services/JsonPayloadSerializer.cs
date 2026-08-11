using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.JsonConverters;
using Elsa.Serialization.SystemText.Resolvers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Elsa.Serialization.SystemText.Services;

/// <summary>
/// Serializes simple DTOs from and to JSON. Reads its <see cref="JsonConverter"/> set
/// from <see cref="JsonPayloadConverterRegistry"/> — populated once at startup by
/// <see cref="JsonPayloadConvertersInitializingStartupTask"/> dispatching the
/// <see cref="JsonPayloadConvertersInitializing"/> domain event (framework §2.6.1
/// Registry + StartUp Task sub-pattern; Elsa §E3.3 worked example).
/// </summary>
public sealed class JsonPayloadSerializer(JsonPayloadConverterRegistry converterRegistry) : IPayloadSerializer
{
    // System.Text.Json caches reflected JsonTypeInfo/converter metadata per JsonSerializerOptions
    // instance. Rebuilding options on every Serialize/Deserialize call (this is a hot path —
    // execution-log-record payloads) threw that cache away every call. Cache the built options and
    // rebuild only when the converter registry's revision changes. _cachedRevision starts at a
    // sentinel that can never equal a real revision so the first call always builds.
    private readonly object _optionsLock = new();
    private JsonSerializerOptions? _cachedOptions;
    private int _cachedRevision = -1;
    /// <inheritdoc />
    public string Serialize(object payload)
    {
        var options = GetOptions();
        // Pass payload.GetType() so the input type is the runtime type, not 'object'. Without it, generic
        // inference picks TValue = object and STJ serializes against the declared 'object' type, losing the
        // payload's members. With the runtime type, STJ uses built-in handling and produces plain JSON for
        // ordinary classes/collections.
        return JsonSerializer.Serialize(payload, payload.GetType(), options);
    }

    /// <inheritdoc />
    public JsonElement SerializeToElement(object payload)
    {
        var options = GetOptions();
        return JsonSerializer.SerializeToElement(payload, payload.GetType(), options);
    }

    /// <inheritdoc />
    public object Deserialize(string payload)
    {
        return Deserialize<object>(payload);
    }

    public object Deserialize(string serializedData, Type type)
    {
        var options = GetOptions();
        return JsonSerializer.Deserialize(serializedData, type, options)!;
    }

    /// <inheritdoc />
    public object Deserialize(JsonElement payload)
    {
        return Deserialize<object>(payload);
    }

    /// <inheritdoc />
    public T Deserialize<T>(string payload)
    {
        var options = GetOptions();
        return JsonSerializer.Deserialize<T>(payload, options)!;
    }

    /// <inheritdoc />
    public T Deserialize<T>(JsonElement payload)
    {
        var options = GetOptions();
        return payload.Deserialize<T>(options)!;
    }

    /// <inheritdoc />
    public JsonSerializerOptions GetOptions()
    {
        var registryRevision = converterRegistry.Revision;

        // Fast path: options already built for the current registry revision. Volatile read pairs
        // with the write under the lock so a stale cache is never observed after an invalidation.
        var cached = Volatile.Read(ref _cachedOptions);
        if (cached is not null && Volatile.Read(ref _cachedRevision) == registryRevision)
            return cached;

        lock (_optionsLock)
        {
            // Double-check inside the lock: another thread may have rebuilt while we waited, and the
            // registry may have advanced again — re-read its revision so we build for the latest.
            registryRevision = converterRegistry.Revision;
            if (_cachedOptions is not null && _cachedRevision == registryRevision)
                return _cachedOptions;

            var options = BuildOptions();
            _cachedRevision = registryRevision;
            Volatile.Write(ref _cachedOptions, options);
            return options;
        }
    }

    private JsonSerializerOptions BuildOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

            // Deterministic serialization (spec 086; ADR 0034 D3/D8): equal graphs must serialize to
            // byte-identical JSON so the output can be content-hashed. SortObjectMembers fixes object member
            // order; DeterministicDictionaryConverterFactory sorts every string-keyed dictionary shape,
            // including object-valued ones (formerly the retired polymorphic converter's job — ADR 0035 D2).
            // These target the real cross-process nondeterminism — per-process string-hash-seeded dictionary
            // enumeration and unfixed reflection member order.
            // Embedded opaque JSON (a JsonElement such as ActivityNode.Structure.Payload) is deliberately NOT
            // rewritten: it re-emits verbatim in parse order with no hashing, so it is already byte-stable
            // across processes, and reordering an opaque blob would assert a semantic equality Elsa does not
            // own (ADR 0035 D3 — opaque JSON stays verbatim). Baked into the per-revision options GetOptions
            // caches — no per-call cost.
            TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers = { DeterministicOrderTypeInfoModifier.SortObjectMembers },
            },
        };

        options.Converters.Add(new DeterministicDictionaryConverterFactory());

        // Sync access to the registry populated at startup. No DI enumeration here —
        // the registry is the seam between the async contribution pipeline and the
        // sync System.Text.Json callbacks.
        foreach (var converter in converterRegistry.Converters)
            options.Converters.Add(converter);

        return options;
    }
}