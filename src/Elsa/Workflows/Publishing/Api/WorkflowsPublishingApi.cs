using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Constants;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Exceptions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NativeEndpoints;
using System.Text;

namespace Elsa.Workflows.Publishing.Api;

/// <summary>
/// Maps the Publishing REST surface using ordinary ASP.NET Core endpoints.
/// </summary>
/// <remarks>
/// This is the composition root only. Each endpoint declares its own route, contract, and permission
/// beside the handling it dispatches, under <c>Endpoints/</c>. The owner's serializer options carry
/// runtime configuration (camel-cased string enums), so the group receives a context constructed
/// over them, and the success content type is the module's published bare <c>application/json</c>.
/// </remarks>
public static class WorkflowsPublishingApi
{
    internal const string OwnerId = "Elsa.Workflows.Publishing.Api";

    public static void MapWorkflowsPublishingApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var api = endpoints.MapEndpointGroup(
            OwnerId,
            WorkflowsPublishingJsonOptions.WireContext,
            jsonContentType: "application/json");

        // Endpoint classes are scanned from this module's own assembly: each declares its route,
        // metadata, and permission on itself under Endpoints/<Resource>/<Operation>/Endpoint.cs.
        api.MapEndpointsFrom(typeof(WorkflowsPublishingApi).Assembly);

        // Export is intentionally a plain Minimal API operation. It returns bytes produced by a target rather
        // than a JSON response object, so it does not fit the request/response endpoint-class scanner's typed
        // operation shape. The producer and target remain replaceable through the engine/API feature seams.
        ElsaEndpointConventions.WithModuleOperation(
                endpoints.MapGet(RouteConstants.WorkflowExecutableExport, (RequestDelegate)HandleExportExecutableClosureAsync)
                    .WithEndpointGroup(OwnerId),
                "ElsaWorkflowsPublishingApiEndpointsExportWorkflowExecutableClosureEndpoint",
                OwnerId,
                typeof(WorkflowArtifactClosure))
            .RequirePermission(WorkflowPublishingPermissions.Read);
    }

    private static async Task HandleExportExecutableClosureAsync(HttpContext context)
    {
        var versionId = Route(context, "versionId");
        if (string.IsNullOrWhiteSpace(versionId))
        {
            await WriteProblemAsync(context,
                StatusCodes.Status404NotFound,
                "Cannot export a workflow definition version: no version was named.");
            return;
        }

        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(OwnerId);
        var target = context.RequestServices.GetServices<IWorkflowArtifactExportTarget>()
            .FirstOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.TargetId, DownloadWorkflowArtifactExportTarget.Id));
        if (target is null)
        {
            logger.LogError(
                "No export target named '{TargetId}' is registered; the publishing API feature must contribute it.",
                DownloadWorkflowArtifactExportTarget.Id);
            await WriteProblemAsync(context,
                StatusCodes.Status500InternalServerError,
                "Workflow artifact export is not available on this engine.");
            return;
        }

        WorkflowArtifactClosure closure;
        WorkflowArtifactExportDelivery delivery;
        try
        {
            closure = await context.RequestServices.GetRequiredService<IWorkflowArtifactClosureFactory>()
                .CreateAsync(versionId, context.RequestAborted);
            delivery = await DeliverAsync(target, closure, versionId, context.RequestAborted);
        }
        catch (WorkflowArtifactClosureSourceNotFoundException exception)
        {
            await WriteProblemAsync(context, StatusCodes.Status404NotFound, exception.Message);
            return;
        }
        catch (WorkflowArtifactClosureNotPublishedException exception)
        {
            await WriteProblemAsync(context, StatusCodes.Status409Conflict, exception.Message);
            return;
        }
        catch (IncompleteWorkflowArtifactClosureException exception)
        {
            await WriteProblemAsync(
                context,
                StatusCodes.Status409Conflict,
                exception.Message,
                exception.MissingArtifactIds
                    .Select(artifactId => $"Dependency artifact '{artifactId}' is missing from the executable store.")
                    .ToArray());
            return;
        }
        catch (WorkflowArtifactClosureCycleException exception)
        {
            logger.LogError(exception, "Corrupt executable dependency graph while exporting '{VersionId}'.", versionId);
            await WriteProblemAsync(context, StatusCodes.Status500InternalServerError, exception.Message);
            return;
        }
        catch (WorkflowArtifactClosureException exception)
        {
            logger.LogError(exception, "Workflow artifact export failed for '{VersionId}'.", versionId);
            await WriteProblemAsync(context, StatusCodes.Status500InternalServerError, exception.Message);
            return;
        }

        if (delivery.Kind != WorkflowArtifactExportDeliveryKind.InlinePayload || delivery.Payload is not { } payload)
        {
            logger.LogError(
                "Export target '{TargetId}' returned a '{Kind}' delivery; this route serves inline payloads only.",
                delivery.TargetId,
                delivery.Kind);
            await WriteProblemAsync(
                context,
                StatusCodes.Status500InternalServerError,
                "Workflow artifact export produced an unexpected delivery.");
            return;
        }

        context.Response.ContentType = "application/json";
        context.Response.ContentLength = payload.Length;
        context.Response.Headers.ContentDisposition = $"attachment; filename=\"{CreateFileName(closure)}\"";
        await context.Response.Body.WriteAsync(payload, context.RequestAborted);
    }

    private static async Task<WorkflowArtifactExportDelivery> DeliverAsync(
        IWorkflowArtifactExportTarget target,
        WorkflowArtifactClosure closure,
        string versionId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await target.DeliverAsync(closure, cancellationToken);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or WorkflowArtifactClosureException))
        {
            throw new WorkflowArtifactClosureStorageException(
                versionId,
                $"encode the exported closure envelope through target '{target.TargetId}'",
                exception);
        }
    }

    internal static string CreateFileName(WorkflowArtifactClosure closure)
    {
        ArgumentNullException.ThrowIfNull(closure);

        var root = closure.Artifacts.FirstOrDefault(artifact =>
            StringComparer.Ordinal.Equals(artifact.Identity.ArtifactId, closure.RootArtifactId));
        var definitionId = SafeNameSegment(root?.Identity.DefinitionId, "workflow");
        var artifactVersion = SafeNameSegment(root?.Identity.ArtifactVersion, "unversioned");
        return $"{definitionId}-{artifactVersion}-closure.json";
    }

    private static string SafeNameSegment(string? value, string fallback)
    {
        const int maximumLength = 96;
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            var safe = char.IsAsciiLetterOrDigit(character) || character is '.' or '_' ? character : '-';
            if (safe == '-' && builder.Length > 0 && builder[^1] == '-')
                continue;
            builder.Append(safe);
        }

        var sanitized = builder.ToString().Trim('-', '.');
        if (sanitized.Length > maximumLength)
            sanitized = sanitized[..maximumLength].TrimEnd('-', '.');
        return sanitized.Length == 0 ? fallback : sanitized;
    }

    private static string? Route(HttpContext context, string name) =>
        context.Request.RouteValues.TryGetValue(name, out var value) ? value?.ToString() : null;

    private static Task WriteProblemAsync(
        HttpContext context,
        int statusCode,
        string detail,
        IReadOnlyCollection<string>? additionalReasons = null)
    {
        var reasons = (additionalReasons ?? []).Concat([detail]).ToArray();
        var problem = new EndpointProblem(
            statusCode,
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["generalErrors"] = reasons
            });
        var writer = context.RequestServices.GetKeyedService<IEndpointProblemWriter>(OwnerId)
                     ?? context.RequestServices.GetRequiredService<IEndpointProblemWriter>();
        return writer.WriteAsync(context, problem);
    }
}
