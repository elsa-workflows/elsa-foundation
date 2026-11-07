using System.Text;
using System.Text.Json;
using Elsa.Jobs.Core;
using Xunit;

namespace Elsa.Jobs.Tests;

public class JobPayloadLimitTests
{
    [Fact]
    public void Durable_payload_byte_count_includes_schema_and_fixed_json_escaping()
    {
        var payload = new JobPayload(new("report", 1), JsonSerializer.SerializeToElement("é<"));
        const string expected = """{"Schema":{"Alias":"report","Version":1},"Value":"\u00E9\u003C"}""";
        Assert.Equal(Encoding.UTF8.GetByteCount(expected), JobPayloadLimits.GetByteCount(payload));
        Assert.True(JobPayloadLimits.GetByteCount(payload) > Encoding.UTF8.GetByteCount(payload.Value.GetRawText()));
    }

    [Fact]
    public void One_mib_boundary_counts_the_envelope_instead_of_only_the_raw_value()
    {
        var schema = new JobSchema("report", 1);
        var overhead = JobPayloadLimits.GetByteCount(new(schema, JsonSerializer.SerializeToElement(string.Empty)));
        var exact = new JobPayload(schema, JsonSerializer.SerializeToElement(new string('x', JobPayloadLimits.MaximumBytes - overhead)));
        var oversized = new JobPayload(schema, JsonSerializer.SerializeToElement(new string('x', JobPayloadLimits.MaximumBytes - overhead + 1)));
        Assert.Equal(1_048_576, JobPayloadLimits.GetByteCount(exact));
        Assert.Equal(JobPayloadLimits.MaximumBytes + 1, JobPayloadLimits.GetByteCount(oversized));
        Assert.True(Encoding.UTF8.GetByteCount(oversized.Value.GetRawText()) < JobPayloadLimits.MaximumBytes);
    }

    [Fact]
    public void Payload_envelope_depth_32_fits_surrounding_request_and_receipt_while_depth_33_is_refused()
    {
        var exact = NestedPayload(JobPayloadLimits.MaximumDepth - 1);
        Assert.True(JobPayloadLimits.GetByteCount(exact) > 0);
        var request = Request(exact);
        var admission = new JobAdmission(new("jobs-tests", null), "job-one", "key-one", JobRequestDigest.Compute(request), request, null);
        var receipt = new JobPhysicalReceipt("receipt-one", "attempt-one", "worker-one", 1, JobAttemptOutcome.Succeeded,
            exact, JobPayloadDigest.Compute(exact), null, false, DateTimeOffset.UnixEpoch);
        Assert.NotEmpty(JsonSerializer.SerializeToUtf8Bytes(admission));
        Assert.NotEmpty(JsonSerializer.SerializeToUtf8Bytes(receipt));
        var error = Assert.Throws<JobException>(() => JobPayloadLimits.GetByteCount(NestedPayload(JobPayloadLimits.MaximumDepth)));
        Assert.Equal("jobs.payload.invalid", error.Code);
        Assert.IsAssignableFrom<JsonException>(error.InnerException);
    }

    [Fact]
    public void Deep_serialization_failures_are_safe_domain_exceptions_with_original_json_causes()
    {
        var payload = NestedPayload(80);
        var errors = new[]
        {
            Assert.Throws<JobException>(() => JobPayloadLimits.GetByteCount(payload)),
            Assert.Throws<JobException>(() => JobPayloadDigest.Compute(payload)),
            Assert.Throws<JobException>(() => JobRequestDigest.Compute(Request(payload)))
        };
        Assert.All(errors, error =>
        {
            Assert.IsAssignableFrom<JsonException>(error.InnerException);
            Assert.DoesNotContain("deep-secret-canary", error.Message);
        });
        Assert.Throws<ArgumentNullException>(() => JobPayloadLimits.GetByteCount(null!));
    }

    private static JobPayload NestedPayload(int arrays)
    {
        using var document = JsonDocument.Parse(new string('[', arrays) + "\"deep-secret-canary\"" + new string(']', arrays),
            new JsonDocumentOptions { MaxDepth = 128 });
        return new(new("report", 1), document.RootElement);
    }

    private static JobRequest Request(JobPayload payload) => new(new("reports.generate", "1"), payload, new("result", 1), "default",
        new(new(3, TimeSpan.Zero, TimeSpan.Zero, 1, 0, false), TimeSpan.FromMinutes(1), TimeSpan.FromDays(30)), null, null);
}
