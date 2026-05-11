using Elsa.Serialization.Core;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Serialization.Services
{
    /// <summary>
    /// Serializes simple DTOs from and to JSON.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="JsonPayloadSerializer"/> class.
    /// </remarks>
    public sealed class JsonPayloadSerializer(IServiceProvider serviceProvider) : IPayloadSerializer
    {
        /// <inheritdoc />
        public string Serialize(object payload)
        {
            var options = GetOptions();
            return JsonSerializer.Serialize(payload, options);
        }

        /// <inheritdoc />
        public JsonElement SerializeToElement(object payload)
        {
            var options = GetOptions();
            return JsonSerializer.SerializeToElement(payload, options);
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

            var converterProviders = serviceProvider.GetServices<IPayloadSerializerConverterProvider>();
            foreach (var provider in converterProviders)
            {
                options.Converters.Add(
                    provider.Get()
                );
            }

            return options;
        }


    }

}
