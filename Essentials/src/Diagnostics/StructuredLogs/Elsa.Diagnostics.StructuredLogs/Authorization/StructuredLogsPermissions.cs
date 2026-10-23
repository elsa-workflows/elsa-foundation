namespace Elsa.Diagnostics.StructuredLogs.Authorization;

/// <summary>Stable permissions owned by the structured-logs diagnostics feature.</summary>
public static class StructuredLogsPermissions
{
    public const string OwnerId = "Elsa.Diagnostics.StructuredLogs";

    public const string Read = "diagnostics/structured-logs:read";

    // Kept as an explicit policy alias for callers migrating from the legacy endpoint adapter.
    public const string Policy = Read;
}
