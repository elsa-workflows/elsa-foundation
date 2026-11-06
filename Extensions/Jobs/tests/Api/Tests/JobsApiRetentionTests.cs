using System.Net;
using System.Text.Json;
using Elsa.Jobs.Api.Authorization;
using Elsa.Jobs.Core;

namespace Elsa.Jobs.Api.Tests;

public sealed class JobsApiRetentionTests
{
    [Fact]
    public async Task Expired_payload_reads_keep_identity_authorization_and_explicit_retry_refusal()
    {
        await using var host = await JobsApiHost.StartAsync(persistence: options =>
        {
            options.TerminalInputRetention = TimeSpan.FromTicks(1);
            options.TerminalResultRetention = TimeSpan.FromTicks(1);
        });
        host.SignIn(); var accepted = await host.SeedAsync(input: new { Protected = "EXPIRED_INPUT_CANARY" });
        await host.AuthorityAsync(async (commands, _) => await commands.CancelAsync(new(new(JobsApiHost.Partition, "tenant-a"),
            accepted.JobId, new("finish-before-expiry", "model", "Complete the test invocation"))));
        var terminal = (await host.ReadAsync())!; var resultId = terminal.Result!.ResultId;
        for (var iteration = 0; iteration < 50; iteration++)
        {
            await host.AuthorityAsync(async (commands, _) => await commands.ExpirePayloadsAsync(new(new(JobsApiHost.Partition, "tenant-a"))));
            if ((await host.ReadAsync())!.InputPayloadState == JobPayloadState.Expired) break;
            await Task.Delay(10);
        }
        var expired = (await host.ReadAsync())!;
        Assert.Equal(JobPayloadState.Expired, expired.InputPayloadState); Assert.Equal(JobPayloadState.Expired, expired.ResultPayloadState);
        Assert.Equal(terminal.Revision, expired.Revision);
        var detail = await Read(host.Client.GetAsync("/jobs/job"));
        Assert.Equal("api.report", detail.GetProperty("handler").GetProperty("jobType").GetString());
        Assert.Equal("expired", detail.GetProperty("inputPayloadState").GetString());
        Assert.Equal(resultId, detail.GetProperty("resultId").GetString());
        var input = await Read(host.Client.GetAsync("/jobs/job/input"));
        Assert.Equal("expired", input.GetProperty("state").GetString()); Assert.Equal(JsonValueKind.Null, input.GetProperty("input").ValueKind);
        Assert.Equal(expired.RequestMetadata!.InputSha256, input.GetProperty("payloadSha256").GetString());
        Assert.DoesNotContain("EXPIRED_INPUT_CANARY", input.GetRawText());
        var result = await Read(host.Client.GetAsync("/jobs/job/result"));
        Assert.Equal("expired", result.GetProperty("state").GetString()); Assert.Equal(resultId, result.GetProperty("resultId").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("payload").ValueKind); Assert.Equal("cancelled", result.GetProperty("outcome").GetString());
        var retry = await host.PostAsync("/jobs/job/retry", new { expectedRevision = terminal.Revision, idempotencyKey = "after-expiry",
            operationId = "refused-expired-retry", reason = "Try the retained original input" });
        Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
        Assert.Equal("jobs.payload.expired", (await Json(retry)).GetProperty("code").GetString());
        host.Authorization.Granted.Remove(JobsPermissions.InputsRead);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.GetAsync("/jobs/job/input")).StatusCode);
        host.SignIn(tenant: "tenant-b");
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync("/jobs/job")).StatusCode);
    }

    private static async Task<JsonElement> Read(Task<HttpResponseMessage> response)
    {
        var actual = await response; Assert.Equal(HttpStatusCode.OK, actual.StatusCode); return await Json(actual);
    }
    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
}
