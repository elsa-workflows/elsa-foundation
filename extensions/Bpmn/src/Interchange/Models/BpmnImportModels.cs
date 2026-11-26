using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Activities.Bpmn.Interchange.Models;

/// <summary>Pre-import inventory of a BPMN 2.0 document: what maps cleanly and what degrades.</summary>
public sealed record BpmnImportAnalysis(
    IReadOnlyCollection<string> ProcessIds,
    IReadOnlyDictionary<string, int> ElementCounts,
    IReadOnlyCollection<BpmnImportIssue> Issues)
{
    /// <summary>Foundation binding assessment, distinct from the library's ability to retain the BPMN descriptor.</summary>
    public IReadOnlyList<BpmnImportCapability> Capabilities { get; init; } = [];

    /// <summary>Whether the binding references were checked against the visible host activity catalog.</summary>
    public bool HostInventoryVerified { get; init; }

    /// <summary>Verified available bindings without degradation; normal publication validation still applies.</summary>
    public bool CanExecute => HostInventoryVerified && Capabilities.All(capability => capability.Availability == BpmnImportCapabilityAvailability.Available) &&
                              Issues.All(issue => issue.Severity == BpmnImportIssueSeverity.Info);
}

public sealed record BpmnImportCapability(string ElementId, string Capability, BpmnImportCapabilityAvailability Availability, string Reason)
{
    public string? BindingRef { get; init; }

    /// <summary>The exact binding the document retained for this task, proposed to the author; null when the document retained none.</summary>
    public BpmnRetainedBinding? RetainedBinding { get; init; }
}

/// <summary>
/// A task binding a document carried in <c>elsa:taskBinding</c>: the exact activity version and the keys of its literal
/// inputs. Import proposes it and keeps the task <see cref="BpmnImportCapabilityAvailability.RequiresBinding"/> until the
/// author confirms the element through <see cref="BpmnImportOptions.ConfirmedBindings"/>.
/// </summary>
public sealed record BpmnRetainedBinding(string ActivityVersionId, IReadOnlyList<string> InputKeys);

public enum BpmnImportCapabilityAvailability
{
    Unknown,
    Available,
    RequiresBinding,
    Unsupported
}

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

    /// <summary>
    /// Element ids whose retained task binding the author confirmed. Only these become bound children; every other retained
    /// binding stays a proposal. A confirmed binding is still checked against the visible host catalog.
    /// </summary>
    public IReadOnlyCollection<string> ConfirmedBindings { get; init; } = [];
}

/// <summary>
/// One imported process/pool of a document: its BPMN process id, the collaboration participant
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
/// <c>ProcessId</c> &gt; first executable &gt; first); <see cref="ProcessNodes"/> carries every
/// process/pool in the document in document order (its selected entry's <see cref="BpmnImportedProcess.Node"/>
/// equals <see cref="ProcessNode"/>). Single-process callers keep using <see cref="ProcessNode"/> unchanged.
/// </summary>
public sealed record BpmnImportResult(
    ActivityNode ProcessNode,
    BpmnImportAnalysis Analysis,
    IReadOnlyList<BpmnImportedProcess> ProcessNodes);

public sealed record BpmnExportOptions
{
    /// <summary>
    /// The BPMN process id to emit; legacy graph export defaults to the exported node id.
    /// Complete-source export retains the authored process id and refuses an implicit rename.
    /// </summary>
    public string? ProcessId { get; init; }
}

/// <summary>The exported document and each native binding or invocation configuration left out of it, with why.</summary>
public sealed record BpmnExportResult(string Xml, IReadOnlyList<BpmnExportOmission> Omissions);

/// <summary>
/// A task binding export did not carry. <see cref="Code"/> is one of <c>catalog-unavailable</c>, <c>unresolvable-version</c>,
/// <c>unresolvable-input</c>, <c>duplicate-input</c>, <c>sensitive-input</c>, <c>non-literal-input</c>, <c>unsupported-input-setting</c>,
/// <c>unsupported-output</c>, <c>structured-child</c>, <c>oversized-binding</c> or <c>ambiguous-element</c>; <see cref="ReferenceKey"/> names the input when one input decided it.
/// Complete-source export reports <c>native-configuration-not-exported</c>, <c>scope-configuration-not-exported</c>
/// and <c>process-configuration-not-exported</c> for configuration kept outside the canonical XML.
/// </summary>
public sealed record BpmnExportOmission(string ElementId, string Code, string Message, string? ReferenceKey = null);
