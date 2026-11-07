using System.Net;
using System.Text.Json;
using Elsa.Jobs.Api.Authorization;
using Elsa.Jobs.Core;
using static Elsa.Jobs.Api.Tests.JobsApiSecurityTests;

namespace Elsa.Jobs.Api.Tests;

public sealed class JobsApiPayloadJsonTests
{
    [Fact]
    public async Task Authorized_raw_input_schedule_and_result_values_survive_http_sqlite_and_host_restart_without_number_conversion()
    {
        const string raw = """{"large":9007199254740993,"overflow":1e309,"precise":1.0000000000000001,"tiny":1e-400,"zero":-0,"text":"9007199254740993"}""";
        var directory = Directory.CreateTempSubdirectory("elsa-jobs-api-json-").FullName;
        string jobId;
        try
        {
            using var document = JsonDocument.Parse(raw);
            await using (var host = await JobsApiHost.StartAsync(storageDirectory: directory))
            {
                host.SignIn();
                var request = JobsApiHost.Request() with { Input = new(JobsApiHost.Handler.InputSchema, document.RootElement) };
                var accepted = await host.PostAsync("/jobs", new { idempotencyKey = "numeric-input", invocation = Invocation(request) });
                Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
                jobId = (await Json(accepted)).GetProperty("jobId").GetString()!;
                var schedule = new { invocation = Invocation(request), recurrence = new JobRecurrence(JobRecurrenceKind.Interval, "PT1H"),
                    firstOccurrence = DateTimeOffset.UtcNow.AddDays(1), missedPolicy = JobMissedOccurrencePolicy.CoalesceOne,
                    expectedRevision = 0, operationId = "numeric-schedule", reason = "Retain exact numeric input" };
                Assert.Equal(HttpStatusCode.OK, (await host.PutAsync("/jobs/schedules/numeric", schedule)).StatusCode);
                await host.AuthorityAsync(async (commands, _) =>
                {
                    var lease = (await commands.ClaimAsync(new(new(JobsApiHost.Partition, "tenant-a"), "numeric-worker", ["reports"],
                        [JobsApiHost.Handler], TimeSpan.FromMinutes(1), "numeric-claim"))).Lease!;
                    var payload = new JobPayload(JobsApiHost.Handler.ResultSchema, document.RootElement);
                    return await commands.CompleteAsync(lease, new("numeric-receipt", lease.AttemptId, lease.Owner, lease.Fence,
                        JobAttemptOutcome.Succeeded, payload, JobPayloadDigest.Compute(payload), null, false, DateTimeOffset.UtcNow));
                });
            }
            await using var restarted = await JobsApiHost.StartAsync(storageDirectory: directory); restarted.SignIn();
            foreach (var path in new[] { $"/jobs/{jobId}/input", $"/jobs/{jobId}/result", "/jobs/schedules/numeric/input" })
            {
                var response = await restarted.Client.GetAsync(path); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal(raw, (await Json(response)).GetProperty("valueJson").GetString());
            }
            var metadata = await restarted.Client.GetAsync($"/jobs/{jobId}");
            Assert.DoesNotContain("9007199254740993", await metadata.Content.ReadAsStringAsync());
            restarted.Authorization.Granted.Remove(JobsPermissions.InputsRead);
            restarted.Authorization.Granted.Remove(JobsPermissions.ResultsRead);
            foreach (var path in new[] { $"/jobs/{jobId}/input", $"/jobs/{jobId}/result", "/jobs/schedules/numeric/input" })
            {
                var denied = await restarted.Client.GetAsync(path); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
                Assert.DoesNotContain("9007199254740993", await denied.Content.ReadAsStringAsync());
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
