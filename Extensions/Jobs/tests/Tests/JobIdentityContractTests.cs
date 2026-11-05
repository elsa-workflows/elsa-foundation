using System.Text.Json;
using Elsa.Jobs.Core;
using Xunit;

namespace Elsa.Jobs.Tests;

public class JobIdentityContractTests
{
    [Fact]
    public void Payload_owns_its_snapshot_after_the_source_document_is_disposed()
    {
        JobPayload payload;
        using (var document = JsonDocument.Parse("{\"report\":\"sales\",\"rows\":[1,2]}"))
            payload = new JobPayload(new JobSchema("report", 1), document.RootElement);

        Assert.Equal("sales", payload.Value.GetProperty("report").GetString());
        Assert.Equal(2, payload.Value.GetProperty("rows").GetArrayLength());
    }

    [Fact]
    public void Payload_identity_has_a_frozen_version_one_vector()
    {
        var payload = Payload("{\"b\":2,\"a\":1}");
        Assert.Equal("456a00be7205d0ba6b9670476bbb6202aecb4086f82e5fa6015baba3b28e3ce9", JobPayloadDigest.Compute(payload));
        Assert.Equal("6ea9b5572b73a25ad62687327befa7b9420a69b47636269cf0e989b0e79fe1dd", JobPayloadDigest.Compute(null));
    }

    [Fact]
    public void Object_property_order_does_not_change_a_protected_request_identity()
    {
        var first = Request(Payload("{\"b\":2,\"a\":{\"y\":2,\"x\":1}}"));
        var second = first with { Input = Payload("{\"a\":{\"x\":1,\"y\":2},\"b\":2}") };
        Assert.Equal(JobRequestDigest.Compute(first), JobRequestDigest.Compute(second));
    }

    [Fact]
    public void Equal_due_instants_with_different_offsets_keep_the_same_identity()
    {
        var first = Request(Payload("{}")) with { NotBefore = new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.FromHours(1)) };
        var second = first with { NotBefore = first.NotBefore!.Value.ToUniversalTime() };
        Assert.Equal(JobRequestDigest.Compute(first), JobRequestDigest.Compute(second));
    }

    [Fact]
    public void Schema_array_order_and_numeric_lexemes_are_part_of_the_payload_contract()
    {
        Assert.NotEqual(JobPayloadDigest.Compute(null), JobPayloadDigest.Compute(Payload("null")));
        Assert.NotEqual(JobPayloadDigest.Compute(Payload("[1,2]")), JobPayloadDigest.Compute(Payload("[2,1]")));
        Assert.NotEqual(JobPayloadDigest.Compute(Payload("1")), JobPayloadDigest.Compute(Payload("1.0")));
        Assert.NotEqual(JobPayloadDigest.Compute(Payload("1")), JobPayloadDigest.Compute(new JobPayload(new JobSchema("report", 2), JsonSerializer.SerializeToElement(1))));
    }

    [Fact]
    public void Every_immutable_policy_and_association_field_can_prevent_false_replay()
    {
        var first = Request(Payload("{\"a\":1}")) with
        {
            Association = new JobAssociation("native", "orders", "1", "run-1", "call-1", "material-1", "hash-1",
                JobAssociationMode.Awaited, Payload("{\"wait\":\"bookmark-1\"}"))
        };
        var digest = JobRequestDigest.Compute(first);
        JobRequest[] changes =
        [
            first with { Handler = first.Handler with { JobType = "reports.other" } },
            first with { Handler = first.Handler with { Version = "2" } },
            first with { Input = Payload("{\"a\":2}") },
            first with { ResultSchema = first.ResultSchema with { Version = 2 } },
            first with { Queue = "other" },
            first with { NotBefore = DateTimeOffset.Parse("2026-11-06T12:00:00Z") },
            first with { Policy = first.Policy with { AttemptTimeout = TimeSpan.FromMinutes(2) } },
            first with { Policy = first.Policy with { MinimumKeyRecognition = TimeSpan.FromDays(31) } },
            first with { Policy = first.Policy with { Retry = first.Policy.Retry with { MaximumAttempts = 4 } } },
            first with { Policy = first.Policy with { Retry = first.Policy.Retry with { InitialBackoff = TimeSpan.FromSeconds(2) } } },
            first with { Policy = first.Policy with { Retry = first.Policy.Retry with { MaximumBackoff = TimeSpan.FromMinutes(2) } } },
            first with { Policy = first.Policy with { Retry = first.Policy.Retry with { BackoffMultiplier = 3 } } },
            first with { Policy = first.Policy with { Retry = first.Policy.Retry with { JitterFraction = .1 } } },
            first with { Policy = first.Policy with { Retry = first.Policy.Retry with { RetryTimeouts = false } } },
            first with { Association = first.Association with { ModelKind = "bpmn" } },
            first with { Association = first.Association with { DefinitionId = "other" } },
            first with { Association = first.Association with { DefinitionVersion = "2" } },
            first with { Association = first.Association with { ExecutionId = "run-2" } },
            first with { Association = first.Association with { InvocationId = "call-2" } },
            first with { Association = first.Association with { MaterialId = "material-2" } },
            first with { Association = first.Association with { MaterialSha256 = "hash-2" } },
            first with { Association = first.Association with { Mode = JobAssociationMode.Independent } },
            first with { Association = first.Association with { Continuation = null } }
        ];
        Assert.All(changes, change => Assert.NotEqual(digest, JobRequestDigest.Compute(change)));
    }

    private static JobPayload Payload(string json)
    {
        using var document = JsonDocument.Parse(json);
        return new JobPayload(new JobSchema("report", 1), document.RootElement);
    }

    private static JobRequest Request(JobPayload input) => new(new JobHandlerIdentity("reports.generate", "1"),
        input, new JobSchema("report-result", 1), "default",
        new JobExecutionPolicy(new JobRetryPolicy(3, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1), 2, 0, true),
            TimeSpan.FromMinutes(1), TimeSpan.FromDays(30)), null, null);
}
