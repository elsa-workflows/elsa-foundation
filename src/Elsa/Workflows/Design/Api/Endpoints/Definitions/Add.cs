using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Constants;
using Elsa.Workflows.Design.Api.Models;
using Microsoft.Extensions.Logging;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;

namespace Elsa.Workflows.Design.Api.Endpoints.Definitions;

internal sealed class Add(ICommandSender commandSender, ILogger<Add> logger) : ElsaEndpoint<AddDefinition, WorkflowDefinitionDetailsView>
{
    public override void Configure()
    {
        Post(RouteConstants.Definitions);
        ConfigurePermissions(PermissionNames.WorkflowDesignManage);
    }

    public override async Task HandleAsync(AddDefinition request, CancellationToken cancellationToken)
    {
        try { await Send.ResponseAsync(await commandSender.Send(request, cancellationToken), 201, cancellationToken); }
        catch (WorkflowFolderRestructureConflictException exception) { ThrowError(exception.Message, 409); }
        catch (EntityNotFoundException exception) { ThrowError(exception.Message, 404); }
        catch (ArgumentException exception) { ThrowError(exception, 400); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { logger.LogError(exception, "Unexpected error occurred when adding a workflow definition"); ThrowError("Unexpected error occurred", 500); }
    }
}
