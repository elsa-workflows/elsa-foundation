using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Constants;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Api.FastEndpoints.Constants;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Design.Api.Endpoints.Versions;

/// <summary>
/// Explicit ingestion path for provisioners and migration tools. Normal authoring creates immutable versions
/// only by promoting a Draft.
/// </summary>
internal sealed class Add(ICommandSender commandSender, ILogger<Add> logger) : ElsaCommandHandlerEndpoint<AddVersion, WorkflowDefinitionVersionDetailsView>(commandSender, logger)
{
    public override void Configure()
    {
        Post(RouteConstants.GetRoute("versions/ingest"));
        ConfigurePermissions(PermissionNames.WorkflowDesignManage);
    }
}
