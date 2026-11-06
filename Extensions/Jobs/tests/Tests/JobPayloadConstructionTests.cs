using System.Text.Json;
using Elsa.Jobs.Core;
using Xunit;

namespace Elsa.Jobs.Tests;

public class JobPayloadConstructionTests
{
    [Fact]
    public void An_absent_json_value_is_a_caller_argument_error_before_clone()
    {
        var error = Assert.Throws<ArgumentException>(() => new JobPayload(new("report", 1), default));
        Assert.Equal("value", error.ParamName);
    }

    [Fact]
    public void Json_null_remains_a_defined_owned_payload_distinct_from_absence()
    {
        JobPayload payload;
        using (var document = JsonDocument.Parse("null"))
            payload = new(new("report", 1), document.RootElement);
        Assert.Equal(JsonValueKind.Null, payload.Value.ValueKind);
        Assert.NotEqual(JobPayloadDigest.Compute(null), JobPayloadDigest.Compute(payload));
    }
}
