using Microsoft.AspNetCore.Http;

namespace Elsa.Api.AspNetCore;

/// <summary>A failure to report from an endpoint, as a status code and a keyed set of messages.</summary>
public sealed record EndpointProblem(int StatusCode, IReadOnlyDictionary<string, string[]> Errors)
{
    /// <summary>A problem carrying a single message under a general key.</summary>
    public static EndpointProblem General(int statusCode, string message, string key = "generalErrors") =>
        new(statusCode, new Dictionary<string, string[]>(StringComparer.Ordinal) { [key] = [message] });
}

/// <summary>
/// Translates an exception raised while handling a request into a response.
/// </summary>
/// <remarks>
/// Owner modules register their own translator. The exception-to-status mapping is domain knowledge
/// -- a promotion conflict, a soft-delete state error, an unavailable permanent delete -- so it stays
/// with the module that defines those exceptions, and the shared layer takes no dependency on them.
/// Translators are consulted in registration order; the first non-null result wins.
/// </remarks>
public interface IEndpointExceptionTranslator
{
    EndpointProblem? Translate(Exception exception);
}

/// <summary>Writes an <see cref="EndpointProblem"/> in the owner's established error shape.</summary>
/// <remarks>
/// The wire shape of an error is part of a module's published contract, so the module owns it rather
/// than inheriting a shared one that would silently change existing responses.
/// </remarks>
public interface IEndpointProblemWriter
{
    Task WriteAsync(HttpContext context, EndpointProblem problem);
}
