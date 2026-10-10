namespace Elsa.Expressions.JavaScript.Rendering;

internal sealed record JavaScriptRenderingSuccessResponse(bool Success, string Document);

internal sealed record JavaScriptRenderingFailureResponse(bool Success, string Message);
