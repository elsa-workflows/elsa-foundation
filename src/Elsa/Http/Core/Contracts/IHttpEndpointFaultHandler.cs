using Elsa.Http.Core.Models;

namespace Elsa.Http.Core.Contracts;

/// <summary>
/// Implement this to control what to return to the client in case an unhandled exception occurs while dispatching
/// an inbound HTTP request. Contract lives in <c>Elsa.Http.Core</c> (spec 089 sub-unit C); the default
/// implementation ships from <c>Elsa.Workflows.Runtime.Http</c>.
/// </summary>
public interface IHttpEndpointFaultHandler
{
    ValueTask HandleAsync(HttpEndpointFaultContext context);
}
