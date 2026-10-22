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
using NativeEndpoints;
using System.Text.Json.Schema;

namespace Elsa.Activities.Design.Api.Endpoints.Definitions.AddDraft;

[Post("/design/activities/definitions/{definitionId}/drafts")]
[RequirePermission(ActivityDesignPermissions.Manage)]
[AuthoringProblems]
public sealed class Endpoint(IReusableActivityAuthoringService service) : ApiEndpoint<CreateReusableActivityDraftRequest, ReusableActivityDraftView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DefinitionsAddDraft";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentType;
        options.SuccessStatus = StatusCodes.Status201Created;
        options.Convention(builder => builder.AddOpenApiOperationTransformer(ConfigureOpenApiAsync));
    }

    public override async Task<ReusableActivityDraftView> HandleAsync(CreateReusableActivityDraftRequest request, CancellationToken cancellationToken)
    {
        var command = new CreateReusableActivityDraft(
            request.DefinitionId,
            request.SourceVersionId,
            request.Provider,
            request.Contract,
            request.Layout?.Records,
            request.PresentationLabel);
        var response = await service.CreateDraftAsync(command, cancellationToken);
        HttpContext.Response.Headers.Location = $"/{RouteConstants.GetRoute($"drafts/{response.DraftId}")}";
        return response;
    }

    private static async Task ConfigureOpenApiAsync(
        OpenApiOperation _,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var requestSchema = await context.GetOrCreateSchemaAsync(typeof(CreateReusableActivityDraftRequest), null, cancellationToken);
        if (requestSchema.Properties is null)
            return;

        var recordSchema = await context.GetOrCreateSchemaAsync(typeof(ActivityLayoutRecord), null, cancellationToken);
        requestSchema.Properties["layout"] = new OpenApiSchema
        {
            Type = JsonSchemaType.Array | JsonSchemaType.Null,
            Items = recordSchema
        };

        var document = context.Document ?? throw new InvalidOperationException("The OpenAPI document is unavailable.");
        var components = document.Components ??= new OpenApiComponents();
        var schemas = components.Schemas ??= new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
        schemas[nameof(CreateReusableActivityDraftRequest)] = requestSchema;
    }
}
