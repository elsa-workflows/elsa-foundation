using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Persistence.Core.Design;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Design.Api.Tests.Support;

/// <summary>
/// Scenario-keyed fakes for the definition domain seams the endpoints call directly.
/// </summary>
/// <remarks>
/// The endpoints own their handling, so tests observe binding and dispatch here — at the store and
/// lifecycle-command boundary — rather than at the retired mediator-sender seam. Scenarios ride the
/// same identity header the hosts already use.
/// </remarks>
public sealed class DefinitionDomainFakes(IHttpContextAccessor contextAccessor)
{
    public const string IdentityHeader = "X-Workflow-Design-Identity";

    private string Scenario => contextAccessor.HttpContext?.Request.Headers[IdentityHeader].ToString() ?? "";

    public string? LastReadDefinitionId { get; private set; }
    public WorkflowDefinition? LastSavedDefinition { get; private set; }
    public string? LastSaveOperationKey { get; private set; }
    public string? LastDeleteDefinitionId { get; private set; }
    public string? LastDeleteOperationKey { get; private set; }

    /// <summary>
    /// The sample is both restorable and soft-deletable: DeletedAt stays null so a soft delete
    /// proceeds, while a non-null DeletedReason satisfies the restore mutation's precondition.
    /// </summary>
    private static WorkflowDefinition Sample() => new()
    {
        Id = "sample-definition",
        Name = "Sample definition",
        DeletedAt = null,
        DeletedReason = "previously-deleted"
    };

    internal WorkflowDefinition Get(string id)
    {
        LastReadDefinitionId = id;
        if (Scenario == "trusted-not-found")
            throw new EntityNotFoundException("definition sample was not found");
        return Sample();
    }

    internal void Save(DesignOperationKey operationKey, WorkflowDefinition definition)
    {
        LastSaveOperationKey = operationKey.Value;
        LastSavedDefinition = definition;
    }

    internal void DeletePermanently(DesignOperationKey operationKey, string definitionId)
    {
        LastDeleteOperationKey = operationKey.Value;
        LastDeleteDefinitionId = definitionId;
        switch (Scenario)
        {
            case "trusted-delete-404": throw new EntityNotFoundException("definition sample was not found");
            case "trusted-delete-501": throw new PermanentDeletionUnavailableException("sample");
            case "trusted-delete-409": throw new WorkflowDefinitionNotSoftDeletedException("sample");
            case "trusted-delete-500": throw new InvalidOperationException("deterministic command failure");
        }
    }

    private sealed class DefinitionStore(DefinitionDomainFakes fakes) : IWorkflowDefinitionStore
    {
        public Task<WorkflowDefinition> GetAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult(fakes.Get(id));

        public Task<WorkflowDefinition?> FindByIdAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkflowDefinition?>(fakes.Get(id));

        public Task<IReadOnlyList<WorkflowDefinition>> ListAsync(WorkflowDefinitionFilter filter, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkflowDefinition>>([]);
    }

    private sealed class VersionStore : IWorkflowDefinitionVersionStore
    {
        private static WorkflowDefinitionVersion Sample(string versionId) => new("sample-definition", "1.0.0")
        {
            Id = versionId,
            State = WorkflowDefinitionState.Empty,
            Definition = new WorkflowDefinition { Id = "sample-definition", Name = "Sample definition" }
        };

        public Task<WorkflowDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Sample(versionId));
        public Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkflowDefinitionVersion?>(Sample(versionId));
        public Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Sample(versionId));
        public Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string definitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkflowDefinitionVersion?>(null);
        public Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkflowDefinitionVersion>>([]);
        public Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed class DraftStore : IWorkflowDefinitionDraftStore
    {
        public Task<WorkflowDefinitionDraft?> FindByIdAsync(string draftId, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkflowDefinitionDraft?>(null);
        public Task<WorkflowDefinitionDraft?> FindByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkflowDefinitionDraft?>(null);
        public Task<IReadOnlyList<WorkflowDefinitionDraft>> ListByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkflowDefinitionDraft>>([]);
        public Task<IReadOnlyCollection<DesignMetadataRecord>> FindLayoutByDraftIdAsync(string draftId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<DesignMetadataRecord>>([]);
        public Task<DraftWithLayout?> FindWithLayoutByIdAsync(string draftId, CancellationToken cancellationToken = default) =>
            Task.FromResult<DraftWithLayout?>(null);
    }

    private sealed class ProjectionStore : IWorkflowDefinitionListProjectionStore
    {
        public Task<IReadOnlyList<WorkflowDefinitionListProjection>> ListByDefinitionIdsAsync(
            IReadOnlyCollection<string> workflowDefinitionIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkflowDefinitionListProjection>>([]);
    }

    private sealed class SaveCommand(DefinitionDomainFakes fakes) : ISaveWorkflowDefinitionCommand
    {
        public Task Execute(DesignOperationKey operationKey, WorkflowDefinition definition, CancellationToken cancellationToken = default)
        {
            fakes.Save(operationKey, definition);
            return Task.CompletedTask;
        }
    }

    private sealed class DeleteCommand(DefinitionDomainFakes fakes) : IDeleteWorkflowDefinitionPermanentlyCommand
    {
        public Task Execute(DesignOperationKey operationKey, string definitionId, CancellationToken cancellationToken = default)
        {
            fakes.DeletePermanently(operationKey, definitionId);
            return Task.CompletedTask;
        }
    }

    private sealed class SubmitCommand : ISubmitWorkflowDefinitionCommand
    {
        public Task<SubmittedWorkflowDefinition> Execute(
            DesignOperationKey operationKey, string name, string? description, WorkflowDefinitionState state, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SubmittedWorkflowDefinition("sample-definition", "sample-draft", "sample-version"));
    }

    private sealed class AddCommand : IAddWorkflowDefinitionCommand
    {
        public Task<WorkflowDefinitionCreated> Execute(
            DesignOperationKey operationKey,
            WorkflowDefinition workflowDefinition,
            WorkflowDefinitionDraft draft,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new WorkflowDefinitionCreated(workflowDefinition.Id, draft.Id));
    }

    private sealed class UpdateDraft : IUpdateDraftCommand
    {
        public Task Execute(DesignOperationKey operationKey, UpdateDraftRequest request, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class SequentialIdentityGenerator : Elsa.Primitives.Contracts.IIdentityGenerator
    {
        private int _next;
        public string Generate() => $"generated-{Interlocked.Increment(ref _next)}";
    }

    public static void Register(IServiceCollection services)
    {
        services.AddSingleton<DefinitionDomainFakes>();
        services.AddSingleton<IWorkflowDefinitionStore>(sp => new DefinitionStore(sp.GetRequiredService<DefinitionDomainFakes>()));
        services.AddSingleton<IWorkflowDefinitionVersionStore, VersionStore>();
        services.AddSingleton<IWorkflowDefinitionDraftStore, DraftStore>();
        services.AddSingleton<IWorkflowDefinitionListProjectionStore, ProjectionStore>();
        services.AddSingleton<ISaveWorkflowDefinitionCommand>(sp => new SaveCommand(sp.GetRequiredService<DefinitionDomainFakes>()));
        services.AddSingleton<IDeleteWorkflowDefinitionPermanentlyCommand>(sp => new DeleteCommand(sp.GetRequiredService<DefinitionDomainFakes>()));
        services.AddSingleton<ISubmitWorkflowDefinitionCommand, SubmitCommand>();
        services.AddSingleton<IAddWorkflowDefinitionCommand, AddCommand>();
        services.AddSingleton<IUpdateDraftCommand, UpdateDraft>();
        services.AddSingleton(TimeProvider.System);

        // The Add endpoint composes its response from factory-created entities, so identity
        // generation must be deterministic or the pinned evidence would churn on every run.
        services.AddSingleton<Elsa.Primitives.Contracts.IIdentityGenerator, SequentialIdentityGenerator>();
        services.AddSingleton<Elsa.Workflows.Design.Core.Contracts.IWorkflowDefinitionFactory,
            Elsa.Workflows.Design.Persistence.Core.Services.WorkflowDefinitionFactory>();
        services.AddSingleton<Elsa.Workflows.Design.Core.Contracts.IWorkflowDefinitionDraftFactory,
            Elsa.Workflows.Design.Persistence.Core.Services.WorkflowDefinitionDraftFactory>();
    }
}
