using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Cli.Worker;
using Elsa.Modularity.Planning.Models;
using Xunit;

namespace Elsa.Cli.Tests;

public sealed class CandidateInspectionOutputTests
{
    [Fact]
    public void Json_contains_only_safe_plan_and_resolution_with_unchecked_persistence()
    {
        using var fixture = new OutputFixture();
        var candidate = fixture.Capture.Payload;
        var output = Render(fixture, CandidateHostResponseFixtures.Success(candidate), "json");
        using var document = JsonDocument.Parse(output);
        Assert.Equal(new[] { "plan", "configurationResolution" }, document.RootElement.EnumerateObject().Select(field => field.Name));
        Assert.Equal("unchecked", document.RootElement.GetProperty("plan").GetProperty("persistence").GetProperty("status").GetString());
        Assert.DoesNotContain(candidate.InvocationId!, output);
        Assert.DoesNotContain(candidate.CaptureId!, output);
        Assert.DoesNotContain("private-rationale-canary", output);
        Assert.DoesNotContain("COMPOSITION_BRIDGE_CONNECTION_CANARY_NOT_A_SECRET", output);
    }

    [Theory]
    [InlineData("text")]
    [InlineData("json")]
    public void Reordered_valid_host_objects_have_identical_public_output(string format)
    {
        using var fixture = new OutputFixture();
        var candidate = fixture.Capture.Payload;
        var host = CandidateHostResponseFixtures.Success(candidate);
        Assert.Equal(Render(fixture, host, format), Render(fixture, Reverse(host), format));
    }

    [Fact]
    public void Legacy_null_targets_are_distinct_from_unavailable_provenance()
    {
        using var fixture = new OutputFixture();
        var candidate = fixture.Capture.Payload;
        var host = CandidateHostResponseFixtures.Success(candidate);
        var resolution = host["configurationResolution"]!.AsObject();
        var row = resolution["participants"]![0]!.AsObject();
        row["selection"] = "Legacy";
        foreach (var field in new[] { "resource", "provider", "connectionReference", "resourceScope" }) row[field] = null;
        row["selectorScope"] = "unavailable";
        resolution["resolution"] = "partial";
        resolution["configuredValueAffinity"] = "not-applicable";
        resolution["unresolved"] = new JsonArray("exact-file-provenance-unavailable", "legacy-target-unprojected");
        var text = Render(fixture, host, "text");
        foreach (var field in new[] { "resource", "provider", "connectionReference", "resourceScope" })
            Assert.Contains(field + ": not projected", text, StringComparison.Ordinal);
        Assert.Contains("selectorScope: unavailable", text, StringComparison.Ordinal);
        Assert.Contains("exactFileProvenance: unavailable", text, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(Render(fixture, host, "json"));
        var jsonRow = json.RootElement.GetProperty("configurationResolution").GetProperty("participants")[0];
        Assert.Equal(JsonValueKind.Null, jsonRow.GetProperty("resource").ValueKind);
    }

    [Theory]
    [InlineData("unknown", "unknown")]
    [InlineData("unavailable", "unavailable")]
    [InlineData("requested-extra", "outside")]
    [InlineData("requested-missing", "omits")]
    [InlineData("expanded-extra", "expansion")]
    [InlineData("required-disabled", "disabled")]
    [InlineData("case-collision", "case collision")]
    public void Selection_refusals_explain_each_closed_reason_with_safe_identity(string reason, string explanation)
    {
        using var fixture = new OutputFixture();
        var candidate = fixture.Capture.Payload;
        var host = Refusal(candidate, "candidate-selection-conflict", reason);
        var refusal = Assert.Throws<CliRefusal>(() => Render(fixture, host, "json", 2));
        Assert.Equal(2, refusal.ExitCode);
        Assert.Equal("candidate-selection-conflict", refusal.Code);
        Assert.Contains(explanation, refusal.Message, StringComparison.Ordinal);
        Assert.Contains("Feature: Probe", refusal.Details);
        Assert.DoesNotContain("private-rationale-canary", refusal.Message);
    }

    [Fact]
    public void Ef_refusal_preserves_only_the_admitted_resource_identity()
    {
        using var fixture = new OutputFixture();
        var candidate = fixture.Capture.Payload;
        var host = Refusal(candidate, "resource-definition-invalid");
        host["error"]!["resource"] = "primary";
        var refusal = Assert.Throws<CliRefusal>(() => Render(fixture, host, "text", 2));
        Assert.Equal("resource-definition-invalid", refusal.Code);
        Assert.Contains("Resource: primary", refusal.Details);
        Assert.DoesNotContain("private-rationale-canary", refusal.Message);
    }

    [Fact]
    public void Unexpected_peer_message_is_refused_without_a_preview_or_source_excerpt()
    {
        using var fixture = new OutputFixture();
        var candidate = fixture.Capture.Payload;
        var host = Refusal(candidate, "resource-definition-invalid");
        host["error"]!["message"] = "private-rationale-canary";
        var refusal = Assert.Throws<CliRefusal>(() => Render(fixture, host, "json", 2));
        Assert.Equal("candidate-response-invalid", refusal.Code);
        Assert.DoesNotContain("private-rationale-canary", refusal.Message);
        Assert.Empty(refusal.Details);
    }

    [Fact]
    public void A_different_capture_is_refused_even_when_its_feature_ids_match()
    {
        using var expected = new OutputFixture();
        using var other = new OutputFixture();
        Assert.Equal(expected.Capture.Payload.AcceptedFeatureIds, other.Capture.Payload.AcceptedFeatureIds);
        var refusal = Assert.Throws<CliRefusal>(() => new CandidateInspectionOutput().Render(expected.Capture,
            JsonSerializer.SerializeToElement(CandidateHostResponseFixtures.Success(other.Capture.Payload)), 0, "json"));
        Assert.Equal("candidate-response-invalid", refusal.Code);
    }

    [Fact]
    public void Unsupported_format_is_refused_by_the_output_owner()
    {
        using var fixture = new OutputFixture();
        var refusal = Assert.Throws<CliRefusal>(() => Render(fixture,
            CandidateHostResponseFixtures.Success(fixture.Capture.Payload), "xml"));
        Assert.Equal("composition-format-invalid", refusal.Code);
        Assert.Equal(2, refusal.ExitCode);
    }

    [Fact]
    public void Explicit_environment_output_requires_its_lane_source_and_intent_projection()
    {
        using var fixture = new EnvironmentOutputFixture();
        var host = CandidateHostResponseFixtures.Success(fixture.Capture.Payload);
        var resolution = host["configurationResolution"]!.AsObject();
        resolution["source"] = "captured-workbench-json-explicit-environment-v1";
        resolution["externalInputs"] = "supplied-intended";

        using var document = JsonDocument.Parse(new CandidateInspectionOutput().Render(fixture.Capture,
            JsonSerializer.SerializeToElement(host), 0, "json"));

        Assert.Equal("captured-workbench-json-explicit-environment-v1",
            document.RootElement.GetProperty("configurationResolution").GetProperty("source").GetString());
        Assert.Equal("supplied-intended",
            document.RootElement.GetProperty("configurationResolution").GetProperty("externalInputs").GetString());
    }

    [Fact]
    public void Explicit_environment_output_rejects_a_file_only_source()
    {
        using var fixture = new EnvironmentOutputFixture();
        var refusal = Assert.Throws<CliRefusal>(() => new CandidateInspectionOutput().Render(fixture.Capture,
            JsonSerializer.SerializeToElement(CandidateHostResponseFixtures.Success(fixture.Capture.Payload)), 0, "json"));

        Assert.Equal("candidate-response-invalid", refusal.Code);
        Assert.DoesNotContain(CandidateInspectionFixture.PrivateEnvironmentCanary, refusal.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Explicit_environment_host_unenrollment_preserves_resolution_failure_exit()
    {
        using var fixture = new EnvironmentOutputFixture();
        var refusal = new JsonObject
        {
            ["version"] = 1,
            ["invocationId"] = fixture.Capture.Payload.InvocationId,
            ["captureId"] = fixture.Capture.Payload.CaptureId,
            ["status"] = "refused",
            ["exitCode"] = 3,
            ["error"] = new JsonObject { ["code"] = "candidate-environment-host-unenrolled" }
        };

        var error = Assert.Throws<CliRefusal>(() => new CandidateInspectionOutput().Render(fixture.Capture,
            JsonSerializer.SerializeToElement(refusal), 3, "json"));

        Assert.Equal("candidate-environment-host-unenrolled", error.Code);
        Assert.Equal(3, error.ExitCode);
    }

    [Theory]
    [InlineData("candidate-request-invalid", "request is invalid")]
    [InlineData("candidate-request-too-large", "size limit")]
    [InlineData("candidate-capture-invalid", "files are invalid")]
    [InlineData("candidate-selection-conflict", "selection conflicts")]
    [InlineData("resource-selection-invalid", "selection is invalid")]
    [InlineData("resource-not-found", "not defined")]
    [InlineData("resource-definition-invalid", "invalid definition")]
    [InlineData("resource-configurator-unsupported", "configurator")]
    [InlineData("resource-required-feature-disabled", "disabled")]
    [InlineData("resource-legacy-conflict", "legacy")]
    [InlineData("resource-ownership-unresolved", "Ownership")]
    [InlineData("resource-context-conflict", "shared context")]
    public void Each_host_refusal_has_fixed_local_wording(string code, string wording)
    {
        using var fixture = new OutputFixture();
        var refusal = Assert.Throws<CliRefusal>(() => Render(fixture, Refusal(fixture.Capture.Payload, code), "text", 2));
        Assert.Equal(code, refusal.Code);
        Assert.Equal(2, refusal.ExitCode);
        Assert.Contains(wording, refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("private-rationale-canary", refusal.Message);
    }

    private static string Render(OutputFixture fixture, JsonObject host, string format, int exitCode = 0) =>
        new CandidateInspectionOutput().Render(fixture.Capture, JsonSerializer.SerializeToElement(host), exitCode, format);

    private sealed class OutputFixture : IDisposable
    {
        private readonly CompositionBridgeFixture source = new();
        public CompositionInspectionCapture Capture { get; }
        public OutputFixture()
        {
            source.WriteAcceptedComposition();
            var profile = source.WriteWorkspaceProfile("output-profile.json", "output-start", "1", ["A"],
                "private-rationale-canary");
            var authored = JsonNode.Parse(File.ReadAllText(source.OutputPath))!;
            authored["profile"] = JsonSerializer.SerializeToNode(new DefinitionReference("workspace", "profile",
                profile.Definition.Id, profile.Definition.Version, profile.Definition.Digest),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            File.WriteAllText(source.OutputPath, authored.ToJsonString());
            Capture = CompositionInspectionCapture.Open(source.HostDirectory, "default", "Production", source.OutputPath,
                source.CatalogPath, source.ReviewPath, [profile.Path]);
        }
        public void Dispose() => source.Dispose();
    }

    private sealed class EnvironmentOutputFixture : IDisposable
    {
        private readonly CompositionBridgeFixture source = new();
        public CompositionInspectionCapture Capture { get; }

        public EnvironmentOutputFixture()
        {
            source.WriteAcceptedComposition();
            Directory.CreateDirectory(source.CandidateDirectory);
            var environmentPath = Path.Join(source.CandidateDirectory, "environment.json");
            File.WriteAllBytes(environmentPath, CandidateInspectionFixture.EnvironmentDocument(("Key", "value")));
            var host = new HostLayout(source.HostDirectory, "Example.Host",
                Path.Join(source.HostDirectory, "Example.Host.runtimeconfig.json"),
                Path.Join(source.HostDirectory, "Example.Host.deps.json"));
            Capture = CompositionInspectionCapture.OpenWithEnvironmentInput(host, source.HostDirectory,
                "default", "Production", source.OutputPath, environmentPath, source.CatalogPath, source.ReviewPath);
        }

        public void Dispose()
        {
            Capture.Dispose();
            source.Dispose();
        }
    }

    private static JsonObject Refusal(WorkerCandidatePayload candidate, string code, string? reason = null)
    {
        var error = new JsonObject { ["code"] = code };
        if (code is not ("candidate-request-invalid" or "candidate-request-too-large" or "candidate-capture-invalid"))
            error["feature"] = "Probe";
        if (reason is not null) error["reason"] = reason;
        return new JsonObject
        {
            ["version"] = 1, ["invocationId"] = candidate.InvocationId, ["captureId"] = candidate.CaptureId,
            ["status"] = "refused", ["exitCode"] = 2, ["error"] = error
        };
    }

    private static JsonObject Reverse(JsonObject source)
    {
        var result = new JsonObject();
        foreach (var field in source.Reverse())
            result[field.Key] = ReverseValue(field.Value);
        return result;
    }

    private static JsonNode? ReverseValue(JsonNode? value) => value switch
    {
        JsonObject child => Reverse(child),
        JsonArray array => new JsonArray(array.Select(ReverseValue).ToArray()),
        _ => value?.DeepClone()
    };
}
