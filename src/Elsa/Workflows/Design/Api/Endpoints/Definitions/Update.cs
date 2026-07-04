using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Constants;
using Elsa.Workflows.Design.Api.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Design.Api.Endpoints.Definitions;

/// <summary>
/// <c>PUT design/workflows/definitions/{id}</c> — the DS-6 Draft mutation gate. The caller submits the
/// complete desired Draft state; the command diffs it against stored state and emits the per-concept mutation
/// events. Secured by construction through <c>ConfigurePermissions()</c> (W4): the mutation path is never
/// anonymous.
/// </summary>
internal sealed class Update(ICommandSender commandSender, ILogger<Update> logger)
    : ElsaCommandHandlerEndpoint<UpdateDefinition, WorkflowDefinitionDetailsView>(commandSender, logger)
{
    public override void Configure()
    {
        Put(RouteConstants.GetRoute("definitions/{id}"));
        ConfigurePermissions();
    }
}
