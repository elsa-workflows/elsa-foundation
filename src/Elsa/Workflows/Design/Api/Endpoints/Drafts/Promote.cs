using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Constants;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using FluentValidation.Results;
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
            // Enrich the 409 with the full violation list so clients get the details inline. The
            // summary message (and its embedded count) is preserved as a general error for
            // backward compatibility; each violation is keyed by its R2 path with the R3 category
            // as the error code.
            AddError(exception.Message);
            foreach (var error in exception.Errors)
                AddError(new ValidationFailure(error.Path, error.Message) { ErrorCode = error.Type });
            await Send.ErrorsAsync(409, cancellationToken);
        }
        catch (EntityNotFoundException exception)
        {
            ThrowError(exception.Message, 404);
        }
        catch (WorkflowDefinitionVersionConflictException exception)
        {
            ThrowError(exception.Message, 409);
        }
        catch (WorkflowPromotionOperationConflictException exception)
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
