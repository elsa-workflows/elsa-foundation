using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Models;

/// <summary>A stable activity publication diagnostic exposed by the Publishing HTTP API.</summary>
public sealed record ActivityPublishingDiagnosticView(
    string Code,
    string Severity,
    string Message,
    ActivityDiagnosticSubject Subject,
    ActivityDiagnosticLocation? Location,
    string? Remediation,
    IReadOnlyDictionary<string, string> Metadata);

/// <summary>A stable activity publication problem response.</summary>
public sealed record ActivityPublishingProblemDetails(
    string Type,
    string Title,
    int Status,
    string Detail,
    string Instance,
    string ErrorCode,
    string TraceId,
    IReadOnlyList<ActivityPublishingDiagnosticView> Diagnostics);

/// <summary>A stable, safe expression-validation diagnostic exposed during publication.</summary>
public sealed record ExpressionPublicationValidationDiagnosticView(
    string Code,
    string Severity,
    string Message,
    string DocumentRevision,
    ExpressionToolingRange? Range,
    string? AuthoredPath);

/// <summary>A stable workflow expression-validation problem response.</summary>
public sealed record ExpressionPublicationValidationProblemDetails(
    string Type,
    string Title,
    int Status,
    string Detail,
    string Instance,
    string ErrorCode,
    string TraceId,
    string ValidationState,
    IReadOnlyList<ExpressionPublicationValidationDiagnosticView> Diagnostics);

/// <summary>A stable runtime-requirement preflight problem response.</summary>
public sealed record RuntimePreflightProblemDetails(
    string Type,
    string Title,
    int Status,
    string Detail,
    string Instance,
    string ErrorCode,
    string TraceId,
    IReadOnlyList<object> Diagnostics);
