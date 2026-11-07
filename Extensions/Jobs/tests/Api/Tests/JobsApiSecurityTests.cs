using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Elsa.Jobs.Api.Authorization;
using Elsa.Jobs.Api.Models;
using Elsa.Jobs.Api.Services;
using Elsa.Jobs.Core;
using Elsa.Api.AspNetCore;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Jobs.Api.Tests;

public sealed class JobsApiSecurityTests
{
    [Theory]
    [InlineData(null, 401)]
    [InlineData("untrusted", 403)]
    [InlineData("missing-user", 403)]
    [InlineData("duplicate-user", 403)]
    [InlineData("duplicate-tenant", 403)]
    public async Task Only_one_trusted_canonical_tenant_subject_enters_operations(string? variant, int expected)
    {
        await using var host = await JobsApiHost.StartAsync();
        await host.SeedAsync();
        if (variant is null)
        {
            Assert.Equal(24, host.Endpoints.Count);
            foreach (var endpoint in host.Endpoints)
            {
                Assert.Equal("Elsa.Jobs.Api", Assert.Single(endpoint.Metadata.GetOrderedMetadata<EndpointOwnershipMetadata>()).OwnerId);
                Assert.Equal(EndpointSecurityDispositionKind.Permission, Assert.Single(endpoint.Metadata.GetOrderedMetadata<EndpointSecurityDispositionMetadata>()).Kind);
                Assert.StartsWith("ElsaJobsApiEndpoints", endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName ?? "");
            }
        }
        if (variant is not null) host.SignIn(variant: variant);
        var result = await host.Client.GetAsync("/jobs/job");
        Assert.Equal(expected, (int)result.StatusCode);
        Assert.Empty(host.Authorization.Requests);
        Assert.Equal(JobState.Accepted, (await host.ReadAsync())!.State);
    }

    [Fact]
    public async Task Tenant_and_partition_are_server_owned_even_for_colliding_job_ids_and_foreign_cursors()
    {
        await using var host = await JobsApiHost.StartAsync();
        await host.SeedAsync("same", "tenant-a", new { Marker = "TENANT_A_PROTECTED" });
        await host.SeedAsync("next", "tenant-a"); await host.SeedAsync("same", "tenant-b", new { Marker = "TENANT_B_PROTECTED" });
        host.SignIn();
        var a = await host.Client.GetAsync("/jobs/same/input?tenantId=tenant-b&persistencePartition=foreign");
        Assert.Equal(HttpStatusCode.OK, a.StatusCode); var aText = await a.Content.ReadAsStringAsync();
        Assert.Contains("TENANT_A_PROTECTED", aText); Assert.DoesNotContain("TENANT_B_PROTECTED", aText);
        var page = await Json(await host.Client.GetAsync("/jobs?limit=1")); var next = page.GetProperty("next").GetString(); Assert.NotNull(next);
        host.SignIn(tenant: "tenant-b");
        var b = await host.Client.GetAsync("/jobs/same/input"); Assert.Contains("TENANT_B_PROTECTED", await b.Content.ReadAsStringAsync());
        var foreign = await host.Client.GetAsync("/jobs?limit=1&after=" + Uri.EscapeDataString(next!));
        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode); Assert.DoesNotContain("TENANT_A_PROTECTED", await foreign.Content.ReadAsStringAsync());
        host.SignIn(tenant: "tenant-c"); Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync("/jobs/same")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.PostAsync("/jobs/same/cancel", new { operationId = "foreign-cancel", reason = "Cannot operate another tenant" })).StatusCode);
        Assert.Equal(JobState.Accepted, (await host.ReadAsync("same", "tenant-a"))!.State);
        Assert.Equal(JobState.Accepted, (await host.ReadAsync("same", "tenant-b"))!.State);
        Assert.All(host.Authorization.Requests, request => Assert.DoesNotContain("foreign", request.TenantId));
    }

    [Fact]
    public async Task Denied_queue_page_boundary_is_encrypted_and_continuation_rechecks_current_resource_authorization()
    {
        const string denied = "A_DENIED_QUEUE_CURSOR_CANARY";
        await using var host = await JobsApiHost.StartAsync(); host.SignIn();
        await host.SeedAsync("a", queue: denied); await host.SeedAsync("b", queue: "B_VISIBLE"); await host.SeedAsync("c", queue: "C_VISIBLE");
        var provider = await host.AuthorityAsync(async (_, queries) => await queries.ListQueuesAsync(new(JobsApiHost.Partition, "tenant-a"), new(1, null)));
        Assert.Equal(denied, Assert.Single(provider.Items).Name);
        Assert.Contains(denied, Encoding.UTF8.GetString(Convert.FromBase64String(provider.ContinuationToken!)));
        host.Authorization.DeniedResources.Add(("queue", denied));
        var first = await host.Client.GetAsync("/jobs/queues?limit=1"); Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var text = await first.Content.ReadAsStringAsync(); var page = await Json(first);
        Assert.Empty(page.GetProperty("items").EnumerateArray());
        var next = page.GetProperty("next").GetString()!; Assert.NotNull(next);
        Assert.DoesNotContain(denied, text);
        Assert.DoesNotContain(denied, Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(next)));
        Assert.NotEqual(provider.ContinuationToken, next);
        var path = "/jobs/queues?limit=1&after=" + Uri.EscapeDataString(next);
        var allowed = await Json(await host.Client.GetAsync(path)); Assert.Equal("B_VISIBLE", allowed.GetProperty("items")[0].GetProperty("name").GetString());
        host.Authorization.DeniedResources.Add(("queue", "B_VISIBLE"));
        var revoked = await Json(await host.Client.GetAsync(path)); Assert.Empty(revoked.GetProperty("items").EnumerateArray());
        var final = await Json(await host.Client.GetAsync("/jobs/queues?limit=1&after=" + Uri.EscapeDataString(revoked.GetProperty("next").GetString()!)));
        Assert.Equal("C_VISIBLE", final.GetProperty("items")[0].GetProperty("name").GetString());
        host.Authorization.Granted.Remove(JobsPermissions.Read);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Protected_cursors_refuse_other_actor_scope_collection_filters_page_size_tampering_and_unbounded_input()
    {
        var directory = Directory.CreateTempSubdirectory("elsa-jobs-api-cursor-").FullName;
        try
        {
            await using var host = await JobsApiHost.StartAsync(storageDirectory: directory); host.SignIn();
            await host.SeedAsync("a", queue: "a"); await host.SeedAsync("b", queue: "b");
            var queues = await Json(await host.Client.GetAsync("/jobs/queues?limit=1")); var queueToken = queues.GetProperty("next").GetString()!;
            var escapedQueue = Uri.EscapeDataString(queueToken);
            foreach (var path in new[] { "/jobs/queues?limit=2", "/jobs/schedules?limit=1", "/jobs?limit=1", "/jobs/holds?limit=1", "/jobs/capacity?limit=1" })
                Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.GetAsync(path + "&after=" + escapedQueue)).StatusCode);
            var jobs = await Json(await host.Client.GetAsync("/jobs?limit=1")); var jobToken = Uri.EscapeDataString(jobs.GetProperty("next").GetString()!);
            foreach (var filter in new[] { "queue=a", "jobType=api.report", "state=accepted" })
                Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.GetAsync("/jobs?limit=1&" + filter + "&after=" + jobToken)).StatusCode);
            var provider = await host.AuthorityAsync(async (_, queries) => await queries.ListQueuesAsync(new(JobsApiHost.Partition, "tenant-a"), new(1, null)));
            var tampered = (queueToken[0] == 'A' ? "B" : "A") + queueToken[1..];
            foreach (var token in new[] { tampered, provider.ContinuationToken!, new string('a', JobsPageCursors.MaximumTokenLength + 1) })
                Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.GetAsync("/jobs/queues?limit=1&after=" + Uri.EscapeDataString(token))).StatusCode);
            host.SignIn("bob");
            Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.GetAsync("/jobs/queues?limit=1&after=" + escapedQueue)).StatusCode);
            host.SignIn(tenant: "tenant-b");
            Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.GetAsync("/jobs/queues?limit=1&after=" + escapedQueue)).StatusCode);
            // Same persisted host key ring and identity, but another host-selected logical partition.
            await using var otherPartition = await JobsApiHost.StartAsync(storageDirectory: directory,
                configure: services => services.Configure<JobsApiOptions>(options => options.PersistencePartition = "other-partition"));
            otherPartition.SignIn();
            Assert.Equal(HttpStatusCode.BadRequest, (await otherPartition.Client.GetAsync("/jobs/queues?limit=1&after=" + escapedQueue)).StatusCode);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task Metadata_omits_payload_progress_and_raw_diagnostics_while_payload_permissions_are_current()
    {
        await using var host = await JobsApiHost.StartAsync(); host.SignIn();
        await host.SeedAsync(input: new { Protected = "INPUT_PROTECTED_CANARY" });
        await host.AuthorityAsync(async (commands, _) =>
        {
            var lease = (await commands.ClaimAsync(new(new(JobsApiHost.Partition, "tenant-a"), "worker", ["reports"], [JobsApiHost.Handler], TimeSpan.FromMinutes(1), "claim"))).Lease!;
            await commands.RecordProgressAsync(lease, new(.5, "PROGRESS_PROTECTED_CANARY", new(new("progress", 1), JsonSerializer.SerializeToElement(new { Protected = "PROGRESS_DATA_CANARY" }))));
            var payload = new JobPayload(JobsApiHost.Handler.ResultSchema, JsonSerializer.SerializeToElement(new { Total = 52, Protected = "RESULT_PROTECTED_CANARY" }));
            return await commands.CompleteAsync(lease, new("receipt", lease.AttemptId, lease.Owner, lease.Fence,
                JobAttemptOutcome.Succeeded, payload, JobPayloadDigest.Compute(payload), new("DIAGNOSTIC_CODE_CANARY", "DIAGNOSTIC_SUMMARY_CANARY"), false, DateTimeOffset.UtcNow));
        });
        foreach (var path in new[] { "/jobs", "/jobs/job", "/jobs/job/attempts", "/jobs/job/physical-receipts" })
        {
            var response = await host.Client.GetAsync(path); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var text = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("CANARY", text); Assert.DoesNotContain("INPUT_PROTECTED", text); Assert.DoesNotContain("RESULT_PROTECTED", text);
        }
        host.Authorization.Granted.Remove(JobsPermissions.InputsRead); host.Authorization.Granted.Remove(JobsPermissions.ResultsRead);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.GetAsync("/jobs/job/input")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.GetAsync("/jobs/job/result")).StatusCode);
        host.Authorization.Granted.Add(JobsPermissions.ResultsRead);
        var result = await Json(await host.Client.GetAsync("/jobs/job/result"));
        Assert.Equal(52, result.GetProperty("payload").GetProperty("value").GetProperty("Total").GetInt32());
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.GetAsync("/jobs/job/input")).StatusCode);
    }

    [Fact]
    public async Task Resource_denial_is_indistinguishable_from_missing_and_revoked_actions_do_not_mutate()
    {
        await using var host = await JobsApiHost.StartAsync(); host.SignIn(); await host.SeedAsync();
        host.Authorization.DenyResources = true;
        var denied = await host.Client.GetAsync("/jobs/job"); var missing = await host.Client.GetAsync("/jobs/missing");
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode); Assert.Equal(await missing.Content.ReadAsStringAsync(), await denied.Content.ReadAsStringAsync());
        var page = await Json(await host.Client.GetAsync("/jobs")); Assert.Empty(page.GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await host.PostAsync("/jobs/job/cancel", new { operationId = "cancel-denied", reason = "Not allowed" })).StatusCode);
        host.Authorization.DenyResources = false;
        var before = await Json(await host.Client.GetAsync("/jobs/job")); Assert.Equal("accepted", before.GetProperty("state").GetString());
        host.Authorization.Granted.Remove(JobsPermissions.Cancel);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.PostAsync("/jobs/job/cancel", new { operationId = "cancel-revoked", reason = "Not allowed now" })).StatusCode);
        Assert.Equal(JobState.Accepted, (await host.ReadAsync())!.State);
        host.Authorization.Granted.Add(JobsPermissions.Cancel);
        Assert.Equal(HttpStatusCode.OK, (await host.PostAsync("/jobs/job/cancel", new { operationId = "cancel-authorized", reason = "Operator request" })).StatusCode);
        Assert.Equal(JobOutcome.Cancelled, (await host.ReadAsync())!.Result!.Outcome);
    }

    [Theory]
    [InlineData("scope")]
    [InlineData("actor")]
    public async Task Mutation_bodies_cannot_supply_scope_or_actor(string forged)
    {
        await using var host = await JobsApiHost.StartAsync(); host.SignIn(); await host.SeedAsync();
        var body = new Dictionary<string, object> { ["operationId"] = "forged-cancel", ["reason"] = "Operator request", [forged] = "tenant-b" };
        Assert.Equal(HttpStatusCode.BadRequest, (await host.PostAsync("/jobs/job/cancel", body)).StatusCode);
        Assert.Equal(JobState.Accepted, (await host.ReadAsync())!.State);
    }

    [Fact]
    public async Task Caller_model_association_is_refused_before_admission_and_plain_submission_replays()
    {
        await using var host = await JobsApiHost.StartAsync(); host.SignIn();
        var request = JobsApiHost.Request();
        var ordinary = Invocation(request);
        var forged = new { idempotencyKey = "forged", invocation = new
        {
            handler = ordinary.Handler, input = ordinary.Input, resultSchema = ordinary.ResultSchema,
            queue = ordinary.Queue, policy = ordinary.Policy, notBefore = ordinary.NotBefore,
            association = new { modelKind = "native", executionId = "other-owner" }
        } };
        Assert.Equal(HttpStatusCode.BadRequest, (await host.PostAsync("/jobs", forged)).StatusCode);
        var body = new { idempotencyKey = "submit", invocation = ordinary };
        var first = await host.PostAsync("/jobs", body); Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        var accepted = await Json(first); var id = accepted.GetProperty("jobId").GetString()!;
        var replay = await host.PostAsync("/jobs", body); Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        Assert.Equal(id, (await Json(replay)).GetProperty("jobId").GetString());
        var snapshot = await host.ReadAsync(id); Assert.Null(snapshot!.Request!.Association);
        var conflict = await host.PostAsync("/jobs", new { idempotencyKey = "submit", invocation = Invocation(request with { Queue = "changed" }) });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }

    [Fact]
    public async Task Identity_read_failure_is_an_unavailable_error_and_never_an_empty_success_or_raw_message()
    {
        await using var host = await JobsApiHost.StartAsync(); host.SignIn(); await host.SeedAsync(); host.Authorization.Throw = true;
        var response = await host.Client.GetAsync("/jobs"); Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("AUTHORIZATION_FAILURE_CANARY", await response.Content.ReadAsStringAsync());
        Assert.Equal(JobState.Accepted, (await host.ReadAsync())!.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Wire_bounds_cover_declared_length_and_nonseekable_streams_before_admission(bool chunked)
    {
        await using var host = await JobsApiHost.StartAsync(); host.SignIn();
        var body = "{\"idempotencyKey\":\"large\",\"invocation\":{\"input\":\"" + new string('x', (int)JobsApi.MaximumRequestBytes) + "\"}}";
        using HttpContent content = chunked ? new ChunkedJsonContent(body) : new StringContent(body, Encoding.UTF8, "application/json");
        var response = await host.Client.PostAsync("/jobs", content); Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        var page = await Json(await host.Client.GetAsync("/jobs")); Assert.Empty(page.GetProperty("items").EnumerateArray());
    }

    internal static JobInvocationRequest Invocation(JobRequest request) =>
        new(request.Handler, request.Input, request.ResultSchema, request.Queue, request.Policy, request.NotBefore);
    internal static async Task<JsonElement> Json(HttpResponseMessage response)
    { using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); return document.RootElement.Clone(); }

    private sealed class ChunkedJsonContent : HttpContent
    {
        private readonly byte[] _bytes;
        internal ChunkedJsonContent(string value)
        {
            _bytes = Encoding.UTF8.GetBytes(value); Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
        {
            for (var offset = 0; offset < _bytes.Length; offset += 4096)
                await stream.WriteAsync(_bytes.AsMemory(offset, Math.Min(4096, _bytes.Length - offset)));
        }
    }
}
