using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Elsa.Server.ExtensionBuilder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Elsa.Server;

internal static class ElsaExtensionBuilderApi
{
    private const string ManagementApiKeyConfigurationKey = "Elsa:ModuleManagement:ApiKey";
    private const string ManagementApiKeyHeaderName = "X-Elsa-Module-Management-Key";
    private const string CallerItemKey = "__ElsaExtensionBuilderCaller";
    private const string ElsaIdentityRoleClaimType = "elsa.identity.role";

    public static IEndpointRouteBuilder MapElsaExtensionBuilderApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/_elsa/extension-builder");
        group.AddEndpointFilter(RequireManagementApiKeyAsync);

        group.MapGet("/capabilities", GetCapabilities);

        var trusted = group.MapGroup("");
        trusted.AddEndpointFilter(RequireTrustedCallerAsync);
        trusted.MapGet("/templates", ListTemplatesAsync);
        trusted.MapGet("/repositories", ListRepositoriesAsync);
        trusted.MapPost("/repositories/server-local", AttachServerLocalRepositoryAsync);
        trusted.MapPost("/repositories/clone", CloneRepositoryAsync);
        trusted.MapPost("/workspaces", CreateWorkspaceAsync);
        trusted.MapGet("/workspaces", ListWorkspacesAsync);
        trusted.MapGet("/workspaces/{workspaceId}", GetWorkspaceAsync);
        trusted.MapDelete("/workspaces/{workspaceId}", DeleteWorkspaceAsync);
        trusted.MapGet("/workspaces/{workspaceId}/working-copies", ListWorkingCopiesAsync);
        trusted.MapPost("/workspaces/{workspaceId}/working-copies/select", SelectWorkingCopyAsync);
        trusted.MapGet("/workspaces/{workspaceId}/repository-tree", GetRepositoryTreeAsync);
        trusted.MapGet("/workspaces/{workspaceId}/files/{*path}", ReadRepositoryFileAsync);
        trusted.MapPut("/workspaces/{workspaceId}/files/{*path}", WriteRepositoryFileAsync);
        trusted.MapPost("/workspaces/{workspaceId}/files/move", MoveRepositoryFileAsync);
        trusted.MapDelete("/workspaces/{workspaceId}/files/{*path}", DeleteRepositoryFileAsync);
        trusted.MapPost("/workspaces/{workspaceId}/projects", CreateProjectAsync);
        trusted.MapGet("/projects/{projectId}", GetProjectAsync);
        trusted.MapDelete("/projects/{projectId}", DeleteProjectAsync);
        trusted.MapGet("/projects/{projectId}/files", ListProjectFilesAsync);
        trusted.MapGet("/projects/{projectId}/files/{*path}", ReadProjectFileAsync);
        trusted.MapPut("/projects/{projectId}/files/{*path}", WriteProjectFileAsync);
        trusted.MapDelete("/projects/{projectId}/files/{*path}", DeleteProjectFileAsync);
        trusted.MapPost("/projects/{projectId}/builds", SubmitBuildAsync);
        trusted.MapGet("/builds/{buildId}", GetBuildAsync);
        trusted.MapGet("/builds/{buildId}/log", GetBuildLogAsync);
        trusted.MapGet("/builds/{buildId}/artifact", GetBuildArtifactAsync);
        trusted.MapPost("/builds/{buildId}/promote", PromoteBuildAsync);
        trusted.MapGet("/projects/{projectId}/runtime-status", GetRuntimeStatusAsync);
        trusted.MapPost("/projects/{projectId}/rollback", RollbackPackageAsync);
        trusted.MapPost("/projects/{projectId}/retry-reconcile", RetryReconciliationAsync);

        return endpoints;
    }

    internal static IResult GetCapabilities(HttpContext httpContext, [FromServices] IExtensionBuilderService service) =>
        Results.Ok(service.GetCapabilities(GetCaller(httpContext)));

    private static async Task<IResult> ListTemplatesAsync([FromServices] IExtensionBuilderService service, CancellationToken cancellationToken) =>
        Results.Ok(await service.ListTemplatesAsync(cancellationToken));

    private static async Task<IResult> ListRepositoriesAsync(HttpContext httpContext, [FromServices] IExtensionBuilderService service, CancellationToken cancellationToken) =>
        Results.Ok(await service.ListRepositoriesAsync(GetCaller(httpContext), cancellationToken));

    private static async Task<IResult> CreateWorkspaceAsync(
        HttpContext httpContext,
        CreateWorkspaceRequest request,
        [FromServices] IExtensionBuilderService service,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await service.CreateWorkspaceAsync(GetCaller(httpContext), request, cancellationToken));
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new ExtensionBuilderErrorResponse(ex.Message));
        }
    }

    private static async Task<IResult> AttachServerLocalRepositoryAsync(
        HttpContext httpContext,
        AttachServerLocalRepositoryRequest request,
        [FromServices] IExtensionBuilderService service,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await service.AttachServerLocalRepositoryAsync(GetCaller(httpContext), request, cancellationToken));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Results.Json(new ExtensionBuilderErrorResponse(ex.Message), statusCode: StatusCodes.Status403Forbidden);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new ExtensionBuilderErrorResponse(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new ExtensionBuilderErrorResponse(ex.Message));
        }
    }

    private static async Task<IResult> CloneRepositoryAsync(
        HttpContext httpContext,
        CloneRepositoryRequest request,
        [FromServices] IExtensionBuilderService service,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await service.CloneRepositoryAsync(GetCaller(httpContext), request, cancellationToken));
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new ExtensionBuilderErrorResponse(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new ExtensionBuilderErrorResponse(ex.Message));
        }
    }

    private static async Task<IResult> ListWorkspacesAsync(HttpContext httpContext, [FromServices] IExtensionBuilderService service, CancellationToken cancellationToken) =>
        Results.Ok(await service.ListWorkspacesAsync(GetCaller(httpContext), cancellationToken));

    private static async Task<IResult> GetWorkspaceAsync(string workspaceId, HttpContext httpContext, [FromServices] IExtensionBuilderService service, CancellationToken cancellationToken) =>
        ToFoundResult(await service.GetWorkspaceAsync(GetCaller(httpContext), workspaceId, cancellationToken), $"Workspace '{workspaceId}' was not found.");

    private static async Task<IResult> DeleteWorkspaceAsync(string workspaceId, HttpContext httpContext, [FromServices] IExtensionBuilderService service, CancellationToken cancellationToken) =>
        await service.DeleteWorkspaceAsync(GetCaller(httpContext), workspaceId, cancellationToken)
            ? Results.Ok(new ExtensionBuilderDeleteResponse(true, "Workspace authoring state deleted. Promoted packages are unchanged."))
            : Results.NotFound(new ExtensionBuilderErrorResponse($"Workspace '{workspaceId}' was not found."));

    private static async Task<IResult> ListWorkingCopiesAsync(
        string workspaceId,
        [FromQuery] string? sessionId,
        HttpContext httpContext,
        [FromServices] IExtensionBuilderService service,
        CancellationToken cancellationToken) =>
        ToFoundResult(await service.ListWorkingCopiesAsync(GetCaller(httpContext), workspaceId, sessionId, cancellationToken), $"Workspace '{workspaceId}' was not found.");

    private static async Task<IResult> SelectWorkingCopyAsync(
        string workspaceId,
        HttpContext httpContext,
        SelectWorkingCopyRequest request,
        [FromServices] IExtensionBuilderService service,
        CancellationToken cancellationToken)
    {
        try
        {
            return ToFoundResult(await service.SelectWorkingCopyAsync(GetCaller(httpContext), workspaceId, request, cancellationToken), $"Workspace '{workspaceId}' was not found.");
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new ExtensionBuilderErrorResponse(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new ExtensionBuilderErrorResponse(ex.Message));
        }
    }

    private static async Task<IResult> GetRepositoryTreeAsync(
        string workspaceId,
        [FromQuery] string? solutionPath,
        HttpContext httpContext,
        [FromServices] IExtensionBuilderService service,
        CancellationToken cancellationToken)
    {
        try
        {
            return ToFoundResult(await service.GetRepositoryTreeAsync(GetCaller(httpContext), workspaceId, solutionPath, cancellationToken), $"Workspace '{workspaceId}' was not found.");
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new ExtensionBuilderErrorResponse(ex.Message));
        }
    }

    private static async Task<IResult> ReadRepositoryFileAsync(string workspaceId, string path, HttpContext httpContext, [FromServices] IExtensionBuilderService service, CancellationToken cancellationToken)
    {
        try
        {
            return ToFoundResult(await service.ReadRepositoryFileAsync(GetCaller(httpContext), workspaceId, path, cancellationToken), $"Repository file '{path}' was not found.");
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new ExtensionBuilderErrorResponse(ex.Message));
        }
    }

    private static async Task<IResult> WriteRepositoryFileAsync(string workspaceId, string path, HttpContext httpContext, WriteProjectFileRequest request, [FromServices] IExtensionBuilderService service, CancellationToken cancellationToken)
    {
        try
        {
            return ToFoundResult(await service.WriteRepositoryFileAsync(GetCaller(httpContext), workspaceId, path, request, cancellationToken), $"Workspace '{workspaceId}' was not found.");
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new ExtensionBuilderErrorResponse(ex.Message));
        }
    }

    private static async Task<IResult> MoveRepositoryFileAsync(string workspaceId, HttpContext httpContext, MoveRepositoryFileRequest request, [FromServices] IExtensionBuilderService service, CancellationToken cancellationToken)
    {
        try
        {
            return ToFoundResult(await service.MoveRepositoryFileAsync(GetCaller(httpContext), workspaceId, request, cancellationToken), $"Repository file '{request.SourcePath}' was not found.");
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new ExtensionBuilderErrorResponse(ex.Message));
        }
        catch (IOException ex)
        {
            return Results.Conflict(new ExtensionBuilderErrorResponse(ex.Message));
        }
    }

    private static async Task<IResult> DeleteRepositoryFileAsync(string workspaceId, string path, HttpContext httpContext, [FromServices] IExtensionBuilderService service, CancellationToken cancellationToken)
    {
        try
        {
            return await service.DeleteRepositoryFileAsync(GetCaller(httpContext), workspaceId, path, cancellationToken)
                ? Results.Ok(new ExtensionBuilderDeleteResponse(true, "Repository file deleted."))
                : Results.NotFound(new ExtensionBuilderErrorResponse($"Repository file '{path}' was not found."));
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new ExtensionBuilderErrorResponse(ex.Message));
        }
    }

    private static async Task<IResult> CreateProjectAsync(
        string workspaceId,
        HttpContext httpContext,
        CreateProjectRequest request,
        [FromServices] IExtensionBuilderService service,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await service.CreateProjectAsync(GetCaller(httpContext), workspaceId, request, cancellationToken));
        }
        catch (KeyNotFoundException ex)
        {
            return Results.NotFound(new ExtensionBuilderErrorResponse(ex.Message));
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new ExtensionBuilderErrorResponse(ex.Message));
        }
    }

    private static async Task<IResult> GetProjectAsync(string projectId, HttpContext httpContext, [FromServices] IExtensionBuilderService service, CancellationToken cancellationToken) =>
        ToFoundResult(await service.GetProjectAsync(GetCaller(httpContext), projectId, cancellationToken), $"Project '{projectId}' was not found.");

    private static async Task<IResult> DeleteProjectAsync(string projectId, HttpContext httpContext, [FromServices] IExtensionBuilderService service, CancellationToken cancellationToken) =>
        await service.DeleteProjectAsync(GetCaller(httpContext), projectId, cancellationToken)
            ? Results.Ok(new ExtensionBuilderDeleteResponse(true, "Project authoring state deleted. Promoted packages are unchanged."))
            : Results.NotFound(new ExtensionBuilderErrorResponse($"Project '{projectId}' was not found."));

    private static async Task<IResult> ListProjectFilesAsync(string projectId, HttpContext httpContext, [FromServices] IExtensionBuilderService service, CancellationToken cancellationToken) =>
        ToFoundResult(await service.ListProjectFilesAsync(GetCaller(httpContext), projectId, cancellationToken), $"Project '{projectId}' was not found.");

    private static async Task<IResult> ReadProjectFileAsync(string projectId, string path, HttpContext httpContext, [FromServices] IExtensionBuilderService service, CancellationToken cancellationToken)
    {
        try
        {
            return ToFoundResult(await service.ReadProjectFileAsync(GetCaller(httpContext), projectId, path, cancellationToken), $"Project file '{path}' was not found.");
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new ExtensionBuilderErrorResponse(ex.Message));
        }
    }

    private static async Task<IResult> WriteProjectFileAsync(string projectId, string path, HttpContext httpContext, WriteProjectFileRequest request, [FromServices] IExtensionBuilderService service, CancellationToken cancellationToken)
    {
        try
        {
            return ToFoundResult(await service.WriteProjectFileAsync(GetCaller(httpContext), projectId, path, request, cancellationToken), $"Project '{projectId}' was not found.");
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new ExtensionBuilderErrorResponse(ex.Message));
        }
    }

    private static async Task<IResult> DeleteProjectFileAsync(string projectId, string path, HttpContext httpContext, [FromServices] IExtensionBuilderService service, CancellationToken cancellationToken)
    {
        try
        {
            return await service.DeleteProjectFileAsync(GetCaller(httpContext), projectId, path, cancellationToken)
                ? Results.Ok(new ExtensionBuilderDeleteResponse(true, "Project file deleted."))
                : Results.NotFound(new ExtensionBuilderErrorResponse($"Project file '{path}' was not found."));
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new ExtensionBuilderErrorResponse(ex.Message));
        }
    }

    private static async Task<IResult> SubmitBuildAsync(string projectId, HttpContext httpContext, [FromServices] IExtensionBuilderService service, CancellationToken cancellationToken) =>
        ToFoundResult(await service.SubmitBuildAsync(GetCaller(httpContext), projectId, cancellationToken), $"Project '{projectId}' was not found.");

    private static async Task<IResult> GetBuildAsync(string buildId, HttpContext httpContext, [FromServices] IExtensionBuilderService service, CancellationToken cancellationToken) =>
        ToFoundResult(await service.GetBuildAsync(GetCaller(httpContext), buildId, cancellationToken), $"Build '{buildId}' was not found.");

    private static async Task<IResult> GetBuildLogAsync(string buildId, HttpContext httpContext, [FromServices] IExtensionBuilderService service, CancellationToken cancellationToken)
    {
        var log = await service.GetBuildLogAsync(GetCaller(httpContext), buildId, cancellationToken);
        return log is null
            ? Results.NotFound(new ExtensionBuilderErrorResponse($"Build log for '{buildId}' was not found."))
            : Results.Text(log, "text/plain");
    }

    private static async Task<IResult> GetBuildArtifactAsync(string buildId, HttpContext httpContext, [FromServices] IExtensionBuilderService service, CancellationToken cancellationToken)
    {
        var artifact = await service.GetBuildArtifactAsync(GetCaller(httpContext), buildId, cancellationToken);
        return artifact is null
            ? Results.NotFound(new ExtensionBuilderErrorResponse($"Build artifact for '{buildId}' was not found."))
            : Results.File(artifact.Path, "application/octet-stream", artifact.FileName);
    }

    private static async Task<IResult> PromoteBuildAsync(string buildId, HttpContext httpContext, [FromServices] IExtensionBuilderService service, CancellationToken cancellationToken) =>
        ToFoundResult(await service.PromoteBuildAsync(GetCaller(httpContext), buildId, cancellationToken), $"Build '{buildId}' was not found.");

    private static async Task<IResult> GetRuntimeStatusAsync(string projectId, HttpContext httpContext, [FromServices] IExtensionBuilderService service, CancellationToken cancellationToken) =>
        ToFoundResult(await service.GetRuntimeStatusAsync(GetCaller(httpContext), projectId, cancellationToken), $"Project '{projectId}' was not found.");

    private static async Task<IResult> RollbackPackageAsync(string projectId, HttpContext httpContext, RollbackPackageRequest request, [FromServices] IExtensionBuilderService service, CancellationToken cancellationToken) =>
        ToFoundResult(await service.RollbackPackageAsync(GetCaller(httpContext), projectId, request, cancellationToken), $"Project '{projectId}' was not found.");

    private static async Task<IResult> RetryReconciliationAsync(string projectId, HttpContext httpContext, [FromServices] IExtensionBuilderService service, CancellationToken cancellationToken) =>
        ToFoundResult(await service.RetryReconciliationAsync(GetCaller(httpContext), projectId, cancellationToken), $"Project '{projectId}' was not found.");

    internal static ValueTask<object?> RequireManagementApiKeyAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var configuredApiKey = configuration[ManagementApiKeyConfigurationKey];
        if (string.IsNullOrWhiteSpace(configuredApiKey))
            return new ValueTask<object?>(Results.NotFound());

        if (!context.HttpContext.Request.Headers.TryGetValue(ManagementApiKeyHeaderName, out var providedApiKeys) ||
            providedApiKeys.Count != 1 ||
            string.IsNullOrWhiteSpace(providedApiKeys[0]) ||
            !ApiKeysEqual(configuredApiKey, providedApiKeys[0]!))
            return new ValueTask<object?>(Results.Unauthorized());

        ApplyManagementApiKeyPrincipal(context.HttpContext);
        context.HttpContext.Items[CallerItemKey] = CreateCaller(context.HttpContext, hasManagementAccess: true);
        return next(context);
    }

    internal static ValueTask<object?> RequireTrustedCallerAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var caller = GetCaller(context.HttpContext);
        if (!caller.IsTrusted)
            return new ValueTask<object?>(Results.StatusCode(StatusCodes.Status403Forbidden));

        return next(context);
    }

    internal static ExtensionBuilderCaller CreateCaller(HttpContext httpContext, bool hasManagementAccess)
    {
        var options = httpContext.RequestServices.GetRequiredService<IOptions<ExtensionBuilderOptions>>().Value;
        var user = httpContext.User;
        var isAuthenticated = user.Identity?.IsAuthenticated == true;
        var isTrusted = isAuthenticated && options.TrustedRoles.Any(role => IsInRole(user, role));
        var ownerId = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue("sub")
            ?? user.Identity?.Name
            ?? "anonymous";
        return new(ownerId, user.Identity?.Name ?? ownerId, hasManagementAccess, isTrusted);
    }

    internal static void ApplyManagementApiKeyPrincipal(HttpContext httpContext)
    {
        var options = httpContext.RequestServices.GetRequiredService<IOptions<ExtensionBuilderOptions>>().Value;
        var trustedRole = options.TrustedRoles.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(trustedRole))
            return;

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "module-management"),
            new Claim(ClaimTypes.Name, "Module Management API Key"),
            new Claim(ClaimTypes.Role, trustedRole),
            new Claim(ElsaIdentityRoleClaimType, trustedRole)
        };
        var managementIdentity = new ClaimsIdentity(claims, "ElsaModuleManagementApiKey", ClaimTypes.Name, ClaimTypes.Role);
        httpContext.User = httpContext.User.Identity?.IsAuthenticated == true
            ? new ClaimsPrincipal(httpContext.User.Identities.Concat([managementIdentity]))
            : new ClaimsPrincipal(managementIdentity);
    }

    private static ExtensionBuilderCaller GetCaller(HttpContext httpContext)
    {
        if (httpContext.Items.TryGetValue(CallerItemKey, out var value) && value is ExtensionBuilderCaller caller)
            return caller;

        return CreateCaller(httpContext, hasManagementAccess: false);
    }

    private static IResult ToFoundResult<T>(T? value, string notFoundMessage) =>
        value is null ? Results.NotFound(new ExtensionBuilderErrorResponse(notFoundMessage)) : Results.Ok(value);

    private static bool IsInRole(ClaimsPrincipal user, string role) =>
        user.IsInRole(role) ||
        user.Claims.Any(claim =>
            claim.Type == ElsaIdentityRoleClaimType &&
            string.Equals(claim.Value, role, StringComparison.OrdinalIgnoreCase));

    private static bool ApiKeysEqual(string expected, string actual)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }
}
