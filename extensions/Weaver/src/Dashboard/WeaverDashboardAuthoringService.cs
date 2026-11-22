using System.Text.Json;
using System.Runtime.CompilerServices;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Studio.Dashboard.Core.Authoring;
using Elsa.Studio.Dashboard.Core.Contracts;
using Elsa.Studio.Dashboard.Core.Models;
using Elsa.Studio.Dashboard.Core.Resources;
using Elsa.Studio.Dashboard.Core.Exceptions;

namespace Elsa.Weaver.Dashboard;

internal sealed class WeaverDashboardAuthoringService(
    DashboardWeaverAccess access,
    IDashboardQueryService queries,
    IDashboardResourceService resources,
    IWeaverSessionService sessions,
    IWeaverProviderRegistry providers,
    IWeaverStreamingService streaming) : IDashboardAuthoringService
{
    public async ValueTask<DashboardAuthoringContext> PrepareAsync(DashboardAuthoringDraft draft, CancellationToken cancellationToken = default)
    {
        DashboardWeaverProtocol.ValidateDraft(draft);
        if (draft.Definition is { } unselected && (unselected.Query?.Dataset is not { } source ||
            string.IsNullOrWhiteSpace(source.Id) && (draft.Target.ResourceType != "question-draft" || !IsCanonicalBlank(unselected))))
            throw new DashboardResourceException("validation", "Select a dataset before disclosing a populated question draft. A new unselected draft must be empty.");
        var actor = await access.CurrentAsync(cancellationToken);
        if (!actor.Permissions.Contains("*") && !actor.Permissions.Contains("studio/questions:read"))
            throw new DashboardResourceException("denied", "Question read access is required for Dashboard authoring.");
        var canSave = actor.Permissions.Contains("*") || actor.Permissions.Contains("studio/questions:create");
        var canEdit = canSave;
        if (draft.Target.ResourceType == "question")
        {
            var current = await resources.GetAsync(actor, DashboardResourceKinds.Question, draft.Target.ResourceId, cancellationToken);
            if (current.Revision != draft.Target.Revision)
                throw new DashboardResourceException("conflict", "This saved question changed. Reload it before asking Weaver to refine it.");
            if (current.DefinitionState != "available" || current.Definition is null)
                throw new DashboardResourceException("denied", "The current question definition cannot be disclosed for authoring.");
            canEdit = current.Capabilities.CanEdit;
            canSave = current.Capabilities.CanEdit;
            if (!canEdit && draft.Definition is not null && DashboardWeaverProtocol.Fingerprint(draft.Definition) != DashboardWeaverProtocol.Fingerprint(current.Definition))
                throw new DashboardResourceException("denied", "Copy this question before refining its definition.");
        }
        var scope = access.Scope(actor);
        IReadOnlyList<DashboardDatasetDeclaration> datasets;
        try { datasets = await queries.DiscoverAsync(scope, cancellationToken); }
        catch (DashboardQueryException error)
        { throw new DashboardResourceException("unavailable", "The current dataset declarations could not be established. Retry after source support recovers.", error); }
        IReadOnlyList<DashboardQueryProblem> problems = [];
        if (draft.Definition is { } definition && !string.IsNullOrWhiteSpace(definition.Query.Dataset.Id))
        {
            DashboardQueryValidation validation;
            try { validation = await queries.ValidateAsync(scope, definition, cancellationToken); }
            catch (DashboardQueryException error)
            { throw new DashboardResourceException("unavailable", "Current query support could not be established. Retry after source support recovers.", error); }
            problems = validation.Problems;
            // Old or forbidden source metadata is not admitted just because its author still has a draft.
            if (!validation.IsValid)
                throw new DashboardResourceException("validation", "The current draft cannot be disclosed. Repair its source, fields or parameters in the ordinary editor first.");
        }
        if (draft.Definition?.Query.Dataset.Id is { Length: > 0 } selected)
            datasets = datasets.Where(dataset => dataset.Id == selected).ToArray();
        var context = new DashboardAuthoringContext(draft, datasets, canEdit, canSave, problems);
        DashboardWeaverProtocol.RequireContextBudget(context);
        return context;
    }

    // This is the native editor's initialDefinition() shape, not a partially validated query.
    // Any populated request must pass the dataset owner's current declaration/field validation.
    private static bool IsCanonicalBlank(DashboardQueryRequest definition) => definition is
    {
        Query: { SchemaVersion: 1, Dataset: { Id: "", SchemaRevision: "" }, Form: "aggregate",
            Parameters: { Count: 0 }, Filters: null, Fields: { Count: 0 }, GroupBy: { Count: 0 },
            Metrics: { Count: 0 }, Time: null, Comparison: null, Having: null, Sort: { Count: 0 }, Limit: 50 },
        Visualization: { Kind: "table", X: null, Y: { Count: 0 }, Label: null },
        ParameterValues: { Count: 0 }
    };

    public async IAsyncEnumerable<DashboardAuthoringEvent> AuthorAsync(DashboardAuthoringRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Message) || request.Message.Length > 4096)
            throw new DashboardResourceException("validation", "Describe the question in 1–4096 characters.");
        var prepared = await PrepareAsync(request.Draft, cancellationToken);
        if (!prepared.CanEdit)
            throw new DashboardResourceException("denied", "Create your own question draft before asking Weaver to author changes.");
        var actor = await access.CurrentAsync(cancellationToken);
        var provider = providers.Active;
        if (provider is null)
        {
            yield return new("failed", request.ConversationId ?? "", "Weaver is unavailable. Ordinary question editing and refresh remain available.", Code: "dashboards.provider.unavailable");
            yield break;
        }
        WeaverSession session;
        if (request.ConversationId is { Length: > 0 } existing)
        {
            await access.RequireSessionAsync(existing, cancellationToken);
            session = await sessions.FindAsync(existing, cancellationToken)
                ?? throw new DashboardResourceException("notFound", "The authoring conversation is unavailable.");
            if (session.ProviderId != provider.ProviderId)
                throw new DashboardResourceException("unavailable", "The conversation's provider changed. Start a new authoring conversation with the current draft.");
        }
        else
        {
            var diagnostics = await provider.GetDiagnosticsAsync(cancellationToken);
            if (!diagnostics.IsAvailable)
            {
                yield return new("failed", "", "Weaver's configured provider is unavailable. Keep working with the ordinary editor.", Code: "dashboards.provider.unavailable");
                yield break;
            }
            session = await sessions.CreateAsync(new(actor.TenantId, actor.AuthenticationSubjectId!,
                request.Draft.Target.ResourceId, provider.ProviderId, "build", "Dashboard question",
                WeaverPolicy.Default with { AutonomyMode = WeaverAutonomyMode.AutoReadOnly, MaxAutonomyMode = WeaverAutonomyMode.AutoReadOnly },
                new Dictionary<string, string> { ["route"] = "/questions", ["resourceType"] = request.Draft.Target.ResourceType,
                    ["resourceId"] = request.Draft.Target.ResourceId }) { Workspace = "dashboards" }, cancellationToken);
            await provider.CreateSessionAsync(session, cancellationToken);
        }
        // The generic engine re-admits retained evidence through its installed owner. A raw
        // client attachment has no server disclosure provenance and cannot be persisted as one.
        var context = new[] { DashboardWeaverProtocol.Admitted(prepared), DashboardWeaverProtocol.InstalledGuidance() };
        await sessions.AddMessageAsync(session.Id, new(WeaverRole.User, request.Message, WeaverMessageStatus.Pending,
            null, context.Select(item => item.Id).ToArray(), context) { Client = DashboardWeaverProtocol.Client(request.Draft),
            Source = new(DashboardWeaverProtocol.ModuleId, "dashboards.author", "Author a dashboard question"),
            HasDisclosureProvenance = true, ExpectedPolicyRevision = session.PolicyRevision }, cancellationToken);
        yield return new("started", session.Id);
        await foreach (var item in streaming.StreamAsync(session.Id, cancellationToken))
        {
            if (item.Kind == WeaverStreamEventKind.MessageDelta)
                yield return new("message", session.Id, item.Content);
            else if (item.Kind == WeaverStreamEventKind.Error)
                yield return new("failed", session.Id, item.Error?.Message ?? "The authoring request failed.", Code: item.Error?.Code);
            else if (item.Kind == WeaverStreamEventKind.TurnCancelled)
                yield return new("cancelled", session.Id, "Authoring cancelled. Your native draft is preserved.");
            else if (item.Kind == WeaverStreamEventKind.ContributedResult && item.Payload is WeaverToolResult result && result.Payload is { } payload)
            {
                if (result.Access is not { ModuleId: DashboardWeaverProtocol.ModuleId, ContractVersion: "1",
                        DisclosureSourceId: DashboardWeaverProtocol.SourceId, Target: { } target } ||
                    !DashboardWeaverProtocol.Identity(result.Access.InvocationId, 256) || payload.SchemaVersion != "1" ||
                    !DashboardWeaverProtocol.SameTarget(target, DashboardWeaverProtocol.Target(request.Draft.Target)))
                    throw new DashboardResourceException("validation", "The authoring result does not belong to the current Dashboard question.");
                if (payload.TypeId == DashboardWeaverProtocol.ProposalType)
                {
                    if (result.ToolName != "dashboard_preview_question")
                        throw new DashboardResourceException("validation", "Only the Dashboard question tool can return an authoring proposal.");
                    var proposal = payload.Data.Deserialize<DashboardAuthoringProposal>(DashboardWeaverProtocol.Json)
                        ?? throw new WeaverContributionException("The Dashboard authoring proposal is malformed.");
                    if (proposal.Preview is not null || proposal.Target != request.Draft.Target || proposal.Client != request.Draft.Client ||
                        proposal.WorkingRevision != request.Draft.WorkingRevision ||
                        proposal.DefinitionFingerprint != DashboardWeaverProtocol.Fingerprint(proposal.Definition))
                        throw new DashboardResourceException("conflict", "The proposal belongs to an older question or editor. Preview the current draft again.");
                    // Measured data is returned to this native caller, never inserted into the model transcript.
                    await PrepareAsync(request.Draft with { Definition = proposal.Definition }, cancellationToken);
                    var currentActor = await access.RequireSessionAsync(session.Id, cancellationToken);
                    var preview = await queries.ExecuteAsync(access.Scope(currentActor), proposal.Definition, cancellationToken);
                    yield return new("proposal", session.Id, Proposal: proposal with { Preview = preview });
                }
                else if (payload.TypeId == DashboardWeaverProtocol.ClarificationType)
                {
                    var clarification = payload.Data.Deserialize<DashboardClarificationResult>(DashboardWeaverProtocol.Json);
                    if (result.ToolName != "dashboard_request_clarification" || clarification is null ||
                        clarification.Target != request.Draft.Target || clarification.Client != request.Draft.Client ||
                        clarification.WorkingRevision != request.Draft.WorkingRevision)
                        throw new DashboardResourceException("conflict", "The clarification belongs to an older question or editor.");
                    yield return new("clarification", session.Id, Clarification: clarification.Clarification);
                }
            }
            else if (item.Kind == WeaverStreamEventKind.Completed)
                yield return new("completed", session.Id);
        }
    }
}
