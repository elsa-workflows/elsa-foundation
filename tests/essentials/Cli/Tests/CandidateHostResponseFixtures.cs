using System.Text.Json.Nodes;
using Elsa.Cli.Worker;

namespace Elsa.Cli.Tests;

/// <summary>One complete synthetic host success used by worker framing and projection tests.</summary>
internal static class CandidateHostResponseFixtures
{
    public static JsonObject Success(string invocationId, string captureId) => new()
    {
        ["version"] = 1,
        ["invocationId"] = invocationId,
        ["captureId"] = captureId,
        ["status"] = "ok",
        ["exitCode"] = 0,
        ["configurationResolution"] = new JsonObject
        {
            ["source"] = "captured-workbench-json-v1",
            ["shell"] = "default",
            ["environment"] = "Production",
            ["resolution"] = "resolved",
            ["selection"] = new JsonObject
            {
                ["acceptedFeatureIds"] = new JsonArray("Probe"),
                ["requestedFeatureIds"] = new JsonArray("Probe"),
                ["effectiveFeatureIds"] = new JsonArray("Probe"),
                ["disabledFeatureIds"] = new JsonArray(),
                ["implicitFeatureIds"] = new JsonArray()
            },
            ["participants"] = new JsonArray
            {
                new JsonObject
                {
                    ["feature"] = "Probe",
                    ["module"] = "Probe.Module",
                    ["selection"] = "RootDefault",
                    ["resource"] = "primary",
                    ["provider"] = "Sqlite",
                    ["connectionReference"] = "Probe",
                    ["selectorScope"] = "root",
                    ["resourceScope"] = "root",
                    ["exactFileProvenance"] = "unavailable"
                }
            },
            ["configuredValueAffinity"] = "checked",
            ["targetVerification"] = "not-performed",
            ["runtimeParity"] = "unobserved",
            ["packageReachability"] = "unverified",
            ["connectivity"] = "unverified",
            ["schemaReadiness"] = "unverified",
            ["migrationReadiness"] = "unverified",
            ["activation"] = "unobserved",
            ["externalInputs"] = "unverified",
            ["unresolved"] = new JsonArray("exact-file-provenance-unavailable")
        }
    };

    public static string SuccessJson(string invocationId, string captureId) => Success(invocationId, captureId).ToJsonString();

    public static JsonObject Success(WorkerCandidatePayload candidate)
    {
        var response = Success(candidate.InvocationId!, candidate.CaptureId!);
        var body = response["configurationResolution"]!.AsObject();
        body["shell"] = candidate.Shell;
        body["environment"] = candidate.Environment;
        var selection = body["selection"]!.AsObject();
        foreach (var field in new[] { "acceptedFeatureIds", "requestedFeatureIds", "effectiveFeatureIds" })
            selection[field] = new JsonArray(candidate.AcceptedFeatureIds!
                .Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());
        selection["disabledFeatureIds"] = new JsonArray(candidate.RemovedFeatureIds!
            .Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());
        var participants = body["participants"]!.AsArray();
        if (candidate.AcceptedFeatureIds!.Count != 0)
            participants[0]!["feature"] = candidate.AcceptedFeatureIds![0];
        else
        {
            participants.Clear();
            body["configuredValueAffinity"] = "not-applicable";
            body["unresolved"] = new JsonArray();
        }
        return response;
    }
}
