namespace Elsa.Diagnostics.StructuredLogs.Endpoints;

/// <summary>
/// The FastEndpoints permission used to guard the structured-logs diagnostics endpoints. It is
/// default-permissive (anonymous) until the host enables endpoint security, after which the host assigns
/// this permission to authorized principals.
/// </summary>
internal static class StructuredLogsPermissions
{
    public const string Policy = "Diagnostics:StructuredLogs";
}
