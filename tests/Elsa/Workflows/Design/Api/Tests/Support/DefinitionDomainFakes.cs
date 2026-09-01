using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
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
    public string? LastDraftReadId { get; private set; }
    public string? LastDiscardDraftId { get; private set; }
    public string? LastDiscardOperationKey { get; private set; }
    public string? LastPromoteDraftId { get; private set; }
    public string? LastPromoteOperationKey { get; private set; }
    public string? LastPromoteRequestedVersion { get; private set; }
    public UpdateDraftRequest? LastDraftUpdate { get; private set; }
    public string? LastDraftUpdateOperationKey { get; private set; }

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

    private sealed class DraftStore(DefinitionDomainFakes fakes) : IWorkflowDefinitionDraftStore
    {
        /// <summary>
        /// Only route-supplied draft ids resolve. The expression-tooling paths probe this store with
        /// their own draft id and must keep seeing "not found" so their pinned evidence stays put.
        /// </summary>
        private static bool Serves(string draftId) => draftId is "sample" or "route-draft";

        private static WorkflowDefinitionDraft Draft(string draftId) => new()
        {
            Id = draftId,
            WorkflowDefinitionId = "sample-definition",
            State = WorkflowDefinitionState.Empty
        };

        public Task<WorkflowDefinitionDraft?> FindByIdAsync(string draftId, CancellationToken cancellationToken = default)
        {
            fakes.LastDraftReadId = draftId;
            return Task.FromResult(Serves(draftId) ? Draft(draftId) : null);
        }

        public Task<WorkflowDefinitionDraft?> FindByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkflowDefinitionDraft?>(null);
        public Task<IReadOnlyList<WorkflowDefinitionDraft>> ListByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkflowDefinitionDraft>>([]);
        public Task<IReadOnlyCollection<DesignMetadataRecord>> FindLayoutByDraftIdAsync(string draftId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<DesignMetadataRecord>>([]);

        public Task<DraftWithLayout?> FindWithLayoutByIdAsync(string draftId, CancellationToken cancellationToken = default)
        {
            fakes.LastDraftReadId = draftId;
            return Task.FromResult(Serves(draftId) ? new DraftWithLayout(Draft(draftId), []) : null);
        }
    }

    private sealed class VersionLayoutStore : IWorkflowDefinitionVersionLayoutStore
    {
        public Task<WorkflowDefinitionVersionLayout?> FindByVersionIdAsync(string workflowDefinitionVersionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkflowDefinitionVersionLayout?>(null);
    }

    private sealed class DiscardCommand(DefinitionDomainFakes fakes) : IDiscardDraftCommand
    {
        public Task Execute(DesignOperationKey operationKey, string draftId, CancellationToken cancellationToken = default)
        {
            fakes.LastDiscardOperationKey = operationKey.Value;
            fakes.LastDiscardDraftId = draftId;
            return Task.CompletedTask;
        }
    }

    private sealed class PromoteCommand(DefinitionDomainFakes fakes) : IPromoteDraftToVersionCommand
    {
        public Task<string> Execute(DesignOperationKey operationKey, string draftId, CancellationToken cancellationToken = default) =>
            Execute(operationKey, draftId, null, cancellationToken);

        public Task<string> Execute(DesignOperationKey operationKey, string draftId, string? requestedVersion, CancellationToken cancellationToken = default)
        {
            fakes.LastPromoteOperationKey = operationKey.Value;
            fakes.LastPromoteDraftId = draftId;
            fakes.LastPromoteRequestedVersion = requestedVersion;
            return fakes.Scenario switch
            {
                "trusted-promote-404" => throw new EntityNotFoundException("draft sample was not found"),
                "trusted-promote-409" => throw new WorkflowDefinitionVersionConflictException("definition sample", "1.0.0"),
                "trusted-promote-500" => throw new InvalidOperationException("deterministic command failure"),
                _ => Task.FromResult("sample-version")
            };
        }
    }

    private sealed class AddVersionCommand : IAddWorkflowDefinitionVersionCommand
    {
        public Task<WorkflowDefinitionVersionAdded> Execute(
            DesignOperationKey operationKey, string definitionId, WorkflowDefinitionState state, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WorkflowDefinitionVersionAdded(definitionId, "sample-version", "1.0.0"));
    }

    /// <summary>No validation contributors: every draft derives a deterministic empty error set.</summary>
    private sealed class NoOpInlineEventPublisher : Elsa.Events.Core.Contracts.IInlineEventPublisher
    {
        public Task Publish(Elsa.Events.Core.Contracts.IEvent @event, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
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

    private sealed class UpdateDraft(DefinitionDomainFakes fakes) : IUpdateDraftCommand
    {
        public Task Execute(DesignOperationKey operationKey, UpdateDraftRequest request, CancellationToken cancellationToken = default)
        {
            fakes.LastDraftUpdateOperationKey = operationKey.Value;
            fakes.LastDraftUpdate = request;
            return Task.CompletedTask;
        }
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
        services.AddSingleton<IWorkflowDefinitionDraftStore>(sp => new DraftStore(sp.GetRequiredService<DefinitionDomainFakes>()));
        services.AddSingleton<IWorkflowDefinitionVersionLayoutStore, VersionLayoutStore>();
        services.AddSingleton<IWorkflowDefinitionListProjectionStore, ProjectionStore>();
        services.AddSingleton<ISaveWorkflowDefinitionCommand>(sp => new SaveCommand(sp.GetRequiredService<DefinitionDomainFakes>()));
        services.AddSingleton<IDeleteWorkflowDefinitionPermanentlyCommand>(sp => new DeleteCommand(sp.GetRequiredService<DefinitionDomainFakes>()));
        services.AddSingleton<ISubmitWorkflowDefinitionCommand, SubmitCommand>();
        services.AddSingleton<IAddWorkflowDefinitionCommand, AddCommand>();
        services.AddSingleton<IUpdateDraftCommand>(sp => new UpdateDraft(sp.GetRequiredService<DefinitionDomainFakes>()));
        services.AddSingleton<IDiscardDraftCommand>(sp => new DiscardCommand(sp.GetRequiredService<DefinitionDomainFakes>()));
        services.AddSingleton<IAddWorkflowDefinitionVersionCommand, AddVersionCommand>();
        services.AddSingleton<IPromoteDraftToVersionCommand>(sp => new PromoteCommand(sp.GetRequiredService<DefinitionDomainFakes>()));
        services.AddSingleton<Elsa.Events.Core.Contracts.IInlineEventPublisher, NoOpInlineEventPublisher>();
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
