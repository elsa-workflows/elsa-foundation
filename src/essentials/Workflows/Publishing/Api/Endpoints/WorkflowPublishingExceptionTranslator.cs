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
    : InvalidOperationException("The workflow publication policy revision changed.")
{
    public string Code => PublicationFailureCodes.PolicyRevisionConflict;
}

/// <summary>
/// Maps publishing domain exceptions onto the owner's legacy problem statuses.
/// </summary>
/// <remarks>
/// This replaces the per-endpoint catch ladders of the hand-written handlers.
/// <see cref="PublicationPolicyRevisionConflictException"/>, <see cref="PublicationActivationException"/>,
/// <see cref="PublicationPreflightConflictException"/>, <see cref="PublicationSnapshotReviewException"/> and
/// <see cref="PublicationPolicyResolutionException"/> all carry a failure code, so
/// <see cref="WorkflowPublishingFaultRenderer"/> renders them with an <c>errorCode</c> extension before this
/// translator ever runs; only the codeless arms below remain here.
/// </remarks>
internal sealed class WorkflowPublishingExceptionTranslator : IEndpointExceptionTranslator
{
    public EndpointProblem? Translate(Exception exception) => exception switch
    {
        EntityNotFoundException => EndpointProblem.General(StatusCodes.Status404NotFound, exception.Message),
        ArgumentException => EndpointProblem.General(StatusCodes.Status400BadRequest, exception.Message),
        _ => null
    };
}
