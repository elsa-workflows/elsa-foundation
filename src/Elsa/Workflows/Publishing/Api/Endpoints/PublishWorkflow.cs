using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Publishing.Api.Constants;
using Elsa.Workflows.Publishing.Api.Handlers;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Models;
using Microsoft.Extensions.Logging;
using PublishWorkflowCommand = Elsa.Workflows.Publishing.Api.Requests.PublishWorkflow;
using PublishWorkflowRequest = Elsa.Workflows.Publishing.Api.Requests.PublishWorkflowRequest;

namespace Elsa.Workflows.Publishing.Api.Endpoints;

internal sealed class PublishWorkflowEndpoint(IRequestSender requestSender, ILogger<PublishWorkflowEndpoint> logger)
    : ElsaEndpoint<PublishWorkflowRequest, PublishedWorkflowView>
{
    public override void Configure()
    {
        Post(RouteConstants.WorkflowPublish);
        ConfigurePermissions(PermissionNames.WorkflowPublishingManage);
    }

    public override async Task HandleAsync(PublishWorkflowRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await requestSender.Send(
                new PublishWorkflowCommand(
                    request.VersionId,
                    request.Action is { } action ? PublicationIntentContract.ToModel(action) : null,
                    request.SlotName,
                    request.ExpectedPublicationId,
                    request.PreflightToken),
                cancellationToken);
            await Send.ResponseAsync(response, response.WasCreated ? 201 : 200, cancellationToken);
        }
        catch (EntityNotFoundException exception)
        {
            ThrowError(exception.Message, 404);
        }
        catch (PublicationPreflightConflictException exception)
        {
            ThrowError(exception.Message, 409);
        }
        catch (PublicationSnapshotReviewException exception)
        {
            ThrowError(exception.Message, 409);
        }
        catch (PublicationActivationException exception)
        {
            ThrowError(exception.Message, 409);
        }
        catch (PublicationPolicyResolutionException exception)
        {
            ThrowError(exception.Message, exception.Code == "expected_publication_mismatch" ? 409 : 400);
        }
        catch (ArgumentException exception)
        {
            ThrowError(exception.Message, 400);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unexpected workflow publication failure for version '{VersionId}'.", request.VersionId);
            ThrowError("Unexpected error occurred", 500);
        }
    }
}
