using Elsa.Mediator.Core.Contracts;
using Elsa.Persistence.Core;
using Elsa.Primitives.Versioning;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;

namespace Elsa.Workflows.Design.Api.Handlers;

public sealed class AddVersionCommandHandler(
    IWorkflowDefinitionVersionFactory versionFactory,
    IAddCommand<WorkflowDefinitionVersion> addCommand,
    IWorkflowDefinitionVersionStore versionStore,
    IWorkflowDefinitionStore definitionStore)

    : ICommandHandler<AddVersion, WorkflowDefinitionVersionDetailsView>
{
    public async Task<WorkflowDefinitionVersionDetailsView> Handle(AddVersion command, CancellationToken cancellationToken)
    {
        var definition = await definitionStore.GetAsync(command.DefinitionId, cancellationToken);
        var lastVersion = await versionStore.FindLatestVersionAsync(command.DefinitionId, cancellationToken);

        var version = versionFactory.Create(definition, NextVersion(lastVersion?.Version), command.State.ToState());

        await addCommand.Add(WorkflowDefinitionVersion.From(version), cancellationToken);

        var addedVersion = await versionStore.GetWithDefinitionAsync(version.Id, cancellationToken);
        return addedVersion.ToDetailsView();
    }

    // Each published workflow version is a new major (1.0.0 → 2.0.0 → …).
    private static string NextVersion(string? lastVersion) =>
        lastVersion is not null && SemVer.TryParse(lastVersion, out var semVer)
            ? $"{semVer.Major + 1}.0.0"
            : "1.0.0";
}
