using Elsa.Api.Mediator;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Constants;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Api.Services;
using Elsa.Workflows.Design.Core.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Design.Api.Endpoints.Authoring;

/// <summary>
/// Authoring and expression-tooling endpoints.
/// </summary>
/// <remarks>
/// These operations resolve domain services directly rather than dispatching mediator requests, so
/// they use the group's inline-handler form. Binding, failure translation, and endpoint metadata are
/// shared with the mediator-mapped endpoints; only dispatch differs.
/// </remarks>
internal static class AuthoringEndpoints
{
    private static readonly string[] AcceptsJson = ["application/json"];

    public static void Map(ModuleEndpointGroup api)
    {
        // The status code is chosen by the resolution result, so this handler writes its own response.
        api.MapWritingHandler<ActivityInputOptionsRequest>(
                HttpMethods.Post, RouteConstants.ActivityInputOptions, "AuthoringResolveActivityInputOptions",
                async (context, request, cancellationToken) =>
                {
                    NoStore(context);
                    var result = await context.RequestServices.GetRequiredService<ActivityInputOptionsAuthoringService>()
                        .ResolveAsync(request.ActivityVersionId, request.InputName, request.NodeId, request.WorkflowState, cancellationToken);
                    await api.WriteJsonAsync(
                        context,
                        result.StatusCode == StatusCodes.Status200OK
                            ? new ActivityInputOptionsResponse(result.Options!)
                            : new ActivityInputOptionsResponse(Error: result.Error, Code: result.Code),
                        result.StatusCode);
                },
                responseType: typeof(ActivityInputOptionsResponse), accepts: AcceptsJson)
            .RequirePermission(WorkflowDesignPermissions.Read);

        api.MapHandler<AnalyzeScopedVariablesRequest, ScopedVariableAnalysisResponse>(
                HttpMethods.Post, RouteConstants.ScopedVariableAnalysis, "AuthoringAnalyzeScopedVariables",
                (context, request, _) =>
                {
                    // Reported through the shared translator, which maps ArgumentException to 400.
                    if (request.State is null)
                        throw new ArgumentException("A workflow definition state is required.");
                    if (request.NodeId is not null && string.IsNullOrWhiteSpace(request.NodeId))
                        throw new ArgumentException("The selected activity node id cannot be empty.");

                    var authoring = context.RequestServices.GetRequiredService<ScopedVariableAuthoringContract>();
                    var state = request.State.ToState();
                    return Task.FromResult(new ScopedVariableAnalysisResponse(
                        authoring.GetVisibleVariables(state, request.NodeId), authoring.GetShadowingWarnings(state)));
                },
                accepts: AcceptsJson)
            .RequirePermission(WorkflowDesignPermissions.Read);

        api.MapHandler<ExpressionToolingDescriptorsResponse>(
                HttpMethods.Get, RouteConstants.ExpressionToolingDescriptors, "AuthoringDescribeExpressionTooling",
                (context, _) =>
                {
                    NoStore(context);
                    return Task.FromResult(new ExpressionToolingDescriptorsResponse(DescribeProviders(context)));
                })
            .RequirePermission(WorkflowDesignPermissions.Read);

        api.MapHandler<ExpressionToolingContextRequest, ExpressionToolingContextResponse>(
                HttpMethods.Post, RouteConstants.ExpressionToolingContext, "AuthoringResolveExpressionToolingContext",
                async (context, request, _) =>
                {
                    NoStore(context);
                    return new(await ExpressionToolingApiHandlers.ResolveContextAsync(context, request));
                },
                accepts: AcceptsJson)
            .RequirePermission(WorkflowDesignPermissions.Read);

        api.MapHandler<ExpressionToolingContextRequest, ExpressionToolingOperationResponse<ExpressionToolingItems>>(
                HttpMethods.Post, RouteConstants.ExpressionToolingSymbols, "AuthoringSearchExpressionToolingSymbols",
                async (context, request, _) =>
                {
                    NoStore(context);
                    return new(await ExpressionToolingApiHandlers.SearchSymbolsAsync(context, request));
                },
                accepts: AcceptsJson)
            .RequirePermission(WorkflowDesignPermissions.Read);

        api.MapHandler<ExpressionToolingCompletionRequest, ExpressionToolingOperationResponse<ExpressionToolingItems>>(
                HttpMethods.Post, RouteConstants.ExpressionToolingCompletions, "AuthoringCompleteExpressionTooling",
                async (context, request, _) =>
                {
                    NoStore(context);
                    return new(await ExpressionToolingApiHandlers.CompleteAsync(context, request));
                },
                accepts: AcceptsJson)
            .RequirePermission(WorkflowDesignPermissions.Read);

        api.MapHandler<ExpressionToolingHoverRequest, ExpressionToolingOperationResponse<ExpressionHover>>(
                HttpMethods.Post, RouteConstants.ExpressionToolingHover, "AuthoringHoverExpressionTooling",
                async (context, request, _) =>
                {
                    NoStore(context);
                    return new(await ExpressionToolingApiHandlers.HoverAsync(context, request));
                },
                accepts: AcceptsJson)
            .RequirePermission(WorkflowDesignPermissions.Read);

        api.MapHandler<ExpressionToolingSourceRequest, ExpressionToolingOperationResponse<ExpressionDiagnosticSet>>(
                HttpMethods.Post, RouteConstants.ExpressionToolingValidate, "AuthoringValidateExpressionTooling",
                async (context, request, _) =>
                {
                    NoStore(context);
                    return new(await ExpressionToolingApiHandlers.ValidateAsync(context, request));
                },
                accepts: AcceptsJson)
            .RequirePermission(WorkflowDesignPermissions.Read);
    }

    private static void NoStore(HttpContext context) => context.Response.Headers.CacheControl = "no-store";

    private static ExpressionToolingOutcome<IReadOnlyList<ExpressionToolingDescriptor>> DescribeProviders(HttpContext context)
    {
        var providers = context.RequestServices.GetServices<IExpressionToolingProvider>()
            .OrderBy(provider => provider.ExpressionType, StringComparer.Ordinal)
            .Select(provider =>
            {
                var assembly = provider.GetType().Assembly.GetName();
                return new ExpressionToolingDescriptor(
                    provider.ExpressionType,
                    assembly.Name ?? provider.GetType().Namespace ?? provider.ExpressionType,
                    assembly.Version?.ToString() ?? "0.0.0.0",
                    provider.SupportedVersion,
                    provider.DeclaredCapabilities);
            })
            .ToArray();

        return providers.Length == 0
            ? ExpressionToolingOutcome<IReadOnlyList<ExpressionToolingDescriptor>>.SupportedEmpty(providers, ExpressionToolingContractVersion.V1, "descriptors", "descriptors")
            : ExpressionToolingOutcome<IReadOnlyList<ExpressionToolingDescriptor>>.Success(providers, ExpressionToolingContractVersion.V1, "descriptors", "descriptors");
    }
}
