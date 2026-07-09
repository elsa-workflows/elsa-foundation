using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Exceptions;
using Elsa.Http.Core.Models;
using Microsoft.AspNetCore.Http;

namespace Elsa.Workflows.Runtime.Http.Services;

/// <summary>
/// A default fault handler that writes information about the fault to the <see cref="HttpResponse"/>.
/// </summary>
internal sealed class HttpEndpointFaultHandler : IHttpEndpointFaultHandler
{
    /// <inheritdoc />
    public ValueTask HandleAsync(HttpEndpointFaultContext context)
    {
        var httpContext = context.HttpContext;
        var isTimeoutIncident = GetIsTimeoutFault(context);
        var isBadRequest = GetIsBadRequestFault(context);
        var statusCode = isTimeoutIncident
            ? StatusCodes.Status408RequestTimeout
            : isBadRequest
                ? StatusCodes.Status400BadRequest
                : StatusCodes.Status500InternalServerError;

        httpContext.Response.StatusCode = statusCode;
        return ValueTask.CompletedTask;
    }

    private static bool GetIsTimeoutFault(HttpEndpointFaultContext context)
    {
        return ContainsException(context, typeof(OperationCanceledException), typeof(TaskCanceledException), typeof(TimeoutException));
    }

    private static bool GetIsBadRequestFault(HttpEndpointFaultContext context)
    {
        return ContainsException(context, typeof(HttpBadRequestException));
    }

    private static bool ContainsException(HttpEndpointFaultContext context, params Type[] exceptionTypes)
    {
        return context.Exceptions
            .Select(x => x.GetType())
            .Any(exceptionTypes.Contains);
    }
}