using Elsa.Api.AspNetCore;
using Microsoft.AspNetCore.Builder;
using NativeEndpoints;

namespace Elsa.Workflows.Runtime.Api.Endpoints;

// The Runtime owner publishes three dispatch-failure families, and the hand-written mapper varied
// their catch ladders per endpoint. Each endpoint carries one shape marker naming its family and
// the per-endpoint arms, and the owner's single keyed fault renderer reproduces the exact ladder.
// The Operation value feeds the unchanged "Unexpected error while {Operation}." log line.

/// <summary>The generic runtime problem family (problem+json with charset, no trace id).</summary>
internal sealed class RuntimeProblemShapeMetadata(string operation, bool notFoundArms, bool executableArms, string? argumentDetail)
{
    public string Operation { get; } = operation;

    /// <summary>Maps missing-resource exceptions to a bare 404, as the shared request helper did.</summary>
    public bool NotFoundArms { get; } = notFoundArms;

    /// <summary>Maps the execute route's executable-lookup exceptions to 400/409 with their messages.</summary>
    public bool ExecutableArms { get; } = executableArms;

    /// <summary>Fixed 400 detail replacing an argument exception's own message, where the ladder did.</summary>
    public string? ArgumentDetail { get; } = argumentDetail;
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class RuntimeProblemsAttribute(string operation) : Attribute, IEndpointConventionAttribute
{
    public bool NotFoundArms { get; set; }
    public bool ExecutableArms { get; set; }
    public string? ArgumentDetail { get; set; }

    public void Apply(IEndpointConventionBuilder builder) =>
        builder.WithMetadata(new RuntimeProblemShapeMetadata(operation, NotFoundArms, ExecutableArms, ArgumentDetail));
}

/// <summary>The alteration problem family (fixed code/message/status tuples as plain JSON).</summary>
internal sealed class AlterationProblemShapeMetadata(string operation, string argumentCode, string argumentMessage, bool entityNotFoundArm, bool submit)
{
    public string Operation { get; } = operation;
    public string ArgumentCode { get; } = argumentCode;
    public string ArgumentMessage { get; } = argumentMessage;

    /// <summary>Maps <c>EntityNotFoundException</c> to a bare 404; the cancel ladder had no such arm.</summary>
    public bool EntityNotFoundArm { get; } = entityNotFoundArm;

    /// <summary>The submission ladder: admission backpressure, idempotency conflict, and 422 arms.</summary>
    public bool Submit { get; } = submit;
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class AlterationProblemsAttribute(string operation, string argumentCode, string argumentMessage) : Attribute, IEndpointConventionAttribute
{
    public bool EntityNotFoundArm { get; set; }

    public void Apply(IEndpointConventionBuilder builder) =>
        builder.WithMetadata(new AlterationProblemShapeMetadata(operation, argumentCode, argumentMessage, EntityNotFoundArm, submit: false));
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class AlterationSubmitProblemsAttribute : Attribute, IEndpointConventionAttribute
{
    public void Apply(IEndpointConventionBuilder builder) =>
        builder.WithMetadata(new AlterationProblemShapeMetadata(
            "submitting runtime alteration plan", "InvalidIdempotencyKey", "The alteration request is invalid.", entityNotFoundArm: false, submit: true));
}

/// <summary>The activity-inspection problem family (bare problem+json with trace id and error codes).</summary>
internal sealed class ActivityInspectionProblemShapeMetadata(string operation)
{
    public string Operation { get; } = operation;
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class ActivityInspectionProblemsAttribute(string operation) : Attribute, IEndpointConventionAttribute
{
    public void Apply(IEndpointConventionBuilder builder) =>
        builder.WithMetadata(new ActivityInspectionProblemShapeMetadata(operation));
}
