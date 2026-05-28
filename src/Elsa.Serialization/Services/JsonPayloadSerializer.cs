using Elsa.Serialization.Core;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Serialization.Services
{
    /// <summary>
    /// Serializes simple DTOs from and to JSON. Reads its <see cref="JsonConverter"/> set
    /// from <see cref="JsonPayloadConverterRegistry"/> — populated once at startup by
    /// <see cref="JsonPayloadConvertersInitializingStartupTask"/> dispatching the
    /// <see cref="OnJsonPayloadConvertersInitializing"/> domain event (framework §2.6.1
    /// Registry + StartUp Task sub-pattern; Elsa §E3.3 worked example).
    /// </summary>
    public sealed class JsonPayloadSerializer(JsonPayloadConverterRegistry converterRegistry) : IPayloadSerializer
    {
        /// <inheritdoc />
        public string Serialize(object payload)
        {
            var options = GetOptions();
            // Pass payload.GetType() so the input type is the runtime type, not 'object'.
            // Without this, generic inference picks TValue = object and the
            // PolymorphicObjectConverterFactory claims the call, wrapping collections in
            // {_items: [...], _type: "..."}. With the runtime type, STJ uses built-in
            // handling and produces plain JSON for ordinary classes/collections.
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
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            };

            // Sync access to the registry populated at startup. No DI enumeration here —
            // the registry is the seam between the async contribution pipeline and the
            // sync System.Text.Json callbacks.
            foreach (var converter in converterRegistry.Converters)
                options.Converters.Add(converter);

            return options;
        }
    }
}
