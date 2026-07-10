using System.Text.Json;
using CShells.Lifecycle;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Flowchart.Activities;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Sequence.Activities;
using Elsa.Activities.Sequence.Models;
using Elsa.Events.Core.Contracts;
using Elsa.Expressions.Core.Contracts;
using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Validations.Core;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ActivityAvailabilityContracts = Elsa.Activities.Design.Core.Contracts;
using ActivityAvailabilityModels = Elsa.Activities.Design.Core.Models;
using ActivityAvailabilityOptionsNs = Elsa.Activities.Design.Core.Options;
using ActivityAvailabilityStores = Elsa.Activities.Design.Core.Stores;
using ActivityDesignFilters = Elsa.Activities.Design.Persistence.Core.Filters;

namespace Elsa.Server;

internal static class ElsaWorkflowManagementApi
{
    private const string DefaultShellName = "default";

    // Draft test runs pin a synthetic definition-version id of the form "draft:{draftId}-{stateHash}"
    // (see StartWorkflowDraftTestRunRequestHandler). These ids never reach the version store, so the
    // designer graph for such a run is resolved from the originating draft instead.
    private const string DraftVersionIdPrefix = "draft:";
    private const string DraftVersionLabel = "draft";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapElsaWorkflowManagementApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/_elsa/workflow-management");

        group.MapGet("/definitions", ListDefinitionsAsync);
        group.MapGet("/definitions/{definitionId}", GetDefinitionAsync);
        group.MapPost("/definitions", CreateDefinitionAsync);
        group.MapPatch("/definitions/{definitionId}", UpdateDefinitionMetadataAsync);
        group.MapDelete("/definitions/{definitionId}", DeleteDefinitionAsync);
        group.MapPost("/definitions/{definitionId}/restore", RestoreDefinitionAsync);
        group.MapDelete("/definitions/{definitionId}/permanent", DeleteDefinitionPermanentlyAsync);
        group.MapPut("/drafts/{draftId}", UpdateDraftAsync);
        group.MapPost("/drafts/{draftId}/promote", PromoteDraftAsync);
        group.MapDelete("/drafts/{draftId}", DiscardDraftAsync);
        group.MapGet("/versions/{versionId}", GetVersionAsync);
        group.MapPost("/versions/{versionId}/publish", PublishVersionAsync);
        group.MapGet("/executables", ListExecutablesAsync);
        group.MapGet("/executables/{artifactId}", GetExecutableAsync);
        group.MapDelete("/executables/{artifactId}", DeleteExecutableAsync);
        group.MapPost("/executables/{artifactId}/restore", RestoreExecutableAsync);
        group.MapDelete("/executables/{artifactId}/permanent", DeleteExecutablePermanentlyAsync);
        group.MapPost("/executables/{artifactId}/run", RunExecutableAsync);
        group.MapGet("/activities", ListActivitiesAsync);
        group.MapGet("/activities/availability/settings", GetActivityAvailabilitySettingsAsync);
        group.MapPut("/activities/availability/settings", SaveActivityAvailabilitySettingsAsync);
        group.MapGet("/activities/availability/diagnostics", ListActivityAvailabilityDiagnosticsAsync);
        group.MapGet("/descriptors/activities", ListActivityDescriptorsAsync);
        group.MapGet("/descriptors/expression-descriptors", ListExpressionDescriptorsAsync);
        group.MapGet("/descriptors/variables", ListVariableDescriptorsAsync);

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

    private static Task<IResult> GetVersionAsync(IShellRegistry shellRegistry, string versionId, CancellationToken cancellationToken) =>
        WithShellAsync(shellRegistry, services => LoadVersionResultAsync(services, versionId, cancellationToken), cancellationToken);

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

    private static Task<IResult> UpdateDefinitionMetadataAsync(IShellRegistry shellRegistry, string definitionId, UpdateWorkflowDefinitionMetadataRequest request, CancellationToken cancellationToken) =>
        WithShellAsync(shellRegistry, async services =>
        {
            var definitionStore = services.GetRequiredService<IWorkflowDefinitionStore>();
            var definition = await definitionStore.FindByIdAsync(definitionId, cancellationToken);
            if (definition is null || definition.DeletedAt is not null)
                return Results.NotFound(new WorkflowManagementErrorResponse($"Workflow definition '{definitionId}' was not found."));

            // Partial update — only provided fields change.
            if (request.Name is not null)
            {
                if (string.IsNullOrWhiteSpace(request.Name))
                    return Results.BadRequest(new WorkflowManagementErrorResponse("A workflow name cannot be empty."));
                definition.Name = request.Name.Trim();
            }

            if (request.Description is not null)
                definition.Description = request.Description;

            await SaveWorkflowDefinitionAsync(services, definition, cancellationToken);
            return await LoadDefinitionResultAsync(services, definitionId, cancellationToken);
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

    // Executables list (#598 P1): artifact rows with nested references. `scope` = published | test-runs | all
    // (default published); `includeRetired=true` surfaces retired references. The legacy `state` param is still
    // honored for backward compatibility (deleted ⇒ retired-only, all ⇒ all scopes incl. retired) so the current
    // Studio table keeps working until it moves to the scope filter.
    private static Task<IResult> ListExecutablesAsync(IShellRegistry shellRegistry, string? scope, bool? includeRetired, string? state, CancellationToken cancellationToken) =>
        WithShellAsync(shellRegistry, async services =>
        {
            var inspector = services.GetRequiredService<WorkflowExecutableInspector>();
            var (listScope, retired) = ResolveExecutableListFilter(scope, includeRetired, state);
            var view = await inspector.ListAsync(listScope, retired, cancellationToken);
            return Results.Ok(view);
        }, cancellationToken);

    // Executable detail (#598 P1): identity block + Execution Material node tree + chosen reference's layout +
    // full reference list. Self-contained — no workflow-definition table is consulted. 404 for unknown artifact.
    private static Task<IResult> GetExecutableAsync(IShellRegistry shellRegistry, string artifactId, string? @ref, CancellationToken cancellationToken) =>
        WithShellAsync(shellRegistry, async services =>
        {
            var inspector = services.GetRequiredService<WorkflowExecutableInspector>();
            var view = await inspector.GetAsync(artifactId, @ref, cancellationToken);
            return view is null
                ? Results.NotFound(new WorkflowManagementErrorResponse($"Executable artifact '{artifactId}' was not found."))
                : Results.Ok(view);
        }, cancellationToken);

    private static (WorkflowExecutableListScope Scope, bool IncludeRetired) ResolveExecutableListFilter(string? scope, bool? includeRetired, string? legacyState)
    {
        // New scope param wins when supplied; otherwise map the legacy state param onto the scope/retired axes.
        if (!string.IsNullOrWhiteSpace(scope))
            return (NormalizeExecutableScope(scope), includeRetired ?? false);

        if (string.Equals(legacyState, WorkflowDefinitionListStates.Deleted, StringComparison.OrdinalIgnoreCase))
            return (WorkflowExecutableListScope.All, true);
        if (string.Equals(legacyState, WorkflowDefinitionListStates.All, StringComparison.OrdinalIgnoreCase))
            return (WorkflowExecutableListScope.All, includeRetired ?? true);

        return (WorkflowExecutableListScope.Published, includeRetired ?? false);
    }

    private static WorkflowExecutableListScope NormalizeExecutableScope(string? scope)
    {
        if (string.Equals(scope, "test-runs", StringComparison.OrdinalIgnoreCase) || string.Equals(scope, "testruns", StringComparison.OrdinalIgnoreCase))
            return WorkflowExecutableListScope.TestRuns;
        if (string.Equals(scope, "all", StringComparison.OrdinalIgnoreCase))
            return WorkflowExecutableListScope.All;
        return WorkflowExecutableListScope.Published;
    }

    private static Task<IResult> DeleteExecutableAsync(IShellRegistry shellRegistry, string artifactId, string? definitionId, CancellationToken cancellationToken) =>
        WithShellAsync(shellRegistry, async services =>
        {
            // Deleting an executable = retiring its references (ADR 0040); the artifact follows by GC (worker W3).
            // Content addressing lets behaviorally identical definitions share one artifact, so an optional
            // ?definitionId= scopes the retire to that definition's references; without it the operation targets the
            // artifact as a whole (every definition's references).
            var referenceStore = services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>();
            var references = ScopeToDefinition(await referenceStore.ListByArtifactAsync(artifactId, cancellationToken), definitionId);
            if (references.Count == 0)
                return Results.NotFound(new WorkflowManagementErrorResponse($"Executable artifact '{artifactId}' was not found{DefinitionScopeSuffix(definitionId)}."));

            var now = DateTimeOffset.UtcNow;
            foreach (var reference in references.Where(reference => reference.DeletedAt is null))
                await referenceStore.RetireAsync(reference.SourceReferenceId, now, cancellationToken: cancellationToken);

            return Results.NoContent();
        }, cancellationToken);

    private static Task<IResult> RestoreExecutableAsync(IShellRegistry shellRegistry, string artifactId, string? definitionId, CancellationToken cancellationToken) =>
        WithShellAsync(shellRegistry, async services =>
        {
            // Restore un-retires references (the inverse of DeleteExecutableAsync), honoring the same optional
            // ?definitionId= scope so restoring one definition's executable never resurrects another definition's
            // retired references on a shared artifact.
            var referenceStore = services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>();
            var references = ScopeToDefinition(await referenceStore.ListByArtifactAsync(artifactId, cancellationToken), definitionId);
            if (references.Count == 0)
                return Results.NotFound(new WorkflowManagementErrorResponse($"Executable artifact '{artifactId}' was not found{DefinitionScopeSuffix(definitionId)}."));

            foreach (var reference in references.Where(reference => reference.DeletedAt is not null))
                await referenceStore.SaveAsync(reference with { DeletedAt = null, DeletedReason = null }, cancellationToken);

            return Results.NoContent();
        }, cancellationToken);

    private static Task<IResult> DeleteExecutablePermanentlyAsync(IShellRegistry shellRegistry, string artifactId, CancellationToken cancellationToken) =>
        WithShellAsync(shellRegistry, async services =>
        {
            // Permanent delete is artifact-level by nature (the artifact is shared by every definition referencing
            // it). Retire the references first so none is left dangling at a missing artifact — the GC sweep purges
            // the retired records.
            var referenceStore = services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>();
            var now = DateTimeOffset.UtcNow;
            foreach (var reference in await referenceStore.ListByArtifactAsync(artifactId, cancellationToken))
            {
                if (reference.DeletedAt is null)
                    await referenceStore.RetireAsync(reference.SourceReferenceId, now, cancellationToken: cancellationToken);
            }

            var store = services.GetRequiredService<IWorkflowExecutableStore>();
            return await store.DeleteAsync(artifactId, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound(new WorkflowManagementErrorResponse($"Executable artifact '{artifactId}' was not found."));
        }, cancellationToken);

    private static Task<IResult> RunExecutableAsync(IShellRegistry shellRegistry, string artifactId, CancellationToken cancellationToken) =>
        WithShellAsync(shellRegistry, async services =>
        {
            var sender = services.GetRequiredService<IRequestSender>();
            try
            {
                var dispatch = await sender.Send(new ExecuteWorkflow(artifactId), cancellationToken);
                return Results.Ok(dispatch);
            }
            catch (WorkflowExecutableNotFoundException)
            {
                return Results.NotFound(new WorkflowManagementErrorResponse($"Executable artifact '{artifactId}' was not found."));
            }
            catch (WorkflowExecutableReferenceRejectedException e)
            {
                // Reference gate refusal (ADR 0040): the artifact exists but has no live Published reference
                // (retired, or an expired test run). Surface the structured reason instead of a 500.
                return Results.Conflict(new WorkflowManagementErrorResponse(e.Message));
            }
        }, cancellationToken);

    private static IReadOnlyList<WorkflowExecutableSourceReference> ScopeToDefinition(
        IReadOnlyCollection<WorkflowExecutableSourceReference> references,
        string? definitionId) =>
        string.IsNullOrWhiteSpace(definitionId)
            ? references.ToArray()
            : references.Where(reference => string.Equals(reference.DefinitionId, definitionId, StringComparison.Ordinal)).ToArray();

    private static string DefinitionScopeSuffix(string? definitionId) =>
        string.IsNullOrWhiteSpace(definitionId) ? "" : $" for definition '{definitionId}'";

    private static Task<IResult> ListActivitiesAsync(IShellRegistry shellRegistry, CancellationToken cancellationToken) =>
        WithShellAsync(shellRegistry, async services =>
        {
            var versionStore = services.GetRequiredService<IActivityDefinitionVersionStore>();
            var definitionStore = services.GetRequiredService<IActivityDefinitionStore>();
            var settingsStore = services.GetRequiredService<ActivityAvailabilityStores.IActivityAvailabilitySettingsStore>();
            var availabilityEvaluator = services.GetRequiredService<ActivityAvailabilityContracts.IActivityAvailabilityEvaluator>();
            var versions = await versionStore.ListAsync(cancellationToken);

            foreach (var version in versions)
            {
                if (version.Definition is null)
                    version.Definition = await definitionStore.GetAsync(version.DefinitionId, cancellationToken);
            }

            // Apply the design-time availability policy stack so the picker only offers addable activities.
            var settings = await settingsStore.LoadAsync(ActivityAvailabilityModels.ActivityAvailabilitySettings.HostDefaultScope, cancellationToken);
            var addableKeys = availabilityEvaluator
                .FilterAddable(versions.Where(x => x.Definition is not null).Select(x => x.Definition!), settings)
                .Select(definition => definition.ActivityTypeKey)
                .ToHashSet(StringComparer.Ordinal);

            var response = versions
                .Where(x => x.Definition is not null && addableKeys.Contains(x.Definition!.ActivityTypeKey))
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

    private static Task<IResult> GetActivityAvailabilitySettingsAsync(IShellRegistry shellRegistry, CancellationToken cancellationToken) =>
        WithShellAsync(shellRegistry, async services =>
        {
            var settingsStore = services.GetRequiredService<ActivityAvailabilityStores.IActivityAvailabilitySettingsStore>();
            var settings = await settingsStore.LoadAsync(ActivityAvailabilityModels.ActivityAvailabilitySettings.HostDefaultScope, cancellationToken)
                ?? new ActivityAvailabilityModels.ActivityAvailabilitySettings();
            return Results.Ok(settings);
        }, cancellationToken);

    private static Task<IResult> SaveActivityAvailabilitySettingsAsync(IShellRegistry shellRegistry, SaveActivityAvailabilitySettingsRequest request, CancellationToken cancellationToken) =>
        WithShellAsync(shellRegistry, async services =>
        {
            var settingsStore = services.GetRequiredService<ActivityAvailabilityStores.IActivityAvailabilitySettingsStore>();
            var settings = new ActivityAvailabilityModels.ActivityAvailabilitySettings
            {
                Scope = string.IsNullOrWhiteSpace(request.Scope)
                    ? ActivityAvailabilityModels.ActivityAvailabilitySettings.HostDefaultScope
                    : request.Scope,
                Mode = request.Mode,
                Rules = new ActivityAvailabilityOptionsNs.ActivityAvailabilityRuleSet
                {
                    ActivityTypes = request.Rules?.ActivityTypes ?? [],
                    Sets = request.Rules?.Sets ?? []
                }
            };

            await settingsStore.SaveAsync(settings, cancellationToken);
            return Results.Ok(settings);
        }, cancellationToken);

    private static Task<IResult> ListActivityAvailabilityDiagnosticsAsync(IShellRegistry shellRegistry, CancellationToken cancellationToken) =>
        WithShellAsync(shellRegistry, async services =>
        {
            var definitionStore = services.GetRequiredService<IActivityDefinitionStore>();
            var settingsStore = services.GetRequiredService<ActivityAvailabilityStores.IActivityAvailabilitySettingsStore>();
            var diagnosticsProjector = services.GetRequiredService<ActivityAvailabilityContracts.IActivityAvailabilityDiagnosticsProjector>();
            var options = services.GetRequiredService<IOptions<ActivityAvailabilityOptionsNs.ActivityAvailabilityOptions>>();

            var definitions = await definitionStore.ListAsync(new ActivityDesignFilters.ActivityDefinitionFilter(), cancellationToken);
            var settings = await settingsStore.LoadAsync(ActivityAvailabilityModels.ActivityAvailabilitySettings.HostDefaultScope, cancellationToken);
            var diagnostics = diagnosticsProjector.Project(definitions, options.Value, settings);
            return Results.Ok(diagnostics);
        }, cancellationToken);

    private static Task<IResult> ListActivityDescriptorsAsync(IShellRegistry shellRegistry, CancellationToken cancellationToken) =>
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

            var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(ElsaWorkflowManagementApi));
            var wellKnownTypeRegistry = services.GetRequiredService<IWellKnownTypeRegistry>();
            var response = versions
                .Where(x => x.Definition is not null)
                .OrderBy(x => x.Definition!.Category, StringComparer.Ordinal)
                .ThenBy(x => x.Definition!.DisplayName, StringComparer.Ordinal)
                .ThenBy(x => x.Id, StringComparer.Ordinal)
                .Select(v => ToActivityDescriptorResponse(v, logger, wellKnownTypeRegistry))
                .ToArray();

            return Results.Ok(new ActivityDescriptorsResponse(response));
        }, cancellationToken);

    private static Task<IResult> ListExpressionDescriptorsAsync(IShellRegistry shellRegistry, CancellationToken cancellationToken) =>
        WithShellAsync(shellRegistry, services =>
        {
            var registry = services.GetService<IExpressionDescriptorRegistry>();
            var descriptors = registry?.ListAll().Select(ToExpressionDescriptorResponse) ?? [];
            var response = descriptors
                .Concat(DefaultExpressionDescriptors())
                .GroupBy(x => x.Type, StringComparer.Ordinal)
                .Select(x => x.First())
                .OrderBy(x => x.DisplayName)
                .ToArray();

            return Task.FromResult<IResult>(Results.Ok(new ExpressionDescriptorsResponse(response)));
        }, cancellationToken);

    private static Task<IResult> ListVariableDescriptorsAsync(IShellRegistry shellRegistry, CancellationToken cancellationToken) =>
        WithShellAsync(shellRegistry, services =>
        {
            var catalog = services.GetRequiredService<IVariableTypeDescriptorCatalog>();
            var descriptors = catalog.GetDescriptors()
                .Select(x => new VariableDescriptorResponse(x.Alias, x.DisplayName, x.Category, x.DefaultEditor))
                .ToArray();

            return Task.FromResult<IResult>(Results.Ok(new VariableDescriptorsResponse(descriptors)));
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
        {
            // This path resolves the draft by workflow-definition id (not by draft id), so it fetches
            // the layout separately rather than adding a second combined by-definition port method. The
            // GET-draft path below uses the single combined read instead.
            var layout = await draftStore.FindLayoutByDraftIdAsync(draft.Id, cancellationToken);
            draftResponse = await ToDraftResponseAsync(services, draft, layout, cancellationToken);
        }

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

        // Single combined read: draft + layout come from one port call (one document load on Groundwork)
        // instead of FindByIdAsync followed by FindLayoutByDraftIdAsync re-loading the same document.
        var draftWithLayout = await draftStore.FindWithLayoutByIdAsync(draftId, cancellationToken);
        if (draftWithLayout is not null)
            return Results.Ok(await ToDraftResponseAsync(services, draftWithLayout.Draft, draftWithLayout.Layout, cancellationToken));

        return Results.NotFound(new WorkflowManagementErrorResponse($"Workflow definition draft '{draftId}' was not found."));
    }

    private static async Task<IResult> LoadVersionResultAsync(IServiceProvider services, string versionId, CancellationToken cancellationToken)
    {
        if (TryGetDraftIdFromSyntheticVersionId(versionId, out var draftId))
            return await LoadDraftVersionResultAsync(services, versionId, draftId, cancellationToken);

        var versionStore = services.GetRequiredService<IWorkflowDefinitionVersionStore>();
        var layoutStore = services.GetRequiredService<IWorkflowDefinitionVersionLayoutStore>();

        WorkflowDefinitionVersion version;
        try
        {
            version = await versionStore.GetWithDefinitionAsync(versionId, cancellationToken);
        }
        catch (ArgumentException)
        {
            return Results.NotFound(new WorkflowManagementErrorResponse($"Workflow definition version '{versionId}' was not found."));
        }

        if (version.Definition is null)
            return Results.NotFound(new WorkflowManagementErrorResponse($"Workflow definition version '{versionId}' was not found."));

        var layout = await layoutStore.FindByVersionIdAsync(versionId, cancellationToken);
        return Results.Ok(version.ToDetailsView(layout?.Records));
    }

    /// <summary>
    /// Resolves the designer graph for a draft test run. The pinned version id is synthetic
    /// (<c>draft:{snapshotId}</c>) and has no row in the version store. For unsaved test runs, the
    /// authored state is read from the transient test-run snapshot; when that has expired or is
    /// unavailable, the originating persisted draft is used as a compatibility fallback.
    /// </summary>
    private static async Task<IResult> LoadDraftVersionResultAsync(IServiceProvider services, string versionId, string draftId, CancellationToken cancellationToken)
    {
        var snapshotDetails = await TryLoadDraftSnapshotVersionDetailsAsync(services, versionId, draftId, cancellationToken);
        if (snapshotDetails is not null)
            return Results.Ok(snapshotDetails);

        var draftStore = services.GetRequiredService<IWorkflowDefinitionDraftStore>();
        var definitionStore = services.GetRequiredService<IWorkflowDefinitionStore>();

        var draft = await draftStore.FindByIdAsync(draftId, cancellationToken);
        if (draft is null)
            return Results.NotFound(new WorkflowManagementErrorResponse($"Workflow definition version '{versionId}' was not found."));

        var definition = await definitionStore.FindByIdAsync(draft.WorkflowDefinitionId, cancellationToken);
        if (definition is null)
            return Results.NotFound(new WorkflowManagementErrorResponse($"Workflow definition version '{versionId}' was not found."));

        var layout = await draftStore.FindLayoutByDraftIdAsync(draftId, cancellationToken);
        var details = new WorkflowDefinitionVersionDetailsView(
            versionId,
            DraftVersionLabel,
            definition.ToView(),
            draft.State.ToStateView(),
            layout.Select(WorkflowDefinitionLayoutRecordView.From).ToArray());

        return Results.Ok(details);
    }

    private static async Task<WorkflowDefinitionVersionDetailsView?> TryLoadDraftSnapshotVersionDetailsAsync(
        IServiceProvider services,
        string versionId,
        string draftId,
        CancellationToken cancellationToken)
    {
        var testRunStore = services.GetService<IWorkflowTestRunStore>();
        if (testRunStore is null)
            return null;

        var snapshot = await testRunStore.FindDraftSnapshotAsync(versionId, cancellationToken);
        if (snapshot is null)
            return null;

        var definitionStore = services.GetRequiredService<IWorkflowDefinitionStore>();
        var definition = await definitionStore.FindByIdAsync(snapshot.DefinitionId, cancellationToken)
            ?? new WorkflowDefinition { Id = snapshot.DefinitionId, Name = snapshot.DefinitionId };
        var layout = await TryLoadDraftSnapshotLayoutAsync(services, draftId, cancellationToken);

        return new WorkflowDefinitionVersionDetailsView(
            snapshot.DefinitionVersionId,
            snapshot.ArtifactVersion,
            definition.ToView(),
            snapshot.State.ToStateView(),
            layout.Select(WorkflowDefinitionLayoutRecordView.From).ToArray());
    }

    private static async Task<IReadOnlyCollection<DesignMetadataRecord>> TryLoadDraftSnapshotLayoutAsync(
        IServiceProvider services,
        string draftId,
        CancellationToken cancellationToken)
    {
        var draftStore = services.GetService<IWorkflowDefinitionDraftStore>();
        if (draftStore is null || string.IsNullOrWhiteSpace(draftId))
            return [];

        var draft = await draftStore.FindByIdAsync(draftId, cancellationToken);
        return draft is null
            ? []
            : await draftStore.FindLayoutByDraftIdAsync(draftId, cancellationToken);
    }

    /// <summary>
    /// Extracts the originating draft id from a synthetic draft-test-run version id of the form
    /// <c>draft:{snapshotId}</c>. When the snapshot id embeds a draft id plus a trailing state hash,
    /// the draft id is recovered by stripping the final hyphen segment.
    /// </summary>
    private static bool TryGetDraftIdFromSyntheticVersionId(string? versionId, out string draftId)
    {
        draftId = string.Empty;
        if (string.IsNullOrEmpty(versionId) || !versionId.StartsWith(DraftVersionIdPrefix, StringComparison.Ordinal))
            return false;

        var snapshotId = versionId[DraftVersionIdPrefix.Length..];
        var separatorIndex = snapshotId.LastIndexOf('-');
        draftId = separatorIndex > 0 ? snapshotId[..separatorIndex] : snapshotId;
        return !string.IsNullOrWhiteSpace(draftId);
    }

    private static async Task<WorkflowDraftResponse> ToDraftResponseAsync(
        IServiceProvider services,
        WorkflowDefinitionDraft draft,
        IReadOnlyCollection<DesignMetadataRecord> layout,
        CancellationToken cancellationToken)
    {
        var eventPublisher = services.GetRequiredService<IInlineEventPublisher>();

        // Derive validation errors from the already-loaded draft via the shielded read gate: a
        // throwing validator yields a synthetic Validation/Faulted error instead of a 500. The draft
        // (and its layout) were loaded once by the caller and are not re-loaded here.
        var errors = await eventPublisher.TryDeriveValidationErrorsAsync(draft, cancellationToken);

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

    private static ActivityDescriptorResponse ToActivityDescriptorResponse(ActivityDefinitionVersion version, ILogger logger, IWellKnownTypeRegistry wellKnownTypeRegistry)
    {
        var definition = version.Definition!;
        var activityName = definition.ActivityTypeKey.Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? definition.ActivityTypeKey;
        return new ActivityDescriptorResponse(
            definition.ActivityTypeKey,
            definition.ActivityTypeKey,
            activityName,
            ParseMajorVersion(version.Version),
            definition.Category,
            definition.DisplayName ?? activityName,
            definition.Description,
            version.ExecutionType.ToString(),
            version.Inputs.Select(input => ToInputDescriptorResponse(input, logger, wellKnownTypeRegistry)).ToArray(),
            version.Outputs.Select(ToOutputDescriptorResponse).ToArray(),
            version.DesignFacets.SelectMany(ToPortDescriptorResponses).ToArray(),
            new Dictionary<string, object>(),
            new Dictionary<string, object>(),
            version.DesignFacets.Any(IsContainerDesignFacet),
            true,
            false,
            false);
    }

    private static InputDescriptorResponse ToInputDescriptorResponse(Elsa.Activities.Design.Core.Models.InputDefinition input, ILogger logger, IWellKnownTypeRegistry wellKnownTypeRegistry) =>
        new(
            input.Name,
            GetTypeName(input.Type),
            input.DisplayName,
            input.Description,
            input.Order,
            input.Category,
            input.IsBrowsable,
            input.UiHint ?? InferUiHint(input.Type),
            true,
            input.DefaultValue,
            input.DefaultSyntax ?? "Literal",
            // Author-provided UI metadata is opaque JSON (a verbatim JsonElement, ADR 0035 D3); when absent we
            // synthesize the descriptor's options from the declared type. Both serialize to the same response JSON.
            input.UISpecifications.HasValue
                ? input.UISpecifications.Value
                : GetUiSpecifications(input.Type, logger, wellKnownTypeRegistry),
            input.IsRequired);

    private static OutputDescriptorResponse ToOutputDescriptorResponse(Elsa.Activities.Design.Core.Models.OutputDefinition output) =>
        new(
            output.Name,
            GetTypeName(output.Type),
            output.DisplayName,
            output.Description,
            output.Order,
            output.Category,
            output.IsBrowsable);

    private static IEnumerable<PortDescriptorResponse> ToPortDescriptorResponses(Elsa.Activities.Design.Core.Models.ActivityDesignFacet facet)
    {
        if (facet.Payload.ValueKind != JsonValueKind.Object || !facet.Payload.TryGetProperty("ports", out var ports) || ports.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var port in ports.EnumerateArray())
        {
            if (port.ValueKind != JsonValueKind.Object || !port.TryGetProperty("name", out var nameProperty))
                continue;

            var name = nameProperty.GetString();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var displayName = port.TryGetProperty("displayName", out var displayNameProperty) ? displayNameProperty.GetString() : name;
            var type = port.TryGetProperty("type", out var typeProperty) ? typeProperty.GetString() ?? "Flow" : "Flow";
            var isBrowsable = port.TryGetProperty("isBrowsable", out var browsableProperty) && browsableProperty.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? browsableProperty.GetBoolean()
                : (bool?)null;

            yield return new PortDescriptorResponse(name, displayName, type, isBrowsable);
        }
    }

    private static ExpressionDescriptorResponse ToExpressionDescriptorResponse(IExpressionDescriptor descriptor) =>
        new(descriptor.TypeName, descriptor.DisplayName, null);

    private static IEnumerable<ExpressionDescriptorResponse> DefaultExpressionDescriptors()
    {
        yield return new ExpressionDescriptorResponse("Literal", "Literal", null);
        yield return new ExpressionDescriptorResponse("JavaScript", "JavaScript", null);
        yield return new ExpressionDescriptorResponse("Liquid", "Liquid", null);
        yield return new ExpressionDescriptorResponse("Object", "Object", null);
        yield return new ExpressionDescriptorResponse("Variable", "Variable", null);
        yield return new ExpressionDescriptorResponse("Input", "Input", null);
    }

    // The authored type is now a rename-proof alias (TypeReference); the descriptor response reports the alias.
    private static string GetTypeName(TypeReference type) => type.Alias;

    private static int ParseMajorVersion(string version) =>
        int.TryParse(version.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(), out var major)
            ? major
            : 1;

    private static string InferUiHint(TypeReference type) =>
        string.Equals(type.Alias, "Boolean", StringComparison.Ordinal) ? "checkbox" : "singleline";

    // Restored (FR-004b / research D8 revised): the authored type is an alias, and the registry now resolves
    // every referenceable element type (primitives + activity I/O types) back to its real CLR type — so an
    // enum-typed input can again expose its option list. We resolve only the element type (collection-ness is
    // irrelevant to whether the element is an enum) via IWellKnownTypeRegistry; unknown aliases yield no options.
    private static object? GetUiSpecifications(TypeReference type, ILogger logger, IWellKnownTypeRegistry wellKnownTypeRegistry)
    {
        if (!wellKnownTypeRegistry.TryGetTypeOrDefault(type.Alias, out var elementType) || !elementType.IsEnum)
            return null;

        return new Dictionary<string, object>
        {
            ["options"] = Enum.GetNames(elementType).Select(name => new DescriptorOptionResponse(name, name)).ToArray()
        };
    }

    private static bool IsContainerDesignFacet(Elsa.Activities.Design.Core.Models.ActivityDesignFacet facet) =>
        facet.Kind.Contains("structure", StringComparison.OrdinalIgnoreCase);

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

// The executables list/detail wire shapes now live with the other Publishing view models
// (WorkflowExecutableInspectionViews) and are projected by WorkflowExecutableInspector.

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

internal sealed record UpdateWorkflowDefinitionMetadataRequest(string? Name, string? Description);

internal sealed record UpdateWorkflowDraftRequest(
    WorkflowDefinitionStateView State,
    IReadOnlyCollection<DesignMetadataRecord> Layout);

internal sealed record PromoteDraftResponse(string VersionId);

internal sealed record WorkflowManagementErrorResponse(string Error);

internal sealed record ActivityCatalogResponse(IReadOnlyList<ActivityCatalogItemResponse> Activities);

internal sealed record SaveActivityAvailabilitySettingsRequest(
    string? Scope,
    ActivityAvailabilityModels.ActivityAvailabilityManagementMode Mode,
    SaveActivityAvailabilityRulesRequest? Rules);

internal sealed record SaveActivityAvailabilityRulesRequest(string[]? ActivityTypes, string[]? Sets);

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

internal sealed record ActivityDescriptorsResponse(IReadOnlyList<ActivityDescriptorResponse> Items);

internal sealed record ActivityDescriptorResponse(
    string TypeName,
    string Namespace,
    string Name,
    int Version,
    string Category,
    string DisplayName,
    string? Description,
    string Kind,
    IReadOnlyCollection<InputDescriptorResponse> Inputs,
    IReadOnlyCollection<OutputDescriptorResponse> Outputs,
    IReadOnlyCollection<PortDescriptorResponse> Ports,
    IReadOnlyDictionary<string, object> CustomProperties,
    IReadOnlyDictionary<string, object> ConstructionProperties,
    bool IsContainer,
    bool IsBrowsable,
    bool IsStart,
    bool IsTerminal);

internal sealed record InputDescriptorResponse(
    string Name,
    string TypeName,
    string DisplayName,
    string? Description,
    float Order,
    string? Category,
    bool? IsBrowsable,
    string UiHint,
    bool IsWrapped,
    object? DefaultValue,
    string DefaultSyntax,
    object? UiSpecifications,
    bool IsRequired);

internal sealed record OutputDescriptorResponse(
    string Name,
    string TypeName,
    string DisplayName,
    string? Description,
    float Order,
    string? Category,
    bool? IsBrowsable);

internal sealed record PortDescriptorResponse(string Name, string? DisplayName, string Type, bool? IsBrowsable);

internal sealed record DescriptorOptionResponse(string Label, object Value);

internal sealed record ExpressionDescriptorsResponse(IReadOnlyList<ExpressionDescriptorResponse> Items);

internal sealed record ExpressionDescriptorResponse(string Type, string DisplayName, string? Description);

internal sealed record VariableDescriptorsResponse(IReadOnlyList<VariableDescriptorResponse> Descriptors);

internal sealed record VariableDescriptorResponse(string Alias, string DisplayName, string Category, string DefaultEditor);
