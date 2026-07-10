using Elsa.Http.Core;
using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;
using Microsoft.AspNetCore.Http;

namespace Elsa.Workflows.Runtime.Http.Services;

/// <summary>
/// A default fault handler that writes information about the fault to the <see cref="HttpResponse"/>. The
/// fault → status mapping is owned by <see cref="HttpEndpointFaultMapping"/> so it stays identical to the
/// middleware's inline fallback (spec 089 sub-unit C, follow-up #592 item 13).
/// </summary>
/// <remarks>
/// Public (like the shipped <c>IHttpEndpointAuthorizationHandler</c> implementations, spec 089 C T008) so the
/// integration fixture can register it explicitly — the fixture composes services directly rather than via the
/// reflective feature loader, and this assembly exposes no <c>InternalsVisibleTo</c> to the test project.
/// </remarks>
public sealed class HttpEndpointFaultHandler : IHttpEndpointFaultHandler
{
    /// <inheritdoc />
    public ValueTask HandleAsync(HttpEndpointFaultContext context)
    {
        context.HttpContext.Response.StatusCode = HttpEndpointFaultMapping.ToStatusCode(context.Exceptions);
        return ValueTask.CompletedTask;
    }
}
