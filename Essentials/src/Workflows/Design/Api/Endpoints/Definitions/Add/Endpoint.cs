using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Validations.Core;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using NativeEndpoints;
using Elsa.Workflows.Design.Api.Services;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Primitives.Exceptions;
using System.Threading;
using System.Threading.Tasks;

namespace Elsa.Workflows.Design.Api.Endpoints.Definitions.Add;

[Post("definitions")]
[RequirePermission(WorkflowDesignPermissions.Manage)]
public sealed class Endpoint(
    IWorkflowDefinitionFactory definitionFactory,
    IWorkflowDefinitionDraftFactory draftFactory,
    IAddWorkflowDefinitionCommand addCommand,
    ICredentialLiteralValidator credentialLiterals,
    IWorkflowLibraryStore? library = null) : ApiEndpoint<AddDefinition, WorkflowDefinitionDetailsView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DefinitionsAdd";
        options.Accepts = ["application/json"];
    }

    public override async Task<WorkflowDefinitionDetailsView> HandleAsync(AddDefinition command, CancellationToken cancellationToken)
    {
        if (command.FolderId is not null)
        {
            var folders = WorkflowLibraryAccess.Require(library);
            await WorkflowLibraryAccess.RequireAsync(HttpContext, WorkflowDesignPermissions.Manage,
                new WorkflowLibraryResource(FolderId: command.FolderId), cancellationToken);
            // The create response includes the home-folder breadcrumb. Check it before
            // committing so a caller cannot create successfully and then discover a read denial.
            var destination = await folders.GetFolderAsync(command.FolderId, cancellationToken)
                ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowFolder), command.FolderId);
            await WorkflowLibraryAccess.RequireFolderReadAsync(HttpContext,
                destination.Ancestors.Select(folder => folder.Id).Append(destination.Folder.Id), cancellationToken);
        }
        var state = (command.InitialState ?? new WorkflowDefinitionStateView()).ToState();
        await credentialLiterals.AdmitAsync(state, cancellationToken);
        var definition = definitionFactory.Create(command.Name, command.Description);
        if (library is not null)
            await WorkflowLibraryAccess.RequireAsync(HttpContext, WorkflowDesignPermissions.Read,
                new WorkflowLibraryResource(DefinitionId: definition.Id), cancellationToken);
        var draft = draftFactory.Create(definition.Id, state);
        var draftEntity = WorkflowDefinitionDraft.From(draft);
        var layout = (command.Layout ?? [])
            .Select(record => new DesignMetadataRecord(
                record.NodeId,
                record.X,
                record.Y,
                record.Width,
                record.Height,
                record.AdditionalProperties))
            .ToArray();
        var activityPresentation = ActivityPresentationRecord.NormalizeCollection(
            (command.ActivityPresentation ?? []).Select(x => x.ToRecord()));

        var result = await addCommand.Execute(
            DesignOperationKey.CreateOrGenerate(command.OperationKey),
            WorkflowDefinition.From(definition),
            draftEntity,
            layout,
            activityPresentation,
            command.FolderId,
            command.FolderId is null ? string.Empty : (await WorkflowLibraryAccess.SubjectAsync(HttpContext, true, cancellationToken))!,
            cancellationToken);

        var authoritativeDefinition = WorkflowDefinition.From(definition);
        authoritativeDefinition.Id = result.DefinitionId;
        var authoritativeDraft = WorkflowDefinitionDraft.From(draft);
        authoritativeDraft.Id = result.DraftId;
        authoritativeDraft.WorkflowDefinitionId = result.DefinitionId;
        var definitionView = authoritativeDefinition.ToView();
        if (library is not null && await library.GetOrganizationAsync(result.DefinitionId, cancellationToken) is { } organization)
        {
            await WorkflowLibraryAccess.RequireOrganizationReadAsync(HttpContext, organization, cancellationToken);
            definitionView = WorkflowLibraryProjection.WithOrganization(definitionView, organization);
        }
        return new WorkflowDefinitionDetailsView(
            definitionView,
            WorkflowDraftView.From(authoritativeDraft, layout, activityPresentation),
            Versions: []);
    }
}
