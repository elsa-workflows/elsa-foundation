using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;

namespace Elsa.Etl.Workflows.Runtime.Models;

/// <summary>A bounded, versioned snapshot of one immutable invocation, without rows or worker state.</summary>
/// <remarks>The inner ETL-owned document preserves opaque identity code units across native JSON persistence.</remarks>
public sealed record RunEtlState(int FormatVersion, JsonElement Invocation);

/// <summary>One frozen invocation and its durable terminal result, delivered through the native typed wait.</summary>
public sealed record RunEtlCompletion(int FormatVersion, JsonElement Invocation, JsonElement Result);

/// <summary>The small atomic result available to surrounding native composites.</summary>
public sealed record RunEtlResult(string RunId, EtlTerminalOutcome Outcome, long AcceptedRecords,
    long RejectedRecords, long FilteredRecords, string? DiagnosticCode);

internal sealed record RunEtlRequest(EtlScope Scope, string RunId, EtlArtifact Artifact,
    string WorkflowExecutionId, string ActivityExecutionId, string ExecutableId, string ExecutableHash, string NodeId,
    string? ResumeTargetId = null);
