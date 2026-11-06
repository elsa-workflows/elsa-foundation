using System.Net;
using System.Text.Json;
using Elsa.Jobs.Api.Authorization;
using Elsa.Jobs.Core;

namespace Elsa.Jobs.Api.Tests;

public sealed class JobsApiAuditTests
{
    [Fact]
    public async Task Retained_audit_shows_original_actor_reason_and_time_after_replay_and_host_restart()
    {
        var directory = Directory.CreateTempSubdirectory("elsa-jobs-api-audit-").FullName;
        string original;
        var command = new { gate = new { kind = "queue", name = "reports" }, holdId = "maintenance",
            operationId = "pause-confirmation", reason = "Investigate overnight report" };
        try
        {
            await using (var host = await JobsApiHost.StartAsync(directory))
            {
                host.SignIn();
                Assert.Equal(HttpStatusCode.OK, (await host.PostAsync("/jobs/holds", command)).StatusCode);
                original = await (await host.Client.GetAsync("/jobs/operations/pause-confirmation")).Content.ReadAsStringAsync();
                using var audit = JsonDocument.Parse(original);
                Assert.Equal("alice", audit.RootElement.GetProperty("actor").GetString());
                Assert.Equal(command.reason, audit.RootElement.GetProperty("reason").GetString());
                Assert.NotNull(audit.RootElement.GetProperty("recordedAt").GetString());
                Assert.Equal("pause", audit.RootElement.GetProperty("kind").GetString());
                Assert.Equal(HttpStatusCode.OK, (await host.PostAsync("/jobs/holds", command)).StatusCode);
            }
            await using var restarted = await JobsApiHost.StartAsync(directory); restarted.SignIn();
            Assert.Equal(original, await (await restarted.Client.GetAsync("/jobs/operations/pause-confirmation")).Content.ReadAsStringAsync());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task Audit_reads_recheck_current_permission_target_access_and_tenant_without_exposing_payload()
    {
        await using var host = await JobsApiHost.StartAsync(); host.SignIn();
        await host.SeedAsync(input: new { Secret = "PROTECTED_INPUT_MUST_NOT_ENTER_AUDIT" });
        Assert.Equal(HttpStatusCode.OK, (await host.PostAsync("/jobs/job/cancel", new { operationId = "cancel-audit", reason = "Operator investigation" })).StatusCode);
        var path = "/jobs/operations/cancel-audit";
        var visible = await host.Client.GetAsync(path); Assert.Equal(HttpStatusCode.OK, visible.StatusCode);
        Assert.DoesNotContain("PROTECTED_INPUT_MUST_NOT_ENTER_AUDIT", await visible.Content.ReadAsStringAsync());
        host.Authorization.DeniedResources.Add(("job", "job"));
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync(path)).StatusCode);
        using var filtered = JsonDocument.Parse(await (await host.Client.GetAsync("/jobs/operations")).Content.ReadAsStringAsync());
        Assert.Empty(filtered.RootElement.GetProperty("items").EnumerateArray());
        host.Authorization.DeniedResources.Clear(); host.Authorization.Granted.Remove(JobsPermissions.AuditRead);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.GetAsync(path)).StatusCode);
        host.Authorization.Granted.Add(JobsPermissions.AuditRead); host.SignIn(tenant: "tenant-b");
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync(path)).StatusCode);
        host.SignIn(); host.Authorization.DeniedResources.Add(("operation-audit", "cancel-audit"));
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync(path)).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancel_origin_audit_requires_exact_origin_and_accepted_job_target_access(bool accepted)
    {
        await using var host = await JobsApiHost.StartAsync(); host.SignIn();
        var origin = new JobOrigin("native", "protected-owner", "protected-invocation", new('a', 64));
        var request = JobsApiHost.Request(); var digest = JobRequestDigest.Compute(request);
        var scope = new JobScope(JobsApiHost.Partition, "tenant-a");
        await host.AuthorityAsync(async (commands, _) =>
        {
            if (accepted) await commands.AdmitAsync(new(scope, "origin-job", "origin-key", digest, request, origin));
            return await commands.CancelOriginAsync(new(scope, origin, digest, new("origin-cancel", "system-owner", "Parent cancelled")));
        });
        const string path = "/jobs/operations/origin-cancel";
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync(path)).StatusCode);
        Assert.Contains(host.Authorization.Requests, r => r.Resource is JobApiResource { Kind: "job-origin", Origin: var selected } && selected == origin);
        if (accepted)
        {
            host.Authorization.DeniedResources.Add(("job", "origin-job"));
            Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync(path)).StatusCode);
            using var filtered = JsonDocument.Parse(await (await host.Client.GetAsync("/jobs/operations")).Content.ReadAsStringAsync());
            Assert.Empty(filtered.RootElement.GetProperty("items").EnumerateArray());
            host.Authorization.DeniedResources.Clear();
        }
        host.Authorization.DeniedResources.Add(("job-origin", origin.OriginId));
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync(path)).StatusCode);
        var denied = await (await host.Client.GetAsync("/jobs/operations")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("protected-owner", denied); Assert.DoesNotContain("origin-job", denied);
    }

}
