using System.Security.Claims;
using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Activities.Design.Import.Services;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Elsa3.Activities.Design.Import.Endpoints;

public sealed record ReusableActivityImportSelectionRequest(
    string PlanId,
    IReadOnlyCollection<string> SelectedSourceVersionIds);

public sealed record ReusableActivityImportApplyHttpRequest(
    string PlanId,
    IReadOnlyCollection<string> SelectedSourceVersionIds,
    string IdempotencyKey);

public sealed class UploadReusableActivityCollectionEndpoint(IReusableActivityImportOperationService service)
    : ElsaEndpointWithoutRequest<ReusableActivityImportUploadResult>
{
    public override void Configure()
    {
        Post("migration/elsa3/reusable-activities/collections");
        ConfigurePermissions(PermissionNames.Elsa3ImportManage);
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.UploadAsync(
                HttpContext.Request.Body,
                HttpContext.Request.ContentLength,
                ReusableActivityImportHttp.Scope(User),
                cancellationToken);
            HttpContext.Response.Headers.Location =
                $"/migration/elsa3/reusable-activities/collections/{Uri.EscapeDataString(result.CollectionHandle)}/analysis";
            await Send.ResponseAsync(result, StatusCodes.Status201Created, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await ReusableActivityImportHttp.WriteProblemAsync(HttpContext, exception, cancellationToken);
        }
    }
}

public sealed class AnalyzeReusableActivityCollectionEndpoint(
    IReusableActivityImportOperationService service,
    IOptions<ReusableActivityImportOptions> options)
    : ElsaEndpointWithoutRequest<ReusableActivityImportAnalysisPage>
{
    public override void Configure()
    {
        Get("migration/elsa3/reusable-activities/collections/{collectionHandle}/analysis");
        ConfigurePermissions(PermissionNames.Elsa3ImportRead);
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.AnalyzeAsync(
                Route<string>("collectionHandle")!,
                Query<int>("offset", false),
                Query<int?>("limit", false) ?? options.Value.DefaultPageSize,
                ReusableActivityImportHttp.Scope(User),
                cancellationToken);
            await Send.OkAsync(result, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await ReusableActivityImportHttp.WriteProblemAsync(HttpContext, exception, cancellationToken);
        }
    }
}

public sealed class ExpandReusableActivityImportSelectionEndpoint(IReusableActivityImportOperationService service)
    : ElsaEndpoint<ReusableActivityImportSelectionRequest, ReusableActivityImportSelectionReadiness>
{
    public override void Configure()
    {
        Post("migration/elsa3/reusable-activities/collections/{collectionHandle}/selection");
        ConfigurePermissions(PermissionNames.Elsa3ImportRead);
    }

    public override async Task HandleAsync(
        ReusableActivityImportSelectionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.ExpandSelectionAsync(
                Route<string>("collectionHandle")!,
                request.PlanId,
                request.SelectedSourceVersionIds,
                ReusableActivityImportHttp.Scope(User),
                cancellationToken);
            await Send.OkAsync(result, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await ReusableActivityImportHttp.WriteProblemAsync(HttpContext, exception, cancellationToken);
        }
    }
}

public sealed class ApplyReusableActivityImportEndpoint(IReusableActivityImportOperationService service)
    : ElsaEndpoint<ReusableActivityImportApplyHttpRequest, ReusableActivityImportReceipt>
{
    public override void Configure()
    {
        Post("migration/elsa3/reusable-activities/collections/{collectionHandle}/apply");
        ConfigurePermissions(PermissionNames.Elsa3ImportManage);
    }

    public override async Task HandleAsync(
        ReusableActivityImportApplyHttpRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.ApplyAsync(
                Route<string>("collectionHandle")!,
                request.PlanId,
                request.SelectedSourceVersionIds,
                request.IdempotencyKey,
                ReusableActivityImportHttp.Scope(User),
                cancellationToken);
            HttpContext.Response.Headers.Location =
                $"/migration/elsa3/reusable-activities/imports/{Uri.EscapeDataString(request.IdempotencyKey)}";
            await Send.ResponseAsync(
                result,
                result.Status == ReusableActivityImportReceiptStatus.Applied
                    ? StatusCodes.Status201Created
                    : StatusCodes.Status200OK,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await ReusableActivityImportHttp.WriteProblemAsync(HttpContext, exception, cancellationToken);
        }
    }
}

public sealed class GetReusableActivityImportStatusEndpoint(IReusableActivityImportOperationService service)
    : ElsaEndpointWithoutRequest<ReusableActivityImportReceipt>
{
    public override void Configure()
    {
        Get("migration/elsa3/reusable-activities/imports/{idempotencyKey}");
        ConfigurePermissions(PermissionNames.Elsa3ImportRead);
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.GetStatusAsync(
                Route<string>("idempotencyKey")!,
                ReusableActivityImportHttp.Scope(User),
                cancellationToken);
            await Send.OkAsync(result, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await ReusableActivityImportHttp.WriteProblemAsync(HttpContext, exception, cancellationToken);
        }
    }
}

public static class ReusableActivityImportHttp
{
    private const string ElsaTenantClaim = "elsa.identity.tenant_id";
    private const string ConventionalTenantClaim = "tenant_id";

    public static ReusableActivityImportAccessScope Scope(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? user.FindFirst("sub")?.Value
                     ?? user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userId))
            throw new ReusableActivityImportNotFoundException("The authorized Elsa 3 import caller has no stable user identity.");
        var tenantId = user.FindFirst(ElsaTenantClaim)?.Value
                       ?? user.FindFirst(ConventionalTenantClaim)?.Value;
        return new(tenantId, userId);
    }

    public static Task WriteProblemAsync(
        HttpContext context,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, code, title) = exception switch
        {
            ReusableActivityImportNotFoundException => (404, "elsa3.import.not-found", "Elsa 3 import resource not found"),
            ReusableActivityImportExpiredException => (410, "elsa3.import.collection-expired", "Elsa 3 import collection expired"),
            ReusableActivityImportIdempotencyConflictException => (409, "elsa3.import.idempotency-conflict", "Idempotency key conflict"),
            ReusableActivityImportCollisionException => (409, "elsa3.import.identity-collision", "Import identity collision"),
            ReusableActivityImportValidationException => (422, "elsa3.import.validation-failed", "Elsa 3 import validation failed"),
            ReusableActivityImportPayloadException => (400, "elsa3.import.payload-invalid", "Elsa 3 import payload invalid"),
            ArgumentException => (400, "elsa3.import.request-invalid", "Elsa 3 import request invalid"),
            _ => (500, "elsa3.import.unexpected", "Elsa 3 import failed")
        };
        var problem = new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Status = status,
            Type = $"https://elsa.dev/problems/{code}",
            Title = title,
            Detail = status == 500 ? "The Elsa 3 import could not be completed." : exception.Message,
            Instance = context.Request.Path
        };
        problem.Extensions["errorCode"] = code;
        if (exception is ReusableActivityImportValidationException validation)
            problem.Extensions["diagnostics"] = validation.Diagnostics;
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        return context.Response.WriteAsJsonAsync(problem, cancellationToken);
    }
}
