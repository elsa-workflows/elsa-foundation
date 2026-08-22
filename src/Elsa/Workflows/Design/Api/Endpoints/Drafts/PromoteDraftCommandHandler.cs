using Elsa.Events.Core.Contracts;
using Elsa.Mediator.Core.Contracts;
using Elsa.Mediator.Core.Models;
using Elsa.Persistence.Core.Design;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Validations.Core;
using System.Text.Json;
using Elsa.Workflows.Design.Api.Endpoints.Versions;

namespace Elsa.Workflows.Design.Api.Endpoints.Drafts;

public sealed class PromoteDraftCommandHandler(
    IPromoteDraftToVersionCommand promoteCommand,
    IRequestSender requestSender)
    : ICommandHandler<PromoteDraft, WorkflowDefinitionVersionDetailsView>
{
    public async Task<WorkflowDefinitionVersionDetailsView> Handle(PromoteDraft command, CancellationToken cancellationToken)
    {
        var versionId = await promoteCommand.Execute(
            DesignOperationKey.CreateOrGenerate(command.OperationKey),
            command.DraftId,
            command.RequestedVersion,
            cancellationToken);
        return await requestSender.Send(new GetVersion(versionId), cancellationToken);
    }
}
