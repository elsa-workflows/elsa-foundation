using CShells.FastEndpoints.Contracts;
using Elsa.Serialization.Core;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.FastEndpoints.Configurators
{
    internal sealed class SerializationFastEndpointConfigurator
        : IFastEndpointsConfigurator
    {
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
            var serializer = httpRequest.HttpContext.RequestServices.GetRequiredService<IPayloadSerializer>();
            var options = serializer.GetOptions();

            return serializerContext == null
                ? JsonSerializer.DeserializeAsync(httpRequest.Body, modelType, options, cancellationToken)
                : JsonSerializer.DeserializeAsync(httpRequest.Body, modelType, serializerContext, cancellationToken);
        }

        private static Task SerializeResponseAsync(HttpResponse httpResponse, object? dto, string contentType, JsonSerializerContext? serializerContext, CancellationToken cancellationToken)
        {
            var serializer = httpResponse.HttpContext.RequestServices.GetRequiredService<IPayloadSerializer>();
            var options = serializer.GetOptions();

            httpResponse.ContentType = contentType;
            return serializerContext == null
                ? JsonSerializer.SerializeAsync(httpResponse.Body, dto, dto?.GetType() ?? typeof(object), options, cancellationToken)
                : JsonSerializer.SerializeAsync(httpResponse.Body, dto, dto?.GetType() ?? typeof(object), serializerContext, cancellationToken);
        }
    }
}
