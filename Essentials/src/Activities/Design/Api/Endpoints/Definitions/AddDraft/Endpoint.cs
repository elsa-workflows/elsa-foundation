using Elsa.Activities.Design.Api.Services;
using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Constants;
using Elsa.Activities.Design.Api.Handlers;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Models;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using MinimalEndpoints;
using System.Text.Json.Schema;

namespace Elsa.Activities.Design.Api.Endpoints.Definitions.AddDraft;

[Post("/design/activities/definitions/{definitionId}/drafts")]
[RequirePermission(ActivityDesignPermissions.Manage)]
[AuthoringProblems]
public sealed class Endpoint(IReusableActivityAuthoringService service) : ApiEndpoint<CreateReusableActivityDraftBody, ReusableActivityDraftView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DefinitionsAddDraft";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
        options.SuccessStatus = StatusCodes.Status201Created;
        options.Convention(builder => builder.AddOpenApiOperationTransformer(ConfigureOpenApiAsync));
    }

    public override async Task<ReusableActivityDraftView> HandleAsync(CreateReusableActivityDraftBody request, CancellationToken cancellationToken)
    {
        var command = request.ToCommand();
        var response = await service.CreateDraftAsync(command, cancellationToken);
        HttpContext.Response.Headers.Location = $"/{RouteConstants.GetRoute($"drafts/{response.DraftId}")}";
        return response;
    }

    private static async Task ConfigureOpenApiAsync(
        OpenApiOperation _,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var requestSchema = await context.GetOrCreateSchemaAsync(typeof(CreateReusableActivityDraftBody), null, cancellationToken);
        if (requestSchema.Properties is null)
            return;

        var recordSchema = await context.GetOrCreateSchemaAsync(typeof(ActivityLayoutRecord), null, cancellationToken);
        requestSchema.Properties["layout"] = new OpenApiSchema
        {
            Type = JsonSchemaType.Array | JsonSchemaType.Null,
            Items = recordSchema
        };

        requestSchema.Properties["activityPresentation"] = new OpenApiSchema
        {
            Type = JsonSchemaType.Array | JsonSchemaType.Null,
            Items = await context.GetOrCreateSchemaAsync(typeof(ActivityNodePresentationRecord), null, cancellationToken)
        };

        var document = context.Document ?? throw new InvalidOperationException("The OpenAPI document is unavailable.");
        var components = document.Components ??= new OpenApiComponents();
        var schemas = components.Schemas ??= new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
        schemas[nameof(CreateReusableActivityDraftBody)] = requestSchema;
    }
}
