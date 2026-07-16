using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Constants;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Design.Api.Endpoints.Drafts;

internal sealed class Promote(ICommandSender commandSender, ILogger<Promote> logger)
    : ElsaEndpoint<PromoteDraft, WorkflowDefinitionVersionDetailsView>
{
    public override void Configure()
    {
        Post(RouteConstants.GetRoute("drafts/{draftId}/promote"));
        ConfigurePermissions(PermissionNames.WorkflowDesignManage);
    }

    public override async Task HandleAsync(PromoteDraft request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await commandSender.Send(request, cancellationToken);
            await Send.ResponseAsync(response, 201, cancellationToken);
        }
        catch (DraftHasValidationErrorsException exception)
        {
            ThrowError(exception.Message, 409);
        }
        catch (ArgumentException exception)
        {
            ThrowError(exception, 400);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unexpected error occurred when promoting workflow draft '{draftId}'", request.DraftId);
            ThrowError("Unexpected error occurred", 500);
        }
    }
}
