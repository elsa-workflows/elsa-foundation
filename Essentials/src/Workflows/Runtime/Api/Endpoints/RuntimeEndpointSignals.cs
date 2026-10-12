namespace Elsa.Workflows.Runtime.Api.Endpoints;

// Internal control flow only: endpoints throw these for the non-success outcomes the hand-written
// mapper decided inline — a bare 404, a pre-dispatch alteration rejection, or an activity-inspection
// problem document — and the owner's fault renderer turns each into the exact published response.
// They never cross the module boundary and carry no caller-visible details of their own.

/// <summary>A resource read resolved to nothing: the published response is a bare 404.</summary>
internal sealed class RuntimeResourceMissingSignal : Exception;

/// <summary>An alteration request was rejected before dispatch, with a fixed problem tuple.</summary>
internal sealed class RuntimeAlterationRequestRejectedSignal(string code, string message) : Exception
{
    public string Code { get; } = code;
    public string ProblemMessage { get; } = message;
}

/// <summary>An activity-execution read resolved to nothing: the published response is the 404 problem document.</summary>
internal sealed class ActivityExecutionMissingSignal : Exception;

/// <summary>A value-payload resolution was denied: the published response is the 403 problem document.</summary>
internal sealed class ActivityValuePayloadDeniedSignal : Exception;

/// <summary>A value payload was never captured: the published response is the 409 problem document.</summary>
internal sealed class ActivityValuePayloadUnavailableSignal : Exception;
