namespace Elsa.Http.Core;

/// <summary>
/// How an HTTP endpoint delivers the workflow-authored response for a request (spec 089 sub-unit E, E-D1).
/// Lives in the lower HTTP contracts package alongside <see cref="HttpEndpointRouting"/> because both the
/// activity side (which stamps the mode onto trigger-binding/bookmark metadata) and the runtime/middleware side
/// (which reads it back to decide the response strategy) speak it. NON-IDENTITY: the mode rides the binding
/// metadata but never participates in <c>HttpEndpointStimulus.Hash</c> — two endpoints that differ only in
/// response mode share a routing key.
/// </summary>
public enum ResponseMode
{
    /// <summary>
    /// The async/202 baseline (the default, FR-018): the endpoint replies <c>202 Accepted</c> the moment it
    /// starts (or resumes) the workflow, and a <c>WriteHttpResponse</c> records a durable artifact rather than
    /// writing a live response. Bit-for-bit today's contract; the default so an unauthored endpoint is unchanged.
    /// </summary>
    Async = 0,

    /// <summary>
    /// The synchronous response mode (FR-019): the middleware holds the request open on the caller's async flow
    /// while the dispatch drains inline, so a <c>WriteHttpResponse</c> the run reaches can write the live
    /// response in the same exchange. Degrades to <c>202 Accepted</c> when the workflow suspends before writing
    /// a response, writes no response, or dispatches non-locally (spec 089 E-D5).
    /// </summary>
    Sync
}
