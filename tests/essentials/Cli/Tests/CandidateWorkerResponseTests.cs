using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Cli.Worker;
using Xunit;

namespace Elsa.Cli.Tests;

public sealed class CandidateWorkerResponseTests
{
    private const int MaxResponseBytes = 4 * 1024 * 1024;
    private const string PeerMessageCanary = "private-peer-message-canary-2177";
    private const string InvocationId = "11111111111111111111111111111111";
    private const string CaptureId = "22222222222222222222222222222222";

    [Fact]
    public void Parses_and_preserves_a_complete_correlated_host_success()
    {
        var candidate = Candidate();
        var host = CandidateHostResponseFixtures.Success(candidate).ToJsonString();

        var response = Parse(OuterTooling(host, ToolExitCode.Success), ToolExitCode.Success, candidate);

        Assert.Equal(WorkerContract.Version, response.Version);
        Assert.Equal(ToolExitCode.Success, response.ExitCode);
        Assert.Null(response.Error);
        var tooling = Assert.IsType<JsonElement>(response.Tooling);
        Assert.Equal(host, tooling.GetRawText());
    }

    [Fact]
    public void Parses_and_preserves_a_valid_host_refusal()
    {
        var candidate = Candidate();
        var host = HostRefusal(candidate, "resource-definition-invalid");

        var response = Parse(OuterTooling(host, ToolExitCode.Refusal), ToolExitCode.Refusal, candidate);

        Assert.Equal(ToolExitCode.Refusal, response.ExitCode);
        Assert.Null(response.Error);
        var tooling = Assert.IsType<JsonElement>(response.Tooling);
        Assert.Equal(host, tooling.GetRawText());
        Assert.Equal("resource-definition-invalid", tooling.GetProperty("error").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("candidate-request-invalid")]
    [InlineData("candidate-request-too-large")]
    [InlineData("candidate-capture-invalid")]
    public void Parses_correlated_host_input_refusals_without_reinterpreting_their_tooling(string code)
    {
        var candidate = Candidate();
        var host = HostRefusal(candidate, code);

        var response = Parse(OuterTooling(host, ToolExitCode.Refusal), ToolExitCode.Refusal, candidate);

        var tooling = Assert.IsType<JsonElement>(response.Tooling);
        Assert.Equal(host, tooling.GetRawText());
        Assert.Equal(code, tooling.GetProperty("error").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("candidate-request-invalid", ToolExitCode.Refusal)]
    [InlineData("candidate-request-too-large", ToolExitCode.Refusal)]
    [InlineData("candidate-host-unavailable", ToolExitCode.ResolutionFailure)]
    [InlineData("candidate-closure-changed", ToolExitCode.ResolutionFailure)]
    [InlineData("candidate-capability-unavailable", ToolExitCode.ResolutionFailure)]
    [InlineData("candidate-response-invalid", ToolExitCode.ResolutionFailure)]
    [InlineData("candidate-response-too-large", ToolExitCode.ResolutionFailure)]
    public void Admitted_prehost_refusals_keep_only_the_code_and_a_local_message(string code, int exitCode)
    {
        var response = Parse(OuterError(code, exitCode, PeerMessageCanary), exitCode);

        Assert.Equal(exitCode, response.ExitCode);
        Assert.Null(response.Tooling);
        Assert.Equal(code, response.Error?.Code);
        Assert.False(string.IsNullOrWhiteSpace(response.Error?.Message));
        Assert.Empty(response.Error!.Details);
        Assert.DoesNotContain(PeerMessageCanary, JsonSerializer.Serialize(response, WorkerContract.Json));
    }

    [Fact]
    public void Accepts_a_null_inactive_payload_field()
    {
        var outer = JsonNode.Parse(Encoding.UTF8.GetString(OuterTooling(
            CandidateHostResponseFixtures.Success(Candidate()).ToJsonString(), ToolExitCode.Success)))!.AsObject();
        outer["error"] = null;

        var response = Parse(Encode(outer.ToJsonString()), ToolExitCode.Success);

        Assert.NotNull(response.Tooling);
        Assert.Null(response.Error);
    }

    [Fact]
    public void Accepts_null_tooling_when_a_valid_worker_error_is_present()
    {
        var outer = JsonNode.Parse(Encoding.UTF8.GetString(OuterError(
            "candidate-host-unavailable", ToolExitCode.ResolutionFailure, PeerMessageCanary)))!.AsObject();
        outer["tooling"] = null;

        var response = Parse(Encode(outer.ToJsonString()), ToolExitCode.ResolutionFailure);

        Assert.Null(response.Tooling);
        Assert.Equal("candidate-host-unavailable", response.Error?.Code);
        Assert.DoesNotContain(PeerMessageCanary, JsonSerializer.Serialize(response, WorkerContract.Json));
    }

    [Theory]
    [InlineData("missing-version")]
    [InlineData("wrong-version")]
    [InlineData("missing-exit-code")]
    [InlineData("mismatched-exit-code")]
    [InlineData("missing-payload")]
    [InlineData("null-tooling-only")]
    [InlineData("both-payloads")]
    [InlineData("unknown-envelope-field")]
    [InlineData("duplicate-envelope-field")]
    public void Refuses_invalid_closed_outer_framing(string mutation)
    {
        var candidate = Candidate();
        var outer = JsonNode.Parse(Encoding.UTF8.GetString(OuterTooling(
            CandidateHostResponseFixtures.Success(candidate).ToJsonString(), ToolExitCode.Success)))!.AsObject();
        switch (mutation)
        {
            case "missing-version": outer.Remove("version"); break;
            case "wrong-version": outer["version"] = 1; break;
            case "missing-exit-code": outer.Remove("exitCode"); break;
            case "mismatched-exit-code": outer["exitCode"] = ToolExitCode.Refusal; break;
            case "missing-payload": outer.Remove("tooling"); break;
            case "null-tooling-only": outer["tooling"] = null; break;
            case "both-payloads":
                outer["exitCode"] = ToolExitCode.Refusal;
                outer["tooling"] = JsonNode.Parse(HostRefusal(candidate, "resource-definition-invalid"));
                outer["error"] = JsonNode.Parse(OuterError("candidate-request-invalid", ToolExitCode.Refusal, "peer message"))!["error"]!.DeepClone();
                break;
            case "unknown-envelope-field": outer["future"] = "private-future-canary"; break;
            case "duplicate-envelope-field":
                var duplicate = outer.ToJsonString()
                    .Replace("\"version\":2", "\"version\":2,\"version\":2", StringComparison.Ordinal);
                AssertInvalid(Encode(duplicate), ToolExitCode.Success, candidate);
                return;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        AssertInvalid(Encode(outer.ToJsonString()),
            mutation == "both-payloads" ? ToolExitCode.Refusal : ToolExitCode.Success,
            candidate);
    }

    [Theory]
    [InlineData("wrong-invocation")]
    [InlineData("wrong-capture")]
    [InlineData("wrong-shell")]
    [InlineData("wrong-environment")]
    [InlineData("wrong-accepted-features")]
    [InlineData("unknown-status")]
    [InlineData("wrong-host-exit-code")]
    [InlineData("unknown-host-field")]
    [InlineData("duplicate-host-field")]
    public void Refuses_tooling_that_does_not_match_the_expected_candidate(string mutation)
    {
        var candidate = Candidate();
        var host = CandidateHostResponseFixtures.Success(candidate);
        switch (mutation)
        {
            case "wrong-invocation": host["invocationId"] = "33333333333333333333333333333333"; break;
            case "wrong-capture": host["captureId"] = "33333333333333333333333333333333"; break;
            case "wrong-shell": host["configurationResolution"]!["shell"] = "other"; break;
            case "wrong-environment": host["configurationResolution"]!["environment"] = "Staging"; break;
            case "wrong-accepted-features":
                host["configurationResolution"]!["selection"]!["acceptedFeatureIds"] = new JsonArray("OtherFeature");
                break;
            case "unknown-status": host["status"] = "partial"; break;
            case "wrong-host-exit-code": host["exitCode"] = ToolExitCode.Refusal; break;
            case "unknown-host-field": host["unexpected"] = "private-host-canary"; break;
            case "duplicate-host-field":
                var duplicate = host.ToJsonString().Replace("\"version\":1", "\"version\":1,\"version\":1", StringComparison.Ordinal);
                AssertInvalid(OuterTooling(duplicate, ToolExitCode.Success), ToolExitCode.Success, candidate);
                return;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        AssertInvalid(OuterTooling(host.ToJsonString(), ToolExitCode.Success), ToolExitCode.Success, candidate);
    }

    [Theory]
    [InlineData("unknown-code", "future-candidate-error", ToolExitCode.ResolutionFailure)]
    [InlineData("wrong-exit-class", "candidate-host-unavailable", ToolExitCode.Refusal)]
    [InlineData("wrong-input-exit-class", "candidate-request-invalid", ToolExitCode.ResolutionFailure)]
    [InlineData("host-only-code", "candidate-capture-invalid", ToolExitCode.Refusal)]
    [InlineData("details", "candidate-host-unavailable", ToolExitCode.ResolutionFailure)]
    [InlineData("missing-code", "candidate-host-unavailable", ToolExitCode.ResolutionFailure)]
    [InlineData("wrong-code-type", "candidate-host-unavailable", ToolExitCode.ResolutionFailure)]
    [InlineData("wrong-message-type", "candidate-host-unavailable", ToolExitCode.ResolutionFailure)]
    [InlineData("unknown-error-field", "candidate-host-unavailable", ToolExitCode.ResolutionFailure)]
    [InlineData("duplicate-error-field", "candidate-host-unavailable", ToolExitCode.ResolutionFailure)]
    public void Refuses_unadmitted_or_malformed_outer_errors(string mutation, string code, int exitCode)
    {
        string json;
        if (mutation == "duplicate-error-field")
            json = $"{{\"version\":2,\"exitCode\":{exitCode},\"error\":{{\"code\":\"{code}\",\"code\":\"{code}\",\"message\":\"{PeerMessageCanary}\",\"details\":[]}}}}";
        else
        {
            var outer = JsonNode.Parse(Encoding.UTF8.GetString(OuterError(code, exitCode, PeerMessageCanary)))!.AsObject();
            var error = outer["error"]!.AsObject();
            switch (mutation)
            {
                case "unknown-code": error["code"] = code; break;
                case "details": error["details"] = new JsonArray("private-detail-canary"); break;
                case "missing-code": error.Remove("code"); break;
                case "wrong-code-type": error["code"] = 7; break;
                case "wrong-message-type": error["message"] = 7; break;
                case "unknown-error-field": error["future"] = "private-error-canary"; break;
            }
            json = outer.ToJsonString();
        }

        AssertInvalid(Encode(json), exitCode);
    }

    [Fact]
    public void Validates_the_expected_candidate_before_accepting_a_prehost_error()
    {
        var invalidCandidate = Candidate() with { Version = 2 };

        var refusal = AssertInvalid(OuterError("candidate-host-unavailable", ToolExitCode.ResolutionFailure, PeerMessageCanary),
            ToolExitCode.ResolutionFailure, invalidCandidate);

        Assert.Equal("candidate-response-invalid", refusal.Code);
    }

    [Fact]
    public void Rejects_a_null_expected_candidate()
    {
        Assert.Throws<ArgumentNullException>(() => WorkerContract.ParseCandidateWorkerResponse(
            OuterError("candidate-host-unavailable", ToolExitCode.ResolutionFailure, PeerMessageCanary), null!, ToolExitCode.ResolutionFailure));
    }

    [Fact]
    public void Accepts_a_response_at_the_exact_utf8_byte_limit()
    {
        var candidate = Candidate();
        var valid = OuterTooling(CandidateHostResponseFixtures.Success(candidate).ToJsonString(), ToolExitCode.Success);
        var padded = new byte[MaxResponseBytes];
        valid.CopyTo(padded, 0);
        Array.Fill(padded, (byte)' ', valid.Length, padded.Length - valid.Length);

        var response = Parse(padded, ToolExitCode.Success, candidate);

        Assert.Equal(ToolExitCode.Success, response.ExitCode);
        Assert.NotNull(response.Tooling);
    }

    [Fact]
    public void Refuses_the_byte_after_the_response_limit_before_parsing_it()
    {
        var candidate = Candidate();
        var valid = OuterTooling(CandidateHostResponseFixtures.Success(candidate).ToJsonString(), ToolExitCode.Success);
        var oversized = new byte[MaxResponseBytes + 1];
        valid.CopyTo(oversized, 0);
        Array.Fill(oversized, (byte)' ', valid.Length, oversized.Length - valid.Length);

        var refusal = Assert.Throws<WorkerRefusal>(() => Parse(oversized, ToolExitCode.Success, candidate));

        Assert.Equal("candidate-response-too-large", refusal.Code);
        Assert.Equal(ToolExitCode.ResolutionFailure, refusal.ExitCode);
    }

    [Theory]
    [InlineData("over-depth")]
    [InlineData("invalid-utf8")]
    [InlineData("multiple-json-values")]
    [InlineData("null-json")]
    [InlineData("malformed-json")]
    public void Refuses_malformed_or_excessively_nested_worker_json(string mutation)
    {
        var candidate = Candidate();
        var valid = OuterTooling(CandidateHostResponseFixtures.Success(candidate).ToJsonString(), ToolExitCode.Success);
        byte[] bytes = mutation switch
        {
            "over-depth" => Encode("{\"version\":2,\"exitCode\":0,\"tooling\":{\"nested\":" +
                                new string('[', 70) + "0" + new string(']', 70) + "}}"),
            "invalid-utf8" => [0x7b, 0x22, 0xff, 0x7d],
            "multiple-json-values" => [.. valid, .. valid],
            "null-json" => Encoding.UTF8.GetBytes("null"),
            "malformed-json" => Encoding.UTF8.GetBytes("{\"version\":2,\"exitCode\":0"),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };

        var refusal = AssertInvalid(bytes, ToolExitCode.Success, candidate);

        Assert.DoesNotContain(PeerMessageCanary, refusal.Message);
        Assert.DoesNotContain("canary", refusal.Message);
    }

    private static WorkerCandidatePayload Candidate()
    {
        var candidate = CandidateWorkerRequestFixture.Create().Candidate!;
        return candidate with
        {
            InvocationId = InvocationId,
            CaptureId = CaptureId,
            AcceptedFeatureIds = ["ResourceProbe"],
            Files = candidate.Files!.Select(file => file with { CaptureId = CaptureId }).ToArray()
        };
    }

    private static string HostRefusal(WorkerCandidatePayload candidate, string code)
    {
        var response = new JsonObject
        {
            ["version"] = 1,
            ["invocationId"] = candidate.InvocationId,
            ["captureId"] = candidate.CaptureId,
            ["status"] = "refused",
            ["exitCode"] = ToolExitCode.Refusal,
            ["error"] = new JsonObject { ["code"] = code }
        };
        return response.ToJsonString();
    }

    private static byte[] OuterTooling(string hostJson, int exitCode)
    {
        using var host = JsonDocument.Parse(hostJson);
        return JsonSerializer.SerializeToUtf8Bytes(new WorkerResponse
        {
            ExitCode = exitCode,
            Tooling = host.RootElement.Clone()
        }, WorkerContract.Json);
    }

    private static byte[] OuterError(string code, int exitCode, string message)
    {
        var response = new JsonObject
        {
            ["version"] = WorkerContract.Version,
            ["exitCode"] = exitCode,
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message,
                ["details"] = new JsonArray()
            }
        };
        return Encode(response.ToJsonString());
    }

    private static byte[] Encode(string json) => Encoding.UTF8.GetBytes(json);

    private static WorkerResponse Parse(byte[] bytes, int processExitCode, WorkerCandidatePayload? candidate = null) =>
        WorkerContract.ParseCandidateWorkerResponse(bytes, candidate ?? Candidate(), processExitCode);

    private static WorkerRefusal AssertInvalid(byte[] bytes, int processExitCode, WorkerCandidatePayload? candidate = null)
    {
        var refusal = Assert.Throws<WorkerRefusal>(() => Parse(bytes, processExitCode, candidate));
        Assert.Equal("candidate-response-invalid", refusal.Code);
        Assert.Equal(ToolExitCode.ResolutionFailure, refusal.ExitCode);
        Assert.DoesNotContain(PeerMessageCanary, refusal.Message);
        return refusal;
    }
}
