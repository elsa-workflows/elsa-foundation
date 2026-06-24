using System.Text.Json;
using CShells.Lifecycle;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Flowchart.Activities;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Sequence.Activities;
using Elsa.Activities.Sequence.Models;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Runtime.Api.Requests;

namespace Elsa.Server;

internal static class ElsaWorkflowManagementApi
{
    private const string DefaultShellName = "default";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapElsaWorkflowManagementApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/_elsa/workflow-management");

        group.MapGet("/definitions", ListDefinitionsAsync);
        group.MapGet("/definitions/{definitionId}", GetDefinitionAsync);
        group.MapPost("/definitions", CreateDefinitionAsync);
        group.MapDelete("/definitions/{definitionId}", DeleteDefinitionAsync);
        group.MapPost("/definitions/{definitionId}/restore", RestoreDefinitionAsync);
        group.MapDelete("/definitions/{definitionId}/permanent", DeleteDefinitionPermanentlyAsync);
        group.MapPut("/drafts/{draftId}", UpdateDraftAsync);
        group.MapPost("/drafts/{draftId}/promote", PromoteDraftAsync);
        group.MapDelete("/drafts/{draftId}", DiscardDraftAsync);
        group.MapPost("/versions/{versionId}/publish", PublishVersionAsync);
        group.MapPost("/executables/{artifactId}/run", RunExecutableAsync);
        group.MapGet("/activities", ListActivitiesAsync);

        return endpoints;
    }

    private static Task<IResult> ListDefinitionsAsync(IShellRegistry shellRegistry, string? search, string? state, CancellationToken cancellationToken) =>
        WithShellAsync(shellRegistry, async services =>
        {
            var definitionStore = services.GetRequiredService<IWorkflowDefinitionStore>();
            var draftStore = services.GetRequiredService<IWorkflowDefinitionDraftStore>();
            var versionStore = services.GetRequiredService<IWorkflowDefinitionVersionStore>();

            var definitions = await definitionStore.ListAsync(new WorkflowDefinitionFilter { SearchTerm = search }, cancellationToken);
            definitions = NormalizeDefinitionListState(state) switch
            {
                WorkflowDefinitionListStates.Deleted => definitions.Where(x => x.DeletedAt != null).ToArray(),
                WorkflowDefinitionListStates.All => definitions,
                _ => definitions.Where(x => x.DeletedAt == null).ToArray()
            };

            var summaries = new List<WorkflowDefinitionSummaryResponse>(definitions.Count);
            foreach (var definition in definitions.OrderBy(x => x.Name, StringComparer.Ordinal).ThenBy(x => x.Id, StringComparer.Ordinal))
            {
                var draft = await draftStore.FindByWorkflowDefinitionIdAsync(definition.Id, cancellationToken);
                var versions = await versionStore.ListByDefinitionAsync(definition.Id, cancellationToken);
                var latestVersion = versions.OrderByDescending(x => x.SemVerSortKey, StringComparer.Ordinal).FirstOrDefault();

                summaries.Add(new WorkflowDefinitionSummaryResponse(
                    definition.Id,
                    definition.Name,
                    definition.Description,
                    definition.CreatedAt,
                    definition.LastModifiedAt,
                    definition.DeletedAt,
                    draft?.Id,
                    latestVersion?.Id,
                    latestVersion?.Version,
                    versions.Count));
            }

            return Results.Ok(new WorkflowDefinitionsResponse(summaries));
        }, cancellationToken);

    private static Task<IResult> GetDefinitionAsync(IShellRegistry shellRegistry, string definitionId, CancellationToken cancellationToken) =>
        WithShellAsync(shellRegistry, services => LoadDefinitionResultAsync(services, definitionId, cancellationToken), cancellationToken);

    private static Task<IResult> CreateDefinitionAsync(IShellRegistry shellRegistry, CreateWorkflowDefinitionRequest request, CancellationToken cancellationToken) =>
        WithShellAsync(shellRegistry, async services =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new WorkflowManagementErrorResponse("A workflow name is required."));

            var activity = await CreateRootActivityAsync(services, request.RootActivityVersionId, request.RootKind, cancellationToken);
            if (activity is null)
                return Results.BadRequest(new WorkflowManagementErrorResponse(GetMissingRootActivityMessage(request)));

            var state = new WorkflowDefinitionState(
                Variables: [],
                RootActivity: activity,
                Inputs: [],
                Outputs: [],
                WorkflowActivityOptions: null,
                StrategyOptions: null);

            var submit = services.GetRequiredService<ISubmitWorkflowDefinitionCommand>();
            var submitted = await submit.Execute(request.Name.Trim(), request.Description, state, cancellationToken);

            return await LoadDefinitionResultAsync(services, submitted.DefinitionId, cancellationToken);
        }, cancellationToken);

    private static Task<IResult> DeleteDefinitionAsync(IShellRegistry shellRegistry, string definitionId, CancellationToken cancellationToken) =>
        WithShellAsync(shellRegistry, async services =>
        {
            var definitionStore = services.GetRequiredService<IWorkflowDefinitionStore>();
            var definition = await definitionStore.FindByIdAsync(definitionId, cancellationToken);
            if (definition is null)
                return Results.NotFound(new WorkflowManagementErrorResponse($"Workflow definition '{definitionId}' was not found."));

            if (definition.DeletedAt is null)
                definition.DeletedAt = DateTimeOffset.UtcNow;

            definition.DeletedReason = null;
            await SaveWorkflowDefinitionAsync(services, definition, cancellationToken);
            return Results.NoContent();
        }, cancellationToken);

    private static Task<IResult> RestoreDefinitionAsync(IShellRegistry shellRegistry, string definitionId, CancellationToken cancellationToken) =>
        WithShellAsync(shellRegistry, async services =>
        {
            var definitionStore = services.GetRequiredService<IWorkflowDefinitionStore>();
            var definition = await definitionStore.FindByIdAsync(definitionId, cancellationToken);
            if (definition is null)
                return Results.NotFound(new WorkflowManagementErrorResponse($"Workflow definition '{definitionId}' was not found."));

            definition.DeletedAt = null;
            definition.DeletedReason = null;
            await SaveWorkflowDefinitionAsync(services, definition, cancellationToken);
            return Results.NoContent();
        }, cancellationToken);

    private static Task<IResult> DeleteDefinitionPermanentlyAsync(IShellRegistry shellRegistry, string definitionId, CancellationToken cancellationToken) =>
        WithShellAsync(shellRegistry, async services =>
        {
            var definitionStore = services.GetRequiredService<IWorkflowDefinitionStore>();

            var definition = await definitionStore.FindByIdAsync(definitionId, cancellationToken);
            if (definition is null)
                return Results.NotFound(new WorkflowManagementErrorResponse($"Workflow definition '{definitionId}' was not found."));

            if (definition.DeletedAt is null)
                return Results.BadRequest(new WorkflowManagementErrorResponse("Only deleted workflow definitions can be permanently deleted."));

            var delete = services.GetRequiredService<IDeleteWorkflowDefinitionPermanentlyCommand>();
            await delete.Execute(definitionId, cancellationToken);

            return Results.NoContent();
        }, cancellationToken);

    private static Task<IResult> UpdateDraftAsync(IShellRegistry shellRegistry, string draftId, UpdateWorkflowDraftRequest request, CancellationToken cancellationToken) =>
        WithShellAsync(shellRegistry, async services =>
        {
            var update = services.GetRequiredService<IUpdateDraftCommand>();
            await update.Execute(new UpdateDraftRequest(draftId, request.State.ToState(), request.Layout), cancellationToken);
            return await LoadDraftResultAsync(services, draftId, cancellationToken);
        }, cancellationToken);

    private static Task<IResult> PromoteDraftAsync(IShellRegistry shellRegistry, string draftId, CancellationToken cancellationToken) =>
        WithShellAsync(shellRegistry, async services =>
        {
            var promote = services.GetRequiredService<IPromoteDraftToVersionCommand>();
            var versionId = await promote.Execute(draftId, cancellationToken);
            return Results.Ok(new PromoteDraftResponse(versionId));
        }, cancellationToken);

    private static Task<IResult> DiscardDraftAsync(IShellRegistry shellRegistry, string draftId, CancellationToken cancellationToken) =>
        WithShellAsync(shellRegistry, async services =>
        {
            var discard = services.GetRequiredService<IDiscardDraftCommand>();
            await discard.Execute(draftId, cancellationToken);
            return Results.NoContent();
        }, cancellationToken);

    private static Task<IResult> PublishVersionAsync(IShellRegistry shellRegistry, string versionId, CancellationToken cancellationToken) =>
        WithShellAsync(shellRegistry, async services =>
        {
            var sender = services.GetRequiredService<IRequestSender>();
            var published = await sender.Send(new PublishWorkflow(versionId), cancellationToken);
            return Results.Ok(published);
        }, cancellationToken);

    private static Task<IResult> RunExecutableAsync(IShellRegistry shellRegistry, string artifactId, CancellationToken cancellationToken) =>
        WithShellAsync(shellRegistry, async services =>
        {
            var sender = services.GetRequiredService<IRequestSender>();
            var dispatch = await sender.Send(new ExecuteWorkflow(artifactId), cancellationToken);
            return Results.Ok(dispatch);
        }, cancellationToken);

    private static Task<IResult> ListActivitiesAsync(IShellRegistry shellRegistry, CancellationToken cancellationToken) =>
        WithShellAsync(shellRegistry, async services =>
        {
            var versionStore = services.GetRequiredService<IActivityDefinitionVersionStore>();
            var definitionStore = services.GetRequiredService<IActivityDefinitionStore>();
            var versions = await versionStore.ListAsync(cancellationToken);

            foreach (var version in versions)
            {
                if (version.Definition is null)
                    version.Definition = await definitionStore.GetAsync(version.DefinitionId, cancellationToken);
            }

            var response = versions
                .Where(x => x.Definition is not null)
                .OrderBy(x => x.Definition!.Category, StringComparer.Ordinal)
                .ThenBy(x => x.Definition!.DisplayName, StringComparer.Ordinal)
                .ThenBy(x => x.Id, StringComparer.Ordinal)
                .Select(x => new ActivityCatalogItemResponse(
                    x.Id,
                    x.Definition!.ActivityTypeKey,
                    x.Version,
                    x.Definition.Category,
                    x.Definition.DisplayName ?? x.Definition.ActivityTypeKey,
                    x.Definition.Description,
                    x.ExecutionType.ToString(),
                    x.Inputs.ToArray(),
                    x.Outputs.ToArray(),
                    x.DesignFacets.ToArray()))
                .ToArray();

            return Results.Ok(new ActivityCatalogResponse(response));
        }, cancellationToken);

    private static async Task<IResult> LoadDefinitionResultAsync(IServiceProvider services, string definitionId, CancellationToken cancellationToken)
    {
        var definitionStore = services.GetRequiredService<IWorkflowDefinitionStore>();
        var draftStore = services.GetRequiredService<IWorkflowDefinitionDraftStore>();
        var versionStore = services.GetRequiredService<IWorkflowDefinitionVersionStore>();

        var definition = await definitionStore.FindByIdAsync(definitionId, cancellationToken);
        if (definition is null || definition.DeletedAt is not null)
            return Results.NotFound(new WorkflowManagementErrorResponse($"Workflow definition '{definitionId}' was not found."));

        var draft = await draftStore.FindByWorkflowDefinitionIdAsync(definitionId, cancellationToken);
        var versions = (await versionStore.ListByDefinitionAsync(definitionId, cancellationToken))
            .OrderByDescending(x => x.SemVerSortKey, StringComparer.Ordinal)
            .Select(x => new WorkflowVersionSummaryResponse(x.Id, x.Version, x.CreatedAt))
            .ToArray();

        WorkflowDraftResponse? draftResponse = null;
        if (draft is not null)
            draftResponse = await LoadDraftAsync(draftStore, draft, cancellationToken);

        return Results.Ok(new WorkflowDefinitionDetailsResponse(
            new WorkflowDefinitionSummaryResponse(
                definition.Id,
                definition.Name,
                definition.Description,
                definition.CreatedAt,
                definition.LastModifiedAt,
                definition.DeletedAt,
                draft?.Id,
                versions.FirstOrDefault()?.Id,
                versions.FirstOrDefault()?.Version,
                versions.Length),
            draftResponse,
            versions));
    }

    private static async Task<IResult> LoadDraftResultAsync(IServiceProvider services, string draftId, CancellationToken cancellationToken)
    {
        var draftStore = services.GetRequiredService<IWorkflowDefinitionDraftStore>();
        var draft = await draftStore.FindByIdAsync(draftId, cancellationToken);
        if (draft is not null)
            return Results.Ok(await LoadDraftAsync(draftStore, draft, cancellationToken));

        return Results.NotFound(new WorkflowManagementErrorResponse($"Workflow definition draft '{draftId}' was not found."));
    }

    private static async Task<WorkflowDraftResponse> LoadDraftAsync(IWorkflowDefinitionDraftStore draftStore, WorkflowDefinitionDraft draft, CancellationToken cancellationToken)
    {
        var layout = await draftStore.FindLayoutByDraftIdAsync(draft.Id, cancellationToken);
        var errors = await draftStore.FindValidationErrorsByDraftIdAsync(draft.Id, cancellationToken);

        return new(
            draft.Id,
            draft.WorkflowDefinitionId,
            draft.SourceVersionId,
            draft.State.ToStateView(),
            layout,
            errors);
    }

    private static async Task<ActivityNode?> CreateRootActivityAsync(IServiceProvider services, string? rootActivityVersionId, string? rootKind, CancellationToken cancellationToken)
    {
        var normalized = NormalizeRootKind(rootKind);
        var versionStore = services.GetRequiredService<IActivityDefinitionVersionStore>();
        var version = string.IsNullOrWhiteSpace(rootActivityVersionId)
            ? await FindRootKindActivityVersionAsync(services, normalized, cancellationToken)
            : await versionStore.GetWithDefinitionAsync(rootActivityVersionId, cancellationToken);

        if (version is null)
            return null;

        return new ActivityNode(
            NodeId: "root",
            ActivityVersionId: version.Id,
            Inputs: [],
            Outputs: [],
            Structure: CreateRootStructure(version.Definition?.ActivityTypeKey, normalized));
    }

    private static async Task<ActivityDefinitionVersion?> FindRootKindActivityVersionAsync(IServiceProvider services, string rootKind, CancellationToken cancellationToken)
    {
        var versionStore = services.GetRequiredService<IActivityDefinitionVersionStore>();
        var definitionStore = services.GetRequiredService<IActivityDefinitionStore>();
        var activityTypeName = rootKind == WorkflowRootKinds.Flowchart ? nameof(Flowchart) : nameof(Sequence);
        var versions = await versionStore.ListAsync(cancellationToken);

        foreach (var version in versions)
        {
            if (version.Definition is null)
                version.Definition = await definitionStore.GetAsync(version.DefinitionId, cancellationToken);
        }

        return versions
            .Where(x => x.Definition != null && x.Definition.ActivityTypeKey.Contains(activityTypeName, StringComparison.Ordinal))
            .OrderByDescending(x => x.SemVerSortKey, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static Task SaveWorkflowDefinitionAsync(IServiceProvider services, WorkflowDefinition definition, CancellationToken cancellationToken)
    {
        var save = services.GetRequiredService<ISaveWorkflowDefinitionCommand>();
        return save.Execute(definition, cancellationToken);
    }

    private static ActivityNodeStructure? CreateRootStructure(string? activityTypeKey, string fallbackRootKind)
    {
        if (IsFlowchartActivity(activityTypeKey) || (activityTypeKey is null && fallbackRootKind == WorkflowRootKinds.Flowchart))
        {
            var structure = new FlowchartAuthoredStructure();
            return new ActivityNodeStructure(
                Flowchart.StructureKind,
                Flowchart.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(structure, SerializerOptions));
        }

        if (!IsSequenceActivity(activityTypeKey) && !(activityTypeKey is null && fallbackRootKind == WorkflowRootKinds.Sequence))
            return null;

        var sequence = new SequenceAuthoredStructure();
        return new ActivityNodeStructure(
            Sequence.StructureKind,
            Sequence.StructureSchemaVersion,
            JsonSerializer.SerializeToElement(sequence, SerializerOptions));
    }

    private static bool IsFlowchartActivity(string? activityTypeKey) =>
        activityTypeKey?.Contains(nameof(Flowchart), StringComparison.Ordinal) == true;

    private static bool IsSequenceActivity(string? activityTypeKey) =>
        activityTypeKey?.Contains(nameof(Sequence), StringComparison.Ordinal) == true;

    private static string GetMissingRootActivityMessage(CreateWorkflowDefinitionRequest request) =>
        string.IsNullOrWhiteSpace(request.RootActivityVersionId)
            ? $"Could not find a constructable {NormalizeRootKind(request.RootKind)} activity in the activity catalog."
            : $"Could not find root activity version '{request.RootActivityVersionId}' in the activity catalog.";

    private static string NormalizeRootKind(string? rootKind) =>
        string.Equals(rootKind, WorkflowRootKinds.Flowchart, StringComparison.OrdinalIgnoreCase)
            ? WorkflowRootKinds.Flowchart
            : WorkflowRootKinds.Sequence;

    private static string NormalizeDefinitionListState(string? state)
    {
        if (string.Equals(state, WorkflowDefinitionListStates.Deleted, StringComparison.OrdinalIgnoreCase))
            return WorkflowDefinitionListStates.Deleted;

        if (string.Equals(state, WorkflowDefinitionListStates.All, StringComparison.OrdinalIgnoreCase))
            return WorkflowDefinitionListStates.All;

        return WorkflowDefinitionListStates.Active;
    }

    private static async Task<IResult> WithShellAsync(IShellRegistry shellRegistry, Func<IServiceProvider, Task<IResult>> action, CancellationToken cancellationToken)
    {
        var shell = await shellRegistry.GetOrActivateAsync(DefaultShellName, cancellationToken);
        await using var shellScope = shell.BeginScope();
        return await action(shellScope.ServiceProvider);
    }
}

internal static class WorkflowRootKinds
{
    public const string Sequence = "sequence";
    public const string Flowchart = "flowchart";
}

internal static class WorkflowDefinitionListStates
{
    public const string Active = "active";
    public const string Deleted = "deleted";
    public const string All = "all";
}

internal sealed record WorkflowDefinitionsResponse(IReadOnlyList<WorkflowDefinitionSummaryResponse> Definitions);

internal sealed record WorkflowDefinitionSummaryResponse(
    string Id,
    string Name,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastModifiedAt,
    DateTimeOffset? DeletedAt,
    string? DraftId,
    string? LatestVersionId,
    string? LatestVersion,
    int VersionCount);

internal sealed record WorkflowDefinitionDetailsResponse(
    WorkflowDefinitionSummaryResponse Definition,
    WorkflowDraftResponse? Draft,
    IReadOnlyList<WorkflowVersionSummaryResponse> Versions);

internal sealed record WorkflowVersionSummaryResponse(string Id, string Version, DateTimeOffset CreatedAt);

internal sealed record WorkflowDraftResponse(
    string Id,
    string DefinitionId,
    string? SourceVersionId,
    WorkflowDefinitionStateView State,
    IReadOnlyCollection<DesignMetadataRecord> Layout,
    IReadOnlyCollection<Elsa.Workflows.Design.Validations.Core.Models.ValidationError> ValidationErrors);

internal sealed record CreateWorkflowDefinitionRequest(string Name, string? Description, string? RootKind, string? RootActivityVersionId);

internal sealed record UpdateWorkflowDraftRequest(
    WorkflowDefinitionStateView State,
    IReadOnlyCollection<DesignMetadataRecord> Layout);

internal sealed record PromoteDraftResponse(string VersionId);

internal sealed record WorkflowManagementErrorResponse(string Error);

internal sealed record ActivityCatalogResponse(IReadOnlyList<ActivityCatalogItemResponse> Activities);

internal sealed record ActivityCatalogItemResponse(
    string ActivityVersionId,
    string ActivityTypeKey,
    string Version,
    string Category,
    string DisplayName,
    string? Description,
    string ExecutionType,
    IReadOnlyCollection<Elsa.Activities.Design.Core.Models.InputDefinition> Inputs,
    IReadOnlyCollection<Elsa.Activities.Design.Core.Models.OutputDefinition> Outputs,
    IReadOnlyCollection<Elsa.Activities.Design.Core.Models.ActivityDesignFacet> DesignFacets);
