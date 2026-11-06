using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Jobs.Api.Authorization;
using Elsa.Jobs.Api.Models;
using Elsa.Jobs.Api.Services;
using Elsa.Jobs.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NativeEndpoints;

namespace Elsa.Jobs.Api;

/// <summary>Maps the module-owned Jobs operations surface.</summary>
public static class JobsApi
{
    internal const string Owner = "Elsa.Jobs.Api";
    internal const long MaximumRequestBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static void MapJobsApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var api = endpoints.MapEndpointGroup(Owner, JobsJsonContext.Default, jsonContentType: "application/json");
        Map<JobListRequest, JobApiPage<JobView>>(api, "GET", "/jobs", "ListJobs", JobsPermissions.Read, (o, _, r, ct) => o.ListAsync(r, ct));
        Map<SubmitJobRequest, JobAcceptance>(api, "POST", "/jobs", "SubmitJob", JobsPermissions.Submit, (o, _, r, ct) => o.SubmitAsync(r, ct), 202);
        Map<JobPageQuery, JobApiPage<JobOperationAuditView>>(api, "GET", "/jobs/operations", "ListJobOperationAudits", JobsPermissions.AuditRead, (o, _, r, ct) => o.OperationAuditsAsync(r, ct));
        Map<NoJobRequest, JobOperationAuditView>(api, "GET", "/jobs/operations/{operationId}", "GetJobOperationAudit", JobsPermissions.AuditRead, (o, c, _, ct) => o.OperationAuditAsync(Id(c, "operationId"), ct));
        Map<NoJobRequest, JobApiAccessView>(api, "GET", "/jobs/access", "JobsAccess", null, (o, _, _, ct) => o.AccessAsync(ct));
        Map<JobPageQuery, JobApiPage<JobQueueSnapshot>>(api, "GET", "/jobs/queues", "ListJobQueues", JobsPermissions.Read, (o, _, r, ct) => o.QueuesAsync(r, ct));
        Map<JobPageQuery, JobApiPage<JobScheduleView>>(api, "GET", "/jobs/schedules", "ListJobSchedules", JobsPermissions.Read, (o, _, r, ct) => o.SchedulesAsync(r, ct));
        Map<NoJobRequest, JobScheduleView>(api, "GET", "/jobs/schedules/{scheduleId}", "GetJobSchedule", JobsPermissions.Read, (o, c, _, ct) => o.ScheduleAsync(Id(c, "scheduleId"), ct));
        Map<NoJobRequest, JobInputView>(api, "GET", "/jobs/schedules/{scheduleId}/input", "GetJobScheduleInput", JobsPermissions.InputsRead, (o, c, _, ct) => o.ScheduleInputAsync(Id(c, "scheduleId"), ct));
        Map<SaveJobScheduleRequest, JobTransition>(api, "PUT", "/jobs/schedules/{scheduleId}", "SaveJobSchedule", JobsPermissions.SchedulesManage, (o, c, r, ct) => o.SaveScheduleAsync(Id(c, "scheduleId"), r, ct));
        Map<EnableJobScheduleRequest, JobTransition>(api, "POST", "/jobs/schedules/{scheduleId}/enabled", "SetJobScheduleEnabled", JobsPermissions.SchedulesManage, (o, c, r, ct) => o.EnableScheduleAsync(Id(c, "scheduleId"), r, ct));
        Map<JobHoldQuery, JobApiPage<JobHold>>(api, "GET", "/jobs/holds", "ListJobHolds", JobsPermissions.Read, (o, _, r, ct) => o.HoldsAsync(r, ct));
        Map<PauseJobsRequest, JobTransition>(api, "POST", "/jobs/holds", "PauseJobs", JobsPermissions.Pause, (o, _, r, ct) => o.PauseAsync(r, ct));
        Map<ReleaseJobHoldRequest, JobTransition>(api, "POST", "/jobs/holds/{holdId}/release", "ReleaseJobHold", JobsPermissions.Pause, (o, c, r, ct) => o.ReleaseAsync(Id(c, "holdId"), r, ct));
        Map<JobPageQuery, JobApiPage<JobCapacity>>(api, "GET", "/jobs/capacity", "ListJobCapacity", JobsPermissions.Read, (o, _, r, ct) => o.CapacityAsync(r, ct));
        Map<SetJobCapacityRequest, JobTransition>(api, "PUT", "/jobs/capacity", "SetJobCapacity", JobsPermissions.CapacityManage, (o, _, r, ct) => o.SetCapacityAsync(r, ct));
        Map<NoJobRequest, JobView>(api, "GET", "/jobs/{jobId}", "GetJob", JobsPermissions.Read, (o, c, _, ct) => o.GetAsync(Id(c, "jobId"), ct));
        Map<NoJobRequest, JobInputView>(api, "GET", "/jobs/{jobId}/input", "GetJobInput", JobsPermissions.InputsRead, (o, c, _, ct) => o.InputAsync(Id(c, "jobId"), ct));
        Map<NoJobRequest, JobResultView>(api, "GET", "/jobs/{jobId}/result", "GetJobResult", JobsPermissions.ResultsRead, (o, c, _, ct) => o.ResultAsync(Id(c, "jobId"), ct));
        Map<JobPageQuery, JobApiPage<JobAttemptView>>(api, "GET", "/jobs/{jobId}/attempts", "ListJobAttempts", JobsPermissions.Read, (o, c, r, ct) => o.AttemptsAsync(Id(c, "jobId"), r, ct));
        Map<JobPageQuery, JobApiPage<JobPhysicalReceiptView>>(api, "GET", "/jobs/{jobId}/physical-receipts", "ListJobPhysicalReceipts", JobsPermissions.Read, (o, c, r, ct) => o.PhysicalReceiptsAsync(Id(c, "jobId"), r, ct));
        Map<JobPageQuery, JobHoldsView>(api, "GET", "/jobs/{jobId}/holds", "GetJobHolds", JobsPermissions.Read, (o, c, r, ct) => o.JobHoldsAsync(Id(c, "jobId"), r, ct));
        Map<JobOperationRequest, JobTransition>(api, "POST", "/jobs/{jobId}/cancel", "CancelJob", JobsPermissions.Cancel, (o, c, r, ct) => o.CancelAsync(Id(c, "jobId"), r, ct));
        Map<RetryJobRequest, JobAcceptance>(api, "POST", "/jobs/{jobId}/retry", "RetryJobAsNewRun", JobsPermissions.Retry, (o, c, r, ct) => o.RetryAsync(Id(c, "jobId"), r, ct), 202);
    }

    private static void Map<TRequest, TResponse>(EndpointGroup api, string method, string pattern, string operation,
        string? permission, Func<JobsOperations, HttpContext, TRequest, CancellationToken, ValueTask<TResponse>> handle, int success = 200)
    {
        var writes = method != "GET";
        var builder = api.MapOperation<TRequest>(new EndpointOperationDescriptor
        {
            Method = method, Pattern = pattern, Operation = operation, ResponseType = typeof(TResponse), SuccessStatus = success,
            BodyMode = writes ? EndpointBodyMode.RequiredWithContentTypeAndPayload : EndpointBodyMode.None,
            StrictTypedParsing = true, ContainFailures = false
        }, async (HttpContext context, TRequest request, CancellationToken ct) =>
        {
            try
            {
                var result = await handle(context.RequestServices.GetRequiredService<JobsOperations>(), context, request, ct);
                await Results.Json(result, JsonOptions, statusCode: success).ExecuteAsync(context);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception error) { await FailureAsync(context, error, writes); }
        }, null);
        builder.RequireAnyPermission(permission is null ? ["*", .. JobsPermissions.All] : ["*", permission]);
        builder.Finally(endpoint =>
        {
            var next = endpoint.RequestDelegate ?? throw new InvalidOperationException("Jobs endpoint dispatch is unavailable.");
            endpoint.RequestDelegate = async context =>
            {
                var original = context.Request.Body;
                try
                {
                    if (context.Request.ContentLength > MaximumRequestBytes)
                        throw new BadHttpRequestException("The Jobs request body exceeds its supported bound.", 413);
                    if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } size) size.MaxRequestBodySize = MaximumRequestBytes;
                    context.Request.Body = new BoundedJobRequestStream(original, MaximumRequestBytes);
                    await next(context);
                }
                catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { throw; }
                catch (Exception error) when (!context.Response.HasStarted) { await FailureAsync(context, error, writes); }
                finally { context.Request.Body = original; }
            };
        });
    }

    private static string Id(HttpContext context, string key) => context.Request.RouteValues.TryGetValue(key, out var value) && value is string id
        ? id : throw new ArgumentException("The Jobs resource identity is required.");

    private static Task FailureAsync(HttpContext context, Exception error, bool writes)
    {
        var (status, problem) = error switch
        {
            JobsApiException own => (own.Status, new JobsApiProblem(own.Code, own.Message)),
            BadHttpRequestException { StatusCode: 413 } => (413, new JobsApiProblem("jobs.request.too-large", "The Jobs request exceeds its supported wire bound.")),
            JobPersistenceException => (503, new JobsApiProblem(writes ? "jobs.operation.unconfirmed" : "jobs.persistence.unavailable",
                writes ? "The Jobs operation was not confirmed. Reconcile using its original identity." : "Jobs storage is unavailable.", writes ? "unknown" : null)),
            JobException { Code: "jobs.operation.conflict" or "jobs.submission.conflict" } => (409, new JobsApiProblem("jobs.operation.conflict", "The operation identity recognizes different content.")),
            JobException { Code: "jobs.recurrence.unavailable" or "jobs.persistence.invalid-record" } => (503, new JobsApiProblem("jobs.record.unavailable", "The requested Jobs state is unavailable.")),
            ArgumentException or JsonException or JobException => (400, new JobsApiProblem("jobs.request.invalid", "The Jobs request was refused. Check its bounded fields and schema.")),
            _ => (503, new JobsApiProblem("jobs.operations.unavailable", "Jobs operations are unavailable.", writes ? "unknown" : null))
        };
        return Results.Json(problem, JsonOptions, contentType: "application/problem+json", statusCode: status).ExecuteAsync(context);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = JobsJsonContext.Default, MaxDepth = 64 };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)); options.MakeReadOnly(); return options;
    }
}
