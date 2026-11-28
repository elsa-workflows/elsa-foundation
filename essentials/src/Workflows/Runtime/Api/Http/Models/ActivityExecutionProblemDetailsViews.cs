namespace Elsa.Workflows.Runtime.Api.Models;

/// <summary>RFC 7807 response used by Runtime-owned activity execution inspection endpoints.</summary>
public sealed record ActivityExecutionProblemDetailsView(
    string Type,
    string Title,
    int Status,
    string Detail,
    string Instance,
    string ErrorCode,
    string TraceId,
    IReadOnlyList<ActivityExecutionProblemDiagnosticView> Diagnostics,
    ActivityExecutionCursorProblemView? Cursor);

public sealed record ActivityExecutionCursorProblemView(
    string CursorClass,
    string BoundaryBinding,
    string QueryBinding,
    string AccessBinding,
    bool Recoverable,
    string RecoveryAction);

/// <summary>Safe diagnostic extension point. Inspection request failures currently return an empty list.</summary>
public sealed record ActivityExecutionProblemDiagnosticView(string Code, string Message, string Severity);
