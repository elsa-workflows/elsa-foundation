using System.Text.Json.Nodes;

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
}
