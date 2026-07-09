namespace Elsa.Workflows.Runtime.Http.Exceptions;

/// <summary>
/// Thrown at publish time when two distinct workflow definitions claim the same HTTP endpoint
/// <c>(template, method)</c> pair (spec 089 follow-up, issue #592 item 2). The HTTP route resolver runs a
/// full re-projection on every publish through <c>RouteTableTriggerIndexObserver</c>, whose exceptions fail the
/// publish; surfacing the collision here means the <em>second</em> publish of a conflicting endpoint fails the
/// author's publish rather than the ambiguity only appearing as a request-time 409.
/// </summary>
public sealed class EndpointRoutingConflictException(string endpoint)
    : Exception($"More than one workflow definition claims the HTTP endpoint '{endpoint}'. A (template, method) pair must be unique across definitions.")
{
    /// <summary>The conflicting endpoint, formatted as <c>METHOD template</c> (e.g. <c>GET orders/{id}</c>).</summary>
    public string Endpoint { get; } = endpoint;
}
