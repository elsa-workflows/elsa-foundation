using System.Text.Json.Nodes;
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
        // Only fixed test context and typed categories cross the test-output boundary.
        // Native paths, graph/state identities, messages and arbitrary result fields stay private.
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
        return $"{context}; status={status}; refusalCodes=[{string.Join(",", codes)}]; diagnosticsTruncated={diagnostics?.Count > DiagnosticLimit}; protectionKnowledge={knowledge}; inventoryComplete={complete}";
    }

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
