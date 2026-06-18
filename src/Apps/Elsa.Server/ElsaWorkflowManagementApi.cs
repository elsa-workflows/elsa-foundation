using System.Text.Json;
using CShells.Lifecycle;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.EFCore.DbContext;
using Elsa.Activities.Flowchart.Activities;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Sequence.Activities;
using Elsa.Activities.Sequence.Models;
using Elsa.Events.Core.Contracts;
using Elsa.Events.Strategies;
using Elsa.Mediator.Core.Contracts;
using Elsa.Persistence.EFCore.Events;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Runtime.Api.Requests;
using Microsoft.EntityFrameworkCore;

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
            var dbContext = await CreateDbContextAsync<WorkflowsDesignDbContext>(services, cancellationToken);
            await using (dbContext)
            {
                var query = dbContext.WorkflowDefinitions.AsNoTracking();
                query = NormalizeDefinitionListState(state) switch
                {
                    WorkflowDefinitionListStates.Deleted => query.Where(x => x.DeletedAt != null),
                    WorkflowDefinitionListStates.All => query,
                    _ => query.Where(x => x.DeletedAt == null)
                };

                if (!string.IsNullOrWhiteSpace(search))
                    query = query.Where(x => x.Name.Contains(search));

                var definitions = await query
                    .OrderBy(x => x.Name)
                    .ThenBy(x => x.Id)
                    .ToArrayAsync(cancellationToken);

                var definitionIds = definitions.Select(x => x.Id).ToArray();
                var drafts = await dbContext.WorkflowDefinitionDrafts.AsNoTracking()
                    .Where(x => definitionIds.Contains(x.WorkflowDefinitionId))
                    .GroupBy(x => x.WorkflowDefinitionId)
                    .Select(x => x.OrderByDescending(draft => draft.LastModifiedAt).First())
                    .ToArrayAsync(cancellationToken);
                var versions = await dbContext.WorkflowDefinitionVersions.AsNoTracking()
                    .Where(x => definitionIds.Contains(x.DefinitionId))
                    .GroupBy(x => x.DefinitionId)
                    .Select(x => new
                    {
                        DefinitionId = x.Key,
                        Count = x.Count(),
                        Latest = x.OrderByDescending(version => version.SemVerSortKey).FirstOrDefault()
                    })
                    .ToArrayAsync(cancellationToken);

                var draftsByDefinition = drafts.ToDictionary(x => x.WorkflowDefinitionId, StringComparer.Ordinal);
                var versionsByDefinition = versions.ToDictionary(x => x.DefinitionId, StringComparer.Ordinal);

                var response = definitions.Select(definition =>
                {
                    draftsByDefinition.TryGetValue(definition.Id, out var draft);
                    versionsByDefinition.TryGetValue(definition.Id, out var version);
                    return new WorkflowDefinitionSummaryResponse(
                        definition.Id,
                        definition.Name,
                        definition.Description,
                        definition.CreatedAt,
                        definition.LastModifiedAt,
                        definition.DeletedAt,
                        draft?.Id,
                        version?.Latest?.Id,
                        version?.Latest?.Version,
                        version?.Count ?? 0);
                }).ToArray();

                return Results.Ok(new WorkflowDefinitionsResponse(response));
            }
        }, cancellationToken);

    private static Task<IResult> GetDefinitionAsync(IShellRegistry shellRegistry, string definitionId, CancellationToken cancellationToken) =>
        WithShellAsync(shellRegistry, services => LoadDefinitionResultAsync(services, definitionId, cancellationToken), cancellationToken);

    private static Task<IResult> CreateDefinitionAsync(IShellRegistry shellRegistry, CreateWorkflowDefinitionRequest request, CancellationToken cancellationToken) =>
        WithShellAsync(shellRegistry, async services =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new WorkflowManagementErrorResponse("A workflow name is required."));

            var activity = await CreateRootActivityAsync(services, request.RootKind, cancellationToken);
            if (activity is null)
                return Results.BadRequest(new WorkflowManagementErrorResponse($"Could not find a constructable {NormalizeRootKind(request.RootKind)} activity in the activity catalog."));

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
            var dbContext = await CreateDbContextAsync<WorkflowsDesignDbContext>(services, cancellationToken);
            await using (dbContext)
            {
                var definition = await dbContext.WorkflowDefinitions.FirstOrDefaultAsync(x => x.Id == definitionId, cancellationToken);
                if (definition is null)
                    return Results.NotFound(new WorkflowManagementErrorResponse($"Workflow definition '{definitionId}' was not found."));

                if (definition.DeletedAt is null)
                    definition.DeletedAt = DateTimeOffset.UtcNow;

                definition.DeletedReason = null;
                await dbContext.SaveChangesAsync(cancellationToken);
                return Results.NoContent();
            }
        }, cancellationToken);

    private static Task<IResult> RestoreDefinitionAsync(IShellRegistry shellRegistry, string definitionId, CancellationToken cancellationToken) =>
        WithShellAsync(shellRegistry, async services =>
        {
            var dbContext = await CreateDbContextAsync<WorkflowsDesignDbContext>(services, cancellationToken);
            await using (dbContext)
            {
                var definition = await dbContext.WorkflowDefinitions.FirstOrDefaultAsync(x => x.Id == definitionId, cancellationToken);
                if (definition is null)
                    return Results.NotFound(new WorkflowManagementErrorResponse($"Workflow definition '{definitionId}' was not found."));

                definition.DeletedAt = null;
                definition.DeletedReason = null;
                await dbContext.SaveChangesAsync(cancellationToken);
                return Results.NoContent();
            }
        }, cancellationToken);

    private static Task<IResult> DeleteDefinitionPermanentlyAsync(IShellRegistry shellRegistry, string definitionId, CancellationToken cancellationToken) =>
        WithShellAsync(shellRegistry, async services =>
        {
            var dbContext = await CreateDbContextAsync<WorkflowsDesignDbContext>(services, cancellationToken);
            await using (dbContext)
            await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
            {
                var definition = await dbContext.WorkflowDefinitions.FirstOrDefaultAsync(x => x.Id == definitionId, cancellationToken);
                if (definition is null)
                    return Results.NotFound(new WorkflowManagementErrorResponse($"Workflow definition '{definitionId}' was not found."));

                if (definition.DeletedAt is null)
                    return Results.BadRequest(new WorkflowManagementErrorResponse("Only deleted workflow definitions can be permanently deleted."));

                var draftIds = await dbContext.WorkflowDefinitionDrafts
                    .Where(x => x.WorkflowDefinitionId == definitionId)
                    .Select(x => x.Id)
                    .ToArrayAsync(cancellationToken);
                var versionIds = await dbContext.WorkflowDefinitionVersions
                    .Where(x => x.DefinitionId == definitionId)
                    .Select(x => x.Id)
                    .ToArrayAsync(cancellationToken);

                await dbContext.WorkflowDefinitionDraftValidations
                    .Where(x => draftIds.Contains(x.WorkflowDefinitionDraftId))
                    .ExecuteDeleteAsync(cancellationToken);
                await dbContext.WorkflowDefinitionDraftLayouts
                    .Where(x => draftIds.Contains(x.WorkflowDefinitionDraftId))
                    .ExecuteDeleteAsync(cancellationToken);
                await dbContext.WorkflowDefinitionVersionLayouts
                    .Where(x => versionIds.Contains(x.WorkflowDefinitionVersionId))
                    .ExecuteDeleteAsync(cancellationToken);
                await dbContext.WorkflowDefinitionDrafts
                    .Where(x => x.WorkflowDefinitionId == definitionId)
                    .ExecuteDeleteAsync(cancellationToken);
                await dbContext.WorkflowDefinitionVersions
                    .Where(x => x.DefinitionId == definitionId)
                    .ExecuteDeleteAsync(cancellationToken);
                await dbContext.WorkflowDefinitions
                    .Where(x => x.Id == definitionId)
                    .ExecuteDeleteAsync(cancellationToken);

                await transaction.CommitAsync(cancellationToken);
                return Results.NoContent();
            }
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
            var dbContext = await CreateDbContextAsync<ActivitiesDesignDbContext>(services, cancellationToken);
            var eventPublisher = services.GetRequiredService<IEventPublisher>();
            await using (dbContext)
            {
                var versions = await dbContext.ActivityDefinitionVersions
                    .Include(x => x.Definition)
                    .OrderBy(x => x.Definition!.Category)
                    .ThenBy(x => x.Definition!.DisplayName)
                    .ThenBy(x => x.Id)
                    .ToArrayAsync(cancellationToken);

                foreach (var version in versions)
                {
                    await eventPublisher.Publish(
                        new OnEntityLoading(dbContext, version),
                        EventPublishingStrategy.Sequential,
                        cancellationToken);
                }

                var response = versions
                    .Where(x => x.Definition is not null)
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
            }
        }, cancellationToken);

    private static async Task<IResult> LoadDefinitionResultAsync(IServiceProvider services, string definitionId, CancellationToken cancellationToken)
    {
        var dbContext = await CreateDbContextAsync<WorkflowsDesignDbContext>(services, cancellationToken);
        await using (dbContext)
        {
            var definition = await dbContext.WorkflowDefinitions.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == definitionId && x.DeletedAt == null, cancellationToken);

            if (definition is null)
                return Results.NotFound(new WorkflowManagementErrorResponse($"Workflow definition '{definitionId}' was not found."));

            var draft = await dbContext.WorkflowDefinitionDrafts
                .Where(x => x.WorkflowDefinitionId == definitionId)
                .OrderByDescending(x => x.LastModifiedAt)
                .FirstOrDefaultAsync(cancellationToken);
            var versions = await dbContext.WorkflowDefinitionVersions.AsNoTracking()
                .Where(x => x.DefinitionId == definitionId)
                .OrderByDescending(x => x.SemVerSortKey)
                .Select(x => new WorkflowVersionSummaryResponse(x.Id, x.Version, x.CreatedAt))
                .ToArrayAsync(cancellationToken);

            WorkflowDraftResponse? draftResponse = null;
            if (draft is not null)
                draftResponse = await LoadDraftAsync(services, dbContext, draft, cancellationToken);

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
    }

    private static async Task<IResult> LoadDraftResultAsync(IServiceProvider services, string draftId, CancellationToken cancellationToken)
    {
        var dbContext = await CreateDbContextAsync<WorkflowsDesignDbContext>(services, cancellationToken);
        await using (dbContext)
        {
            var draft = await dbContext.WorkflowDefinitionDrafts.FirstOrDefaultAsync(x => x.Id == draftId, cancellationToken);
            if (draft is null)
                return Results.NotFound(new WorkflowManagementErrorResponse($"Workflow definition draft '{draftId}' was not found."));

            return Results.Ok(await LoadDraftAsync(services, dbContext, draft, cancellationToken));
        }
    }

    private static async Task<WorkflowDraftResponse> LoadDraftAsync(IServiceProvider services, WorkflowsDesignDbContext dbContext, WorkflowDefinitionDraft draft, CancellationToken cancellationToken)
    {
        var eventPublisher = services.GetRequiredService<IEventPublisher>();
        await eventPublisher.Publish(
            new OnEntityLoading(dbContext, draft),
            EventPublishingStrategy.Sequential,
            cancellationToken);

        var layout = await dbContext.WorkflowDefinitionDraftLayouts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.WorkflowDefinitionDraftId == draft.Id, cancellationToken);
        var validation = await dbContext.WorkflowDefinitionDraftValidations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.WorkflowDefinitionDraftId == draft.Id, cancellationToken);

        return new WorkflowDraftResponse(
            draft.Id,
            draft.WorkflowDefinitionId,
            draft.SourceVersionId,
            draft.State.ToStateView(),
            layout?.Records.ToArray() ?? [],
            validation?.Errors.ToArray() ?? []);
    }

    private static async Task<ActivityNode?> CreateRootActivityAsync(IServiceProvider services, string? rootKind, CancellationToken cancellationToken)
    {
        var normalized = NormalizeRootKind(rootKind);
        var dbContext = await CreateDbContextAsync<ActivitiesDesignDbContext>(services, cancellationToken);
        await using (dbContext)
        {
            var activityTypeName = normalized == WorkflowRootKinds.Flowchart ? nameof(Flowchart) : nameof(Sequence);
            var version = await dbContext.ActivityDefinitionVersions
                .Include(x => x.Definition)
                .Where(x => x.Definition != null && x.Definition.ActivityTypeKey.Contains(activityTypeName))
                .OrderByDescending(x => x.SemVerSortKey)
                .FirstOrDefaultAsync(cancellationToken);

            if (version is null)
                return null;

            return new ActivityNode(
                NodeId: "root",
                ActivityVersionId: version.Id,
                Inputs: [],
                Outputs: [],
                Structure: CreateRootStructure(normalized));
        }
    }

    private static ActivityNodeStructure CreateRootStructure(string rootKind)
    {
        if (rootKind == WorkflowRootKinds.Flowchart)
        {
            var structure = new FlowchartAuthoredStructure();
            return new ActivityNodeStructure(
                Flowchart.StructureKind,
                Flowchart.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(structure, SerializerOptions));
        }

        var sequence = new SequenceAuthoredStructure();
        return new ActivityNodeStructure(
            Sequence.StructureKind,
            Sequence.StructureSchemaVersion,
            JsonSerializer.SerializeToElement(sequence, SerializerOptions));
    }

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

    private static async Task<TDbContext> CreateDbContextAsync<TDbContext>(IServiceProvider services, CancellationToken cancellationToken)
        where TDbContext : DbContext
    {
        var factory = services.GetRequiredService<IDbContextFactory<TDbContext>>();
        return await factory.CreateDbContextAsync(cancellationToken);
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

internal sealed record CreateWorkflowDefinitionRequest(string Name, string? Description, string? RootKind);

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
