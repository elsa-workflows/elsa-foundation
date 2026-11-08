using Elsa.Api.AspNetCore;
using Microsoft.AspNetCore.Builder;
using NativeEndpoints;

namespace Elsa.Activities.Design.Api.Endpoints;

/// <summary>
/// Marks which of the owner's two published failure shapes an endpoint renders.
/// </summary>
/// <remarks>
/// The availability, catalog, and capability routes kept the historical mediator error transport
/// (RFC 7807 with <c>traceId</c> and <c>errors</c> extensions as <c>application/problem+json</c>),
/// while every authoring operation renders <see cref="Models.ActivityProblemDetailsView"/> as
/// <c>application/json; charset=utf-8</c>. Both unexpected-failure shapes differ from the shared
/// pipeline default, so every endpoint declares its family explicitly and
/// <see cref="ActivitiesDesignFaultRenderer"/> owns all dispatch failures for both.
/// </remarks>
internal sealed class ActivityDesignProblemShapeMetadata
{
    public static readonly ActivityDesignProblemShapeMetadata Legacy = new(isLegacy: true);
    public static readonly ActivityDesignProblemShapeMetadata Authoring = new(isLegacy: false);

    private ActivityDesignProblemShapeMetadata(bool isLegacy) => IsLegacy = isLegacy;

    public bool IsLegacy { get; }
}

/// <summary>Failures on this endpoint render in the historical mediator transport shape.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class LegacyProblemsAttribute : Attribute, IEndpointConventionAttribute
{
    public void Apply(IEndpointConventionBuilder builder) =>
        builder.WithMetadata(ActivityDesignProblemShapeMetadata.Legacy);
}

/// <summary>Failures on this endpoint render as <see cref="Models.ActivityProblemDetailsView"/>.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class AuthoringProblemsAttribute : Attribute, IEndpointConventionAttribute
{
    public void Apply(IEndpointConventionBuilder builder) =>
        builder.WithMetadata(ActivityDesignProblemShapeMetadata.Authoring);
}
