using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text;
using global::Nuplane.Abstractions.PackageStoreProtection;

namespace Elsa.Studio.Tests.Administration;

internal enum PackageMaintenancePhase
{
    RecoverStaleUses,
    KeepAll,
    DeleteObsolete,
    PreserveRetainedGeneration,
    RetainAll
}

internal static class PackageMaintenanceAssertions
{
    private const int DiagnosticLimit = 8;

    internal static void Completed(JsonObject result, PackageMaintenancePhase phase)
        => Assert.True((string?)result["status"] == "Completed", Describe(result, phase));

    internal static string Describe(JsonObject result, PackageMaintenancePhase phase)
    {
        // Only fixed test context, typed categories and bounded SHA256 fingerprints cross the test-output boundary.
        // Native paths, graph/state identities, raw messages and arbitrary result fields stay private.
        var context = phase switch
        {
            PackageMaintenancePhase.RecoverStaleUses => "phase=recover-stale-uses; seededVersions=1.0.0,2.0.0",
            PackageMaintenancePhase.KeepAll => "phase=keep-all; seededVersions=0.5.0,1.0.0",
            PackageMaintenancePhase.DeleteObsolete => "phase=delete-obsolete; seededVersions=0.5.0,1.0.0",
            PackageMaintenancePhase.PreserveRetainedGeneration => "phase=preserve-retained-generation; seededVersions=1.0.0,2.0.0; retainedShell=1.0.0",
            PackageMaintenancePhase.RetainAll => "phase=retain-all; seededVersions=1.0.0,2.0.0",
            _ => "phase=unknown; seededVersions=unknown"
        };
        var status = Allowed(result["status"], "Preview", "Completed", "Refused", "Failed", "Cancelled");
        var knowledge = Allowed(result["protectionKnowledge"], "Known", "Unknown");
        var complete = result["inventoryComplete"] is JsonValue value && value.TryGetValue<bool>(out var inventoryComplete)
            ? inventoryComplete ? "true" : "false"
            : "unknown";
        var diagnostics = result["diagnostics"] as JsonArray;
        var codes = diagnostics?.Take(DiagnosticLimit).Select(AdmissionCode).Distinct().Order(StringComparer.Ordinal)
            ?? Enumerable.Empty<string>();
        var fingerprints = diagnostics?.Take(DiagnosticLimit).Select(MessageFingerprint).Distinct().Order(StringComparer.Ordinal)
            ?? Enumerable.Empty<string>();
        return $"{context}; status={status}; refusalCodes=[{string.Join(",", codes)}]; refusalFingerprints=[{string.Join(",", fingerprints)}]; diagnosticsTruncated={diagnostics?.Count > DiagnosticLimit}; protectionKnowledge={knowledge}; inventoryComplete={complete}";
    }

    private static string MessageFingerprint(JsonNode? diagnostic)
        => AdmissionCode(diagnostic) is var code && code != "OtherOrMissing" && diagnostic is JsonObject item
            && item["message"] is JsonValue value && value.TryGetValue<string>(out var message) && message is { Length: > 0 and <= 8192 }
                ? code + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(message)))
                : "OtherOrMissing";

    private static string AdmissionCode(JsonNode? diagnostic)
    {
        if (diagnostic is JsonObject item && item["code"] is JsonValue value && value.TryGetValue<string>(out var code) && code is { Length: <= 64 }
            && Enum.TryParse<PackageStoreAdmissionReason>(code, out var reason) && Enum.IsDefined(reason)
            && string.Equals(reason.ToString(), code, StringComparison.Ordinal))
            return reason.ToString();
        return "OtherOrMissing";
    }

    private static string Allowed(JsonNode? node, params string[] allowed)
        => node is JsonValue value && value.TryGetValue<string>(out var token) && allowed.Contains(token, StringComparer.Ordinal)
            ? token
            : "OtherOrMissing";
}
