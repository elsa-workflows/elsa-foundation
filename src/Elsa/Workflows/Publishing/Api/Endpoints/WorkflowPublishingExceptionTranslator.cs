using Elsa.Api.AspNetCore;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Handlers;
using Elsa.Workflows.Publishing.Services;
using Microsoft.AspNetCore.Http;
using NativeEndpoints;

namespace Elsa.Workflows.Publishing.Api.Endpoints;

/// <summary>
/// The optimistic policy write lost its revision race. Raised by the policy endpoint and mapped to
/// the published 409 with this exact message.
/// </summary>
internal sealed class PublicationPolicyRevisionConflictException()
    : InvalidOperationException("The workflow publication policy revision changed.");

/// <summary>
/// Maps publishing domain exceptions onto the owner's legacy problem statuses.
/// </summary>
/// <remarks>
/// This replaces the per-endpoint catch ladders of the hand-written handlers. Order matters:
/// <see cref="PublicationPolicyResolutionException"/> derives from <see cref="ArgumentException"/>
/// and the publication lifecycle exceptions derive from <see cref="InvalidOperationException"/>, so
/// the specific arms sit above the general ones.
/// </remarks>
internal sealed class WorkflowPublishingExceptionTranslator : IEndpointExceptionTranslator
{
    public EndpointProblem? Translate(Exception exception) => exception switch
    {
        EntityNotFoundException => EndpointProblem.General(StatusCodes.Status404NotFound, exception.Message),
        PublicationPolicyRevisionConflictException or
            PublicationActivationException or
            PublicationPreflightConflictException or
            PublicationSnapshotReviewException =>
            EndpointProblem.General(StatusCodes.Status409Conflict, exception.Message),
        PublicationPolicyResolutionException policy =>
            EndpointProblem.General(
                policy.Code == "expected_publication_mismatch" ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest,
                exception.Message),
        ArgumentException => EndpointProblem.General(StatusCodes.Status400BadRequest, exception.Message),
        _ => null
    };
}
