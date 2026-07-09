using Microsoft.AspNetCore.Http;

namespace Elsa.Http.Core.Models;

/// <summary>
/// Provides context about a faulted HTTP endpoint dispatch (spec 089 sub-unit C).
/// </summary>
/// <param name="HttpContext">The HTTP context of the inbound request.</param>
/// <param name="Exceptions">The exception(s) raised while dispatching the request.</param>
/// <param name="CancellationToken">The cancellation token.</param>
public record HttpEndpointFaultContext(HttpContext HttpContext, IEnumerable<Exception> Exceptions, CancellationToken CancellationToken);
