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
/// The imported process as a <c>BpmnProcess</c> ActivityNode carrying the authored
/// <c>elsa.bpmn.structure</c> payload (elements, sequence flows, lanes, diagram), plus the issues the
/// analyze pass reported.
/// </summary>
public sealed record BpmnImportResult(
    ActivityNode ProcessNode,
    BpmnImportAnalysis Analysis);

public sealed record BpmnExportOptions
{
    /// <summary>The BPMN process id to emit; defaults to the node id of the exported process.</summary>
    public string? ProcessId { get; init; }
}
