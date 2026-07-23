using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Activities.Bpmn.Interchange.Models;

/// <summary>Pre-import inventory of a BPMN 2.0 document: what maps cleanly and what degrades.</summary>
public sealed record BpmnImportAnalysis(
    IReadOnlyCollection<string> ProcessIds,
    IReadOnlyDictionary<string, int> ElementCounts,
    IReadOnlyCollection<BpmnImportIssue> Issues);

public sealed record BpmnImportIssue(
    BpmnImportIssueSeverity Severity,
    string Message,
    string? ElementId = null);

public enum BpmnImportIssueSeverity
{
    /// <summary>Informational — the element imported cleanly with a note (e.g. an unbound task).</summary>
    Info,

    /// <summary>The element imported in a degraded form (e.g. an expression condition carried as text).</summary>
    Degraded,

    /// <summary>The element could not be imported and was dropped.</summary>
    Dropped
}

public sealed record BpmnImportOptions
{
    /// <summary>The id of the process to import; defaults to the first executable (else first) process.</summary>
    public string? ProcessId { get; init; }

    /// <summary>Prefix for generated activity/element node ids; defaults to the process id.</summary>
    public string? NodeIdPrefix { get; init; }
}

/// <summary>
/// One imported process/pool of a document (spec 136): its BPMN process id, the collaboration participant
/// that references it (when unambiguous — a black-box or unreferenced process carries none), and the imported
/// <c>BpmnProcess</c> <see cref="ActivityNode"/>. A single-process document yields a one-entry list.
/// </summary>
public sealed record BpmnImportedProcess(
    string ProcessId,
    string? ParticipantId,
    string? ParticipantName,
    ActivityNode Node);

/// <summary>
/// The imported process as a <c>BpmnProcess</c> ActivityNode carrying the authored
/// <c>elsa.bpmn.structure</c> payload (elements, sequence flows, pools, lanes, message flows, diagram), plus
/// the issues the analyze pass reported. <see cref="ProcessNode"/> is the selected/primary process (explicit
/// <c>ProcessId</c> &gt; first executable &gt; first); <see cref="ProcessNodes"/> (spec 136) carries every
/// process/pool in the document in document order (its selected entry's <see cref="BpmnImportedProcess.Node"/>
/// equals <see cref="ProcessNode"/>). Single-process callers keep using <see cref="ProcessNode"/> unchanged.
/// </summary>
public sealed record BpmnImportResult(
    ActivityNode ProcessNode,
    BpmnImportAnalysis Analysis,
    IReadOnlyList<BpmnImportedProcess> ProcessNodes);

public sealed record BpmnExportOptions
{
    /// <summary>The BPMN process id to emit; defaults to the node id of the exported process.</summary>
    public string? ProcessId { get; init; }
}
