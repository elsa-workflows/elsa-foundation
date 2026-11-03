namespace Elsa.Agent.Core.Models;

/// <summary>
/// Server-retained evidence offered to its original installed source before another provider disclosure.
/// A successful contribution authorizes the supplied historical evidence and results unchanged;
/// returned attachments are the permitted projection for the current turn. Required diagnostics deny provider entry.
/// </summary>
public sealed record AgentContextDisclosureRequest(
    string SessionId,
    IReadOnlyCollection<AgentContextAttachment> Context,
    IReadOnlyList<AgentToolResult> Results,
    bool IsReceiptContinuation)
{
    /// <summary>Historical transcript admission checks disclosure authority without treating old snapshots as fresh reads.</summary>
    public bool IsHistorical { get; init; }
}

/// <summary>Server-stamped preparation identity for later result disclosure; it grants no authority.</summary>
public sealed record AgentToolResultAccess(string ModuleId, string ContractVersion, AgentResourceTarget? Target)
{
    public string? DisclosureSourceId { get; init; }
    public string InvocationId { get; init; } = string.Empty;
}
