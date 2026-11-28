namespace Elsa.Http.Core.Exceptions;

/// <summary>
/// Exception thrown when a bad request is received. Lives in <c>Elsa.Http.Core</c> so the
/// request middleware and the default fault handler can share it without a cross-module edge.
/// </summary>
public class HttpBadRequestException : Exception
{
    /// <inheritdoc />
    public HttpBadRequestException(string message, Exception exception) : base(message, exception)
    {
    }
}
