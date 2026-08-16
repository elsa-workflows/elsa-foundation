using System.Text.Json;

namespace Elsa.Workflows.Runtime.JavaScript;

// Preserve the consumed OpenAPI schema identifier emitted by the legacy endpoint while keeping
// the request contract explicit and source-generation friendly.
internal sealed record RequestModel(string? Script);

internal sealed record JavaScriptExecutionErrorResponse(string Error);

internal sealed record JavaScriptExecutionProblemDetailsResponse(
    string Detail,
    IReadOnlyList<JavaScriptExecutionProblemError> Errors,
    string Instance,
    int Status,
    string Title,
    string TraceId,
    string Type);

internal sealed record JavaScriptExecutionProblemError(string Name, string Reason);

internal sealed record JavaScriptExecutionSuccessResponse(bool Success, JsonElement? Value);

internal sealed record JavaScriptExecutionFailureResponse(bool Success, string Message);
