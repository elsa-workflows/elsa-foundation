using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Cli.Worker;
using Xunit;

namespace Elsa.Cli.Tests;

/// <summary>Closed public projection contents, independently of private envelope framing.</summary>
public sealed class CandidateResolutionTests
{
    private const string Invocation = "11111111111111111111111111111111";
    private const string Capture = "22222222222222222222222222222222";

    [Theory]
    [InlineData("resource")]
    [InlineData("legacy")]
    [InlineData("empty")]
    [InlineData("resource-ids")]
    [InlineData("partial")]
    [InlineData("boundary")]
    [InlineData("partial-unenrolled")]
    [InlineData("selection-boundary")]
    public async Task Valid_resource_legacy_and_empty_projections_remain_honest(string kind)
    {
        var response = Response(kind);
        using var stream = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(response));
        var admitted = await WorkerContract.ReadCandidateHostResponseAsync(stream, Invocation, Capture, 0, CancellationToken.None);
        Assert.Equal(response.ToJsonString(), admitted.GetRawText());
    }

    [Theory]
    [InlineData("empty-body")]
    [InlineData("unknown-field")]
    [InlineData("missing-field")]
    [InlineData("missing-selection-field")]
    [InlineData("missing-unresolved-field")]
    [InlineData("source")]
    [InlineData("unsafe-shell")]
    [InlineData("unsafe-environment")]
    [InlineData("claims-ready")]
    [InlineData("wrong-resolution")]
    [InlineData("selection-extra")]
    [InlineData("selection-order")]
    [InlineData("selection-case-collision")]
    [InlineData("implicit-feature")]
    [InlineData("active-disabled")]
    [InlineData("disabled-case-collision")]
    [InlineData("participant-order")]
    [InlineData("unknown-participant-field")]
    [InlineData("missing-participant-field")]
    [InlineData("unselected-participant")]
    [InlineData("unsafe-module")]
    [InlineData("unknown-provider")]
    [InlineData("unknown-selection")]
    [InlineData("inline-resource")]
    [InlineData("inline-connection")]
    [InlineData("reserved-resource")]
    [InlineData("reserved-connection")]
    [InlineData("unknown-selector-scope")]
    [InlineData("unknown-resource-scope")]
    [InlineData("exact-provenance")]
    [InlineData("missing-resource-target")]
    [InlineData("legacy-with-target")]
    [InlineData("duplicate-participant")]
    [InlineData("unknown-unresolved")]
    [InlineData("unresolved-order")]
    [InlineData("missing-legacy-evidence")]
    [InlineData("missing-provenance-evidence")]
    [InlineData("oversized-identity")]
    [InlineData("excessive-selection")]
    [InlineData("excessive-total-rows")]
    [InlineData("resolution-wrong-type")]
    [InlineData("selection-wrong-type")]
    [InlineData("participants-wrong-type")]
    [InlineData("participant-wrong-type")]
    [InlineData("unresolved-wrong-type")]
    [InlineData("missing-scope-evidence")]
    [InlineData("resolved-with-unavailable-evidence")]
    [InlineData("empty-checked")]
    [InlineData("resource-not-applicable")]
    [InlineData("empty-provenance-evidence")]
    [InlineData("empty-legacy-evidence")]
    [InlineData("legacy-resource-scope")]
    public async Task Unsafe_or_inconsistent_projection_refuses_without_exporting_peer_values(string mutation)
    {
        var kind = mutation is "legacy-with-target" or "missing-legacy-evidence" or "legacy-resource-scope" ? "legacy" :
            mutation is "unresolved-order" or "missing-scope-evidence" ? "partial" :
            mutation == "resolved-with-unavailable-evidence" ? "partial-unenrolled" : "resource";
        var response = Response(kind);
        var body = response["configurationResolution"]!.AsObject();
        var selection = body["selection"]!.AsObject();
        var participants = body["participants"]!.AsArray();
        var row = participants.Count == 0 ? null : participants[0]!.AsObject();
        switch (mutation)
        {
            case "empty-body": response["configurationResolution"] = new JsonObject(); break;
            case "unknown-field": body["secret"] = "private-projection-canary"; break;
            case "missing-field": body.Remove("connectivity"); break;
            case "missing-selection-field": selection.Remove("disabledFeatureIds"); break;
            case "missing-unresolved-field": body.Remove("unresolved"); break;
            case "source": body["source"] = "other-source"; break;
            case "unsafe-shell": body["shell"] = "private-projection-canary:path"; break;
            case "unsafe-environment": body["environment"] = "../private-projection-canary"; break;
            case "claims-ready": body["migrationReadiness"] = "verified"; break;
            case "wrong-resolution": body["resolution"] = "partial"; break;
            case "selection-extra": selection["requestedFeatureIds"] = new JsonArray("Probe", "Unexpected"); break;
            case "selection-order": SetIds(selection, "Z", "Probe"); break;
            case "selection-case-collision": SetIds(selection, "Probe", "probe"); break;
            case "implicit-feature": selection["implicitFeatureIds"] = new JsonArray("Probe"); break;
            case "active-disabled": selection["disabledFeatureIds"] = new JsonArray("Probe"); break;
            case "disabled-case-collision": selection["disabledFeatureIds"] = new JsonArray("A", "a"); break;
            case "participant-order":
            {
                SetRows(participants, 2);
                var first = participants[0]!.DeepClone();
                participants[0] = participants[1]!.DeepClone();
                participants[1] = first;
                break;
            }
            case "unknown-participant-field": row!["connectionValue"] = "private-projection-canary"; break;
            case "missing-participant-field": row!.Remove("resourceScope"); break;
            case "unselected-participant": row!["feature"] = "Unexpected"; break;
            case "unsafe-module": row!["module"] = "private-projection-canary:path"; break;
            case "unknown-provider": row!["provider"] = "Unknown"; break;
            case "unknown-selection": row!["selection"] = "Future"; break;
            case "inline-resource": row!["resource"] = "Server=private-projection-canary"; break;
            case "inline-connection": row!["connectionReference"] = "Password=private-projection-canary"; break;
            case "reserved-resource": row!["resource"] = "false"; break;
            case "reserved-connection": row!["connectionReference"] = "null"; break;
            case "unknown-selector-scope": row!["selectorScope"] = "other"; break;
            case "unknown-resource-scope": row!["resourceScope"] = "other"; break;
            case "exact-provenance": row!["exactFileProvenance"] = "appsettings.json"; break;
            case "missing-resource-target": row!["resource"] = null; break;
            case "legacy-with-target": row!["resource"] = "primary"; break;
            case "duplicate-participant": participants.Add(row!.DeepClone()); break;
            case "unknown-unresolved": body["unresolved"] = new JsonArray("private-projection-canary"); break;
            case "unresolved-order": body["unresolved"] = new JsonArray("resource-scope-unsupported", "exact-file-provenance-unavailable"); break;
            case "missing-legacy-evidence": body["unresolved"] = new JsonArray("exact-file-provenance-unavailable", "resource-scope-unsupported"); break;
            case "missing-provenance-evidence": body["unresolved"] = new JsonArray(); break;
            case "oversized-identity": row!["module"] = new string('a', 129); break;
            case "excessive-selection":
                SetIds(selection, Enumerable.Range(0, 4097).Select(index => $"F{index:D4}").ToArray());
                participants.Clear();
                break;
            case "excessive-total-rows":
                SetRows(participants, 1024);
                break;
            case "resolution-wrong-type": body["resolution"] = 1; break;
            case "selection-wrong-type": body["selection"] = "Probe"; break;
            case "participants-wrong-type": body["participants"] = "Probe"; break;
            case "participant-wrong-type": row!["module"] = 1; break;
            case "unresolved-wrong-type": body["unresolved"] = "private-projection-canary"; break;
            case "missing-scope-evidence": body["unresolved"] = new JsonArray("exact-file-provenance-unavailable", "resource-participant-unenrolled"); break;
            case "resolved-with-unavailable-evidence": body["resolution"] = "resolved"; break;
            case "empty-checked":
                SetIds(selection);
                participants.Clear();
                body["unresolved"] = new JsonArray();
                break;
            case "resource-not-applicable": body["configuredValueAffinity"] = "not-applicable"; break;
            case "empty-provenance-evidence":
                SetIds(selection);
                participants.Clear();
                body["configuredValueAffinity"] = "not-applicable";
                break;
            case "empty-legacy-evidence":
                SetIds(selection);
                participants.Clear();
                body["configuredValueAffinity"] = "not-applicable";
                body["resolution"] = "partial";
                body["unresolved"] = new JsonArray("legacy-target-unprojected");
                break;
            case "legacy-resource-scope": row!["resourceScope"] = "root"; break;
        }
        using var stream = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(response));
        var refusal = await Assert.ThrowsAsync<WorkerRefusal>(() =>
            WorkerContract.ReadCandidateHostResponseAsync(stream, Invocation, Capture, 0, CancellationToken.None));
        Assert.Equal("candidate-response-invalid", refusal.Code);
        Assert.Equal(3, refusal.ExitCode);
        Assert.DoesNotContain("private-projection-canary", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Candidate_host_response_reader_rejects_duplicate_nested_projection_fields()
    {
        var json = Response("resource").ToJsonString().Replace(
            "\"source\":\"captured-workbench-json-v1\"",
            "\"source\":\"captured-workbench-json-v1\",\"source\":\"captured-workbench-json-v1\"",
            StringComparison.Ordinal);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        var refusal = await Assert.ThrowsAsync<WorkerRefusal>(() =>
            WorkerContract.ReadCandidateHostResponseAsync(stream, Invocation, Capture, 0, CancellationToken.None));
        Assert.Equal("candidate-response-invalid", refusal.Code);
        Assert.Equal(3, refusal.ExitCode);
    }

    private static void SetIds(JsonObject selection, params string[] ids)
    {
        foreach (var name in new[] { "acceptedFeatureIds", "requestedFeatureIds", "effectiveFeatureIds" })
            selection[name] = new JsonArray(ids.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());
    }

    private static JsonObject Response(string kind)
    {
        var response = CandidateHostResponseFixtures.Success(Invocation, Capture);
        var body = response["configurationResolution"]!.AsObject();
        var selection = body["selection"]!.AsObject();
        var participants = body["participants"]!.AsArray();
        var legacy = kind == "legacy";
        var empty = kind.StartsWith("empty", StringComparison.Ordinal);
        if (legacy)
        {
            body["resolution"] = "partial";
            body["configuredValueAffinity"] = "not-applicable";
            body["unresolved"] = new JsonArray("exact-file-provenance-unavailable", "legacy-target-unprojected");
            var row = participants[0]!.AsObject();
            row["selection"] = "Legacy";
            row["resource"] = null;
            row["provider"] = null;
            row["connectionReference"] = null;
            row["selectorScope"] = "feature";
            row["resourceScope"] = null;
        }
        else if (empty)
        {
            SetIds(selection);
            participants.Clear();
            body["configuredValueAffinity"] = "not-applicable";
            body["unresolved"] = new JsonArray();
        }
        else if (kind == "partial-unenrolled")
        {
            SetIds(selection);
            participants.Clear();
            body["resolution"] = "partial";
            body["configuredValueAffinity"] = "not-applicable";
            body["unresolved"] = new JsonArray("resource-participant-unenrolled");
        }
        else if (kind == "selection-boundary")
        {
            SetIds(selection, Enumerable.Range(0, 4096).Select(index => $"Feature{index:D4}").ToArray());
            participants.Clear();
            body["unresolved"] = new JsonArray();
            body["configuredValueAffinity"] = "not-applicable";
        }

        if (kind == "resource-ids")
        {
            SetIds(selection, "Probe/Feature+1");
            participants[0]!["feature"] = "Probe/Feature+1";
            participants[0]!["module"] = "Probe/Module+1";
        }
        else if (kind == "partial")
        {
            body["resolution"] = "partial";
            participants[0]!["resourceScope"] = "unavailable";
            body["unresolved"] = new JsonArray("exact-file-provenance-unavailable", "resource-scope-unsupported");
        }
        else if (kind == "boundary")
            SetRows(participants, 1023);
        return response;
    }

    private static void SetRows(JsonArray participants, int count)
    {
        var template = participants[0]!.DeepClone();
        participants.Clear();
        for (var index = 0; index < count; index++)
        {
            var row = template.DeepClone().AsObject();
            row["module"] = $"Module{index:D4}";
            participants.Add(row);
        }
    }
}
