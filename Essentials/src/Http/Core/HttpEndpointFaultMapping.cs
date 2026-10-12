using Elsa.Http.Core.Exceptions;
using Microsoft.AspNetCore.Http;

namespace Elsa.Http.Core;

/// <summary>
/// The single owner of the endpoint dispatch-fault → HTTP status mapping (spec 089 sub-unit C). Both the default
/// <c>IHttpEndpointFaultHandler</c> implementation in <c>Elsa.Workflows.Runtime.Http</c> and the request
/// middleware's inline fallback (used when the policy feature is absent) delegate here, so the two paths cannot
/// drift: a timeout/cancellation maps to <c>408</c>, an <see cref="HttpBadRequestException"/> to <c>400</c>, and
/// anything else to <c>500</c>.
/// </summary>
public static class HttpEndpointFaultMapping
{
    /// <summary>
    /// Maps a single dispatch fault to its response status code: timeout/cancellation → <c>408</c>,
    /// <see cref="HttpBadRequestException"/> → <c>400</c>, otherwise → <c>500</c>.
    /// </summary>
    public static int ToStatusCode(Exception exception) => exception switch
    {
        TimeoutException or OperationCanceledException => StatusCodes.Status408RequestTimeout,
        HttpBadRequestException => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status500InternalServerError
    };

    /// <summary>
    /// Maps a set of dispatch faults to a single response status code, honouring the same precedence a handler
    /// uses when several exceptions are reported at once: a timeout wins over a bad request, which wins over the
    /// generic <c>500</c>. An empty set maps to <c>500</c>.
    /// </summary>
    public static int ToStatusCode(IEnumerable<Exception> exceptions)
    {
        var isTimeout = false;
        var isBadRequest = false;

        foreach (var exception in exceptions)
        {
            switch (exception)
            {
                case TimeoutException or OperationCanceledException:
                    isTimeout = true;
                    break;
                case HttpBadRequestException:
                    isBadRequest = true;
                    break;
            }
        }

        return isTimeout
            ? StatusCodes.Status408RequestTimeout
            : isBadRequest
                ? StatusCodes.Status400BadRequest
                : StatusCodes.Status500InternalServerError;
    }
}
