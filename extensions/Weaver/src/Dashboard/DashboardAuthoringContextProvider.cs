using System.Text.Json;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Studio.Dashboard.Core.Authoring;
using Elsa.Studio.Dashboard.Core.Contracts;
using Elsa.Studio.Dashboard.Core.Models;
using Elsa.Studio.Dashboard.Core.Resources;

namespace Elsa.Weaver.Dashboard;

internal sealed class DashboardAuthoringContextProvider(IDashboardAuthoringService authoring,
    DashboardWeaverAccess access, IDashboardQueryService queries, IDashboardResourceService resources) : IWeaverContextProvider
{
    public WeaverContextProviderDescriptor Descriptor { get; } = new(DashboardWeaverProtocol.SourceId,
        DashboardWeaverProtocol.ModuleId, "dashboard-question",
        [DashboardWeaverProtocol.ContextKind, DashboardWeaverProtocol.GuidanceKind], ["1"], ["question", "question-draft"]);

    public async ValueTask<WeaverContextContribution> CollectAsync(WeaverContextRequest request, CancellationToken cancellationToken = default)
    {
        var inputs = request.CallerAttachments.Where(x => x.ContentType == DashboardWeaverProtocol.ContextKind).ToArray();
        if (inputs.Length == 0) return new([], []);
        try
        {
            await access.RequireSessionAsync(request.SessionId, cancellationToken);
            if (inputs.Length != 1)
                return Refused("dashboards.context.ambiguous", "Select one question editor for authoring.");
            var draft = DashboardWeaverProtocol.ReadDraft(inputs[0]);
            if (request.Client is not null && request.Client != DashboardWeaverProtocol.Client(draft))
                return Refused("dashboards.context.client_mismatch", "The authoring draft belongs to another editor.");
            if (request.Target is not null && !DashboardWeaverProtocol.SameTarget(request.Target, DashboardWeaverProtocol.Target(draft.Target)))
                return Refused("dashboards.context.target_mismatch", "The authoring draft belongs to another question.");
            var context = await authoring.PrepareAsync(draft, cancellationToken);
            return new([DashboardWeaverProtocol.Admitted(context), DashboardWeaverProtocol.InstalledGuidance()], []);
        }
        catch (DashboardResourceException error) { return Refused("dashboards.context." + error.Code, "Current Dashboard context could not be admitted.", error.Code is "unavailable" or "schemaIncompatible"); }
        catch (JsonException) { return Refused("dashboards.context.invalid", "Supply the supported Dashboard authoring context."); }
    }

    public async ValueTask<WeaverContextContribution> AdmitDisclosureAsync(WeaverContextDisclosureRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var actor = await access.RequireSessionAsync(request.SessionId, cancellationToken);
            var contexts = new List<WeaverContextAttachment>();
            foreach (var item in request.Context)
            {
                if (item.ContentType == DashboardWeaverProtocol.GuidanceKind)
                {
                    if (DashboardWeaverProtocol.Element(item.Content).GetString() != DashboardWeaverProtocol.Guidance)
                        return Refused("dashboards.context.guidance_changed", "Start a fresh authoring conversation with current installed guidance.");
                    contexts.Add(DashboardWeaverProtocol.InstalledGuidance());
                }
                else if (item.ContentType == DashboardWeaverProtocol.ContextKind)
                {
                    var draft = DashboardWeaverProtocol.ReadDraft(item);
                    var original = DashboardWeaverProtocol.Element(item.Content).Deserialize<DashboardAuthoringContext>(DashboardWeaverProtocol.Json);
                    var current = await authoring.PrepareAsync(draft, cancellationToken);
                    if (original is null || original.Datasets.Any(old => !current.Datasets.Any(now =>
                            now.Id == old.Id && DashboardWeaverProtocol.Fingerprint(now) == DashboardWeaverProtocol.Fingerprint(old))))
                        return Refused("dashboards.context.support_changed", "The previously disclosed dataset support changed. Start fresh with current authorized metadata.");
                    contexts.Add(DashboardWeaverProtocol.Admitted(current));
                }
                else return Refused("dashboards.context.kind_invalid", "This context version is unsupported.");
            }
            foreach (var result in request.Results)
            {
                if (result.Access?.ModuleId != DashboardWeaverProtocol.ModuleId || result.Access.DisclosureSourceId != Descriptor.Id ||
                    result.Access.ContractVersion != "1" || !DashboardWeaverProtocol.Identity(result.Access.InvocationId, 256) ||
                    result.Payload is not { SchemaVersion: "1" } payload || result.Access.Target is not { } target)
                    return Refused("dashboards.result.provenance_missing", "The Dashboard proposal's disclosure provenance cannot be established.");
                var authority = await access.AuthorizeTargetAsync(actor.AuthenticationSubjectId!, actor.TenantId, target,
                    resources, cancellationToken);
                if (!authority.Allowed)
                    return Refused("dashboards.result.target_unavailable", "The originating question is no longer available.",
                        authority.Violations.Any(x => x.OwnerUnavailable));
                if (payload.TypeId == DashboardWeaverProtocol.ProposalType)
                {
                    if (result.ToolName != "dashboard_preview_question")
                        return Refused("dashboards.result.provenance_invalid", "The originating question tool is unavailable.");
                    var proposal = payload.Data.Deserialize<DashboardAuthoringProposal>(DashboardWeaverProtocol.Json);
                    if (proposal is null || proposal.Preview is not null ||
                        !DashboardWeaverProtocol.SameTarget(target, DashboardWeaverProtocol.Target(proposal.Target)) ||
                        !HasOrigin(contexts, proposal.Target, proposal.Client, proposal.WorkingRevision) ||
                        proposal.DefinitionFingerprint != DashboardWeaverProtocol.Fingerprint(proposal.Definition))
                        return Refused("dashboards.result.invalid", "Protected result evidence cannot be disclosed through an authoring proposal.");
                    await authoring.PrepareAsync(new(proposal.Target, proposal.Client, proposal.WorkingRevision,
                        proposal.Definition, ConfirmedInterpretation: null), cancellationToken);
                    // This transcript contains declarations only, never old source rows or aggregate values.
                    var validation = await queries.ValidateAsync(access.Scope(actor), proposal.Definition, cancellationToken);
                    if (!validation.IsValid) return Refused("dashboards.result.unavailable", "The previously proposed question is no longer currently available.");
                }
                else if (payload.TypeId == DashboardWeaverProtocol.CatalogType)
                {
                    if (result.ToolName != "dashboard_discover_datasets")
                        return Refused("dashboards.result.provenance_invalid", "The originating catalog tool is unavailable.");
                    var catalog = payload.Data.Deserialize<DashboardCatalogResult>(DashboardWeaverProtocol.Json);
                    var current = await queries.DiscoverAsync(access.Scope(actor), cancellationToken);
                    if (catalog is null || catalog.Items.Any(old => !current.Any(now =>
                            now.Id == old.Id && DashboardWeaverProtocol.Fingerprint(now) == DashboardWeaverProtocol.Fingerprint(old))))
                        return Refused("dashboards.result.catalog_changed", "Start fresh with the currently authorized dataset catalog.");
                }
                else if (payload.TypeId == DashboardWeaverProtocol.ClarificationType)
                {
                    if (result.ToolName != "dashboard_request_clarification")
                        return Refused("dashboards.result.provenance_invalid", "The originating clarification tool is unavailable.");
                    if (!DashboardWeaverProtocol.Identity(target.ResourceId, 256))
                        return Refused("dashboards.result.target_invalid", "The authoring target is unavailable.");
                    if (target.ResourceType == "question")
                    {
                        // The question's current resource access is also checked for a retained clarification.
                        var targetContext = contexts.FirstOrDefault(x => x.Target is not null && DashboardWeaverProtocol.SameTarget(x.Target, target));
                        if (targetContext is null) return Refused("dashboards.result.context_missing", "Current question context is required.");
                    }
                    if (payload.Data.Deserialize<DashboardClarificationResult>(DashboardWeaverProtocol.Json) is not { } clarification ||
                        !DashboardWeaverProtocol.SameTarget(target, DashboardWeaverProtocol.Target(clarification.Target)) ||
                        !HasOrigin(contexts, clarification.Target, clarification.Client, clarification.WorkingRevision))
                        return Refused("dashboards.result.invalid", "The clarification is malformed.");
                }
                else return Refused("dashboards.result.unsupported", "This Dashboard result version is unsupported.");
            }
            return new(contexts, []);
        }
        catch (DashboardResourceException error) { return Refused("dashboards.disclosure." + error.Code, "Current Dashboard disclosure authority could not be established.", error.Code is "unavailable" or "schemaIncompatible"); }
        catch (Elsa.Studio.Dashboard.Core.Exceptions.DashboardQueryException)
        { return Refused("dashboards.disclosure.source_unavailable", "Current dataset support could not be established.", true); }
        catch (JsonException) { return Refused("dashboards.disclosure.invalid", "The retained Dashboard proposal is malformed."); }
    }

    private WeaverContextContribution Refused(string code, string message, bool unavailable = false) =>
        new([], [new(Descriptor.Id, code, message, true) { OwnerUnavailable = unavailable }]);

    private static bool HasOrigin(IEnumerable<WeaverContextAttachment> contexts, DashboardAuthoringTarget target,
        DashboardAuthoringClient client, string workingRevision) => contexts.Where(item => item.ContentType == DashboardWeaverProtocol.ContextKind)
        .Select(DashboardWeaverProtocol.ReadDraft).Any(draft => draft.Target == target && draft.Client == client && draft.WorkingRevision == workingRevision);
}

internal sealed record DashboardCatalogResult(IReadOnlyList<DashboardDatasetDeclaration> Items);
