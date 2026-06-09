using CShells.FastEndpoints.Contracts;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Api.FastEndpoints.Configurators;

internal sealed class SerializationFastEndpointConfigurator
    : IFastEndpointsConfigurator
{
    private static readonly JsonSerializerOptions options = Create();

    private static JsonSerializerOptions Create()
    {
        var result = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        result.Converters.Add(new JsonStringEnumConverter());

        return result;
    }

    /// <inheritdoc />
    public void Configure(Config config)
    {
        config.Serializer.RequestDeserializer = DeserializeRequestAsync;
        config.Serializer.ResponseSerializer = SerializeResponseAsync;

        config.Binding.ValueParserFor<DateTimeOffset>(s =>
            new(DateTimeOffset.TryParse(s.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result), result));
    }

    private static ValueTask<object?> DeserializeRequestAsync(HttpRequest httpRequest, Type modelType, JsonSerializerContext? serializerContext, CancellationToken cancellationToken)
    {
        return serializerContext == null
            ? JsonSerializer.DeserializeAsync(httpRequest.Body, modelType, options, cancellationToken)
            : JsonSerializer.DeserializeAsync(httpRequest.Body, modelType, serializerContext, cancellationToken);
    }

    private static Task SerializeResponseAsync(HttpResponse httpResponse, object? dto, string contentType, JsonSerializerContext? serializerContext, CancellationToken cancellationToken)
    {
        httpResponse.ContentType = contentType;
        return serializerContext == null
            ? JsonSerializer.SerializeAsync(httpResponse.Body, dto, dto?.GetType() ?? typeof(object), options, cancellationToken)
            : JsonSerializer.SerializeAsync(httpResponse.Body, dto, dto?.GetType() ?? typeof(object), serializerContext, cancellationToken);
    }
}