using Elsa.Activities.Design.Core.Models;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using System.Text.Json.Schema;

namespace Elsa.Activities.Design.Api.Services;

internal static class ActivityNodePresentationOpenApi
{
    public static async Task ConfigureAsync<TRequest>(
        OpenApiOperation _, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var request = await context.GetOrCreateSchemaAsync(typeof(TRequest), null, cancellationToken);
        if (request.Properties is null)
            return;
        request.Properties["activityPresentation"] = new OpenApiSchema
        {
            Type = JsonSchemaType.Array | JsonSchemaType.Null,
            Items = await context.GetOrCreateSchemaAsync(typeof(ActivityNodePresentationRecord), null, cancellationToken)
        };
        var document = context.Document ?? throw new InvalidOperationException("The OpenAPI document is unavailable.");
        var schemas = (document.Components ??= new OpenApiComponents()).Schemas ??= new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
        schemas[typeof(TRequest).Name] = request;
    }
}
