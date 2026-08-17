using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Diagnostics;

namespace Elsa.Activities.Design.Api.Handlers;

public sealed class ActivityContractProposalService(
    IActivityDefinitionDraftStore drafts,
    IActivityDefinitionAuthoringStore authoring,
    IActivityProviderRegistry providers,
    IApplyActivityContractProposalCommand applyProposal,
    ActivityContractAuthoringValidator contractValidator,
    ReusableActivityAuthoringService projections,
    IActivityAuthoringContextAsync context)
{
    public async Task<ActivityContractProposalView> ProposeAsync(
        ProposeReusableActivityContract request,
        CancellationToken cancellationToken)
    {
        var draft = await GetBoundDraftAsync(request, cancellationToken);
        return await BuildProposalAsync(draft, cancellationToken);
    }

    public async Task<ReusableActivityDraftView> ApplyAsync(
        ApplyReusableActivityContractProposal request,
        CancellationToken cancellationToken)
    {
        if (request.SelectedChangeIds.Count == 0 || request.SelectedChangeIds.Any(string.IsNullOrWhiteSpace))
            throw BadRequest("At least one non-empty proposal change id must be selected.");
        if (request.SelectedChangeIds.Distinct(StringComparer.Ordinal).Count() != request.SelectedChangeIds.Count)
            throw BadRequest("Proposal change ids must be unique.");

        var binding = new ProposeReusableActivityContract(
            request.DraftId,
            request.ExpectedRevision,
            request.ExpectedProviderKey,
            request.ExpectedProviderSchemaVersion,
            request.ExpectedManifestFingerprint);
        var draft = await GetBoundDraftAsync(binding, cancellationToken);
        var proposal = await BuildProposalAsync(draft, cancellationToken);
        if (!StringComparer.Ordinal.Equals(proposal.ProposalFingerprint, request.ProposalFingerprint))
            throw StaleProposal("The provider proposal changed after it was reviewed.");
        if (proposal.Diagnostics.Any(x => x.Severity == ActivityDiagnosticSeverity.Error))
            throw new ActivityAuthoringException(
                422,
                "activity.contract.proposal-rejected",
                "Activity contract proposal cannot be applied",
                "The exact provider proposal contains errors.",
                proposal.Diagnostics);

        var selected = request.SelectedChangeIds.ToHashSet(StringComparer.Ordinal);
        if (selected.Except(proposal.Changes.Select(x => x.ChangeId), StringComparer.Ordinal).Any())
            throw StaleProposal("One or more selected changes are not present in the exact provider proposal.");

        var updatedContract = ApplyChanges(draft.State.Contract, proposal.Changes.Where(x => selected.Contains(x.ChangeId)));
        var diagnostics = contractValidator.Validate(
            updatedContract,
            new("ActivityDraft", draft.Id, draft.DefinitionId, Revision: draft.Revision));
        if (diagnostics.Any(x => x.Severity == ActivityDiagnosticSeverity.Error))
            throw new ActivityAuthoringException(
                422,
                "activity.contract.capability-rejected",
                "Activity contract proposal is not authorable",
                "The proposed contract uses capabilities outside the activated catalog.",
                diagnostics);

        try
        {
            await applyProposal.ExecuteAsync(new(
                draft.Id,
                context.TenantId,
                draft.Revision,
                request.ExpectedProviderKey,
                request.ExpectedProviderSchemaVersion,
                request.ExpectedManifestFingerprint,
                updatedContract), cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            throw new ActivityAuthoringException(
                409,
                "activity.contract.proposal-stale",
                "Activity contract proposal is stale",
                "The draft changed while the exact provider proposal was being applied.",
                innerException: exception);
        }

        return await projections.GetDraftViewAsync(draft.Id, cancellationToken);
    }

    private async Task<ActivityDefinitionDraft> GetBoundDraftAsync(
        ProposeReusableActivityContract request,
        CancellationToken cancellationToken)
    {
        var draft = await drafts.FindAsync(request.DraftId, cancellationToken)
                    ?? throw new ActivityAuthoringException(404, "activity.draft.not-found", "Activity draft not found", "The requested activity draft was not found.");
        if (draft.TenantId is not null && !StringComparer.Ordinal.Equals(draft.TenantId, context.TenantId))
            throw new ActivityAuthoringException(403, "activity.tenant.reference-denied", "Activity authoring is forbidden", "The requested activity draft is outside the caller's tenant scope.");
        var owner = await authoring.FindAsync(draft.DefinitionId, cancellationToken)
                    ?? throw new ActivityAuthoringException(404, "activity.definition.not-found", "Activity definition not found", "The draft's activity definition was not found.");
        if (owner.ContentAuthority.Kind != ActivityContentAuthorityKind.Design)
            throw new ActivityAuthoringException(409, "activity.definition.content-authority", "Activity definition is source-owned", "Provider proposals cannot mutate a source-owned activity definition.");
        if (!await context.CanAuthorProviderAsync(draft.State.Provider.ProviderKey, cancellationToken))
            throw new ActivityAuthoringException(403, ActivityErrorCodes.AuthorizationDenied, "Activity authoring is forbidden", "The caller is not authorized to author this activity provider.");
        if (draft.Status != ActivityDefinitionDraftStatus.Active ||
            draft.Revision != request.ExpectedRevision ||
            !StringComparer.Ordinal.Equals(draft.State.Provider.ProviderKey, request.ExpectedProviderKey) ||
            !StringComparer.Ordinal.Equals(draft.State.Provider.SchemaVersion, request.ExpectedProviderSchemaVersion) ||
            !StringComparer.Ordinal.Equals(ActivityProviderManifestFingerprint.Compute(draft.State.Provider), request.ExpectedManifestFingerprint))
            throw StaleProposal("The draft revision or provider binding changed after it was read.");

        IActivityProvider provider;
        try
        {
            provider = providers.Resolve(request.ExpectedProviderKey, request.ExpectedProviderSchemaVersion);
        }
        catch (InvalidOperationException exception)
        {
            throw new ActivityAuthoringException(
                422,
                "activity.provider.schema-unavailable",
                "Activity provider schema is unavailable",
                "The exact provider schema is not available for authoring.",
                innerException: exception);
        }
        if (provider.AuthoringCapabilities.ManifestSchemas.All(x =>
                !StringComparer.Ordinal.Equals(x.SchemaVersion, request.ExpectedProviderSchemaVersion) || !x.IsAuthorable))
            throw new ActivityAuthoringException(
                422,
                "activity.provider.schema-not-authorable",
                "Activity provider schema is not authorable",
                "The exact provider schema cannot produce mutable contract proposals.");
        return draft;
    }

    private async Task<ActivityContractProposalView> BuildProposalAsync(
        ActivityDefinitionDraft draft,
        CancellationToken cancellationToken)
    {
        var provider = providers.Resolve(draft.State.Provider.ProviderKey, draft.State.Provider.SchemaVersion);
        var result = await provider.ProposeContractAsync(new(draft.State.Provider, draft.State.Contract), cancellationToken);
        if (result.Diagnostics is null)
            throw InvalidProviderProposal();
        var changes = Normalize(result.Changes);
        var diagnostics = ActivityDiagnosticOrderer.Order(result.Diagnostics);
        var manifestFingerprint = ActivityProviderManifestFingerprint.Compute(draft.State.Provider);
        var proposalFingerprint = ComputeFingerprint(
            draft.Id,
            draft.Revision,
            draft.State.Provider.ProviderKey,
            draft.State.Provider.SchemaVersion,
            manifestFingerprint,
            changes,
            diagnostics);
        return new(
            draft.Id,
            draft.Revision,
            draft.State.Provider.ProviderKey,
            draft.State.Provider.SchemaVersion,
            manifestFingerprint,
            proposalFingerprint,
            changes.Select(ToView).ToArray(),
            diagnostics);
    }

    private static IReadOnlyList<ActivityContractProposalChange> Normalize(IReadOnlyList<ActivityContractProposalChange>? changes)
    {
        if (changes is null || changes.Any(x => x is null) ||
            changes.Any(x => string.IsNullOrWhiteSpace(x.ChangeId) || string.IsNullOrWhiteSpace(x.ReferenceKey)) ||
            changes.Select(x => x.ChangeId).Distinct(StringComparer.Ordinal).Count() != changes.Count ||
            changes.Select(x => (x.MemberKind, x.ReferenceKey)).Distinct().Count() != changes.Count ||
            changes.Any(IsMalformed))
            throw InvalidProviderProposal();
        return changes.OrderBy(x => x.ChangeId, StringComparer.Ordinal).ToArray();
    }

    private static bool IsMalformed(ActivityContractProposalChange change)
    {
        if (!Enum.IsDefined(change.Operation) || !Enum.IsDefined(change.MemberKind))
            return true;
        var payloadCount = (change.Input is null ? 0 : 1) + (change.Output is null ? 0 : 1) + (change.Outcome is null ? 0 : 1);
        if (change.Operation == ActivityContractProposalOperation.Remove)
            return payloadCount != 0;
        if (payloadCount != 1)
            return true;
        return change.MemberKind switch
        {
            ActivityContractMemberKind.Input => change.Input is null || !StringComparer.Ordinal.Equals(change.Input.ReferenceKey, change.ReferenceKey),
            ActivityContractMemberKind.Output => change.Output is null || !StringComparer.Ordinal.Equals(change.Output.ReferenceKey, change.ReferenceKey),
            ActivityContractMemberKind.Outcome => change.Outcome is null || !StringComparer.Ordinal.Equals(change.Outcome.ReferenceKey, change.ReferenceKey),
            _ => true
        };
    }

    private static ActivityContract ApplyChanges(
        ActivityContract contract,
        IEnumerable<ActivityContractProposalChangeView> changes)
    {
        var inputs = contract.Inputs.ToList();
        var outputs = contract.Outputs.ToList();
        var outcomes = contract.Outcomes.ToList();
        foreach (var change in changes)
        {
            switch (change.MemberKind)
            {
                case ActivityContractMemberKind.Input:
                    Apply(inputs, change.ReferenceKey, change.Operation, change.Input is null ? null : ToDomain(change.Input));
                    break;
                case ActivityContractMemberKind.Output:
                    Apply(outputs, change.ReferenceKey, change.Operation, change.Output is null ? null : ToDomain(change.Output));
                    break;
                case ActivityContractMemberKind.Outcome:
                    Apply(outcomes, change.ReferenceKey, change.Operation, change.Outcome is null ? null : new(change.Outcome.ReferenceKey, change.Outcome.Name, change.Outcome.IsEmitted, change.Outcome.Description));
                    break;
                default:
                    throw InvalidProviderProposal();
            }
        }
        return new(contract.ContractSchemaVersion, inputs, outputs, outcomes);
    }

    private static void Apply<T>(List<T> members, string referenceKey, ActivityContractProposalOperation operation, T? replacement)
        where T : class
    {
        var index = members.FindIndex(x => StringComparer.Ordinal.Equals(ReferenceKey(x), referenceKey));
        if ((operation == ActivityContractProposalOperation.Add && index >= 0) ||
            (operation != ActivityContractProposalOperation.Add && index < 0) ||
            (operation != ActivityContractProposalOperation.Remove && replacement is null))
            throw StaleProposal("A selected proposal change no longer applies to the exact contract member.");
        if (operation == ActivityContractProposalOperation.Remove)
            members.RemoveAt(index);
        else if (operation == ActivityContractProposalOperation.Replace)
            members[index] = replacement!;
        else
            members.Add(replacement!);
    }

    private static string ReferenceKey<T>(T member) => member switch
    {
        ActivityInputContract input => input.ReferenceKey,
        ActivityOutputContract output => output.ReferenceKey,
        ActivityOutcomeContract outcome => outcome.ReferenceKey,
        _ => throw InvalidProviderProposal()
    };

    private static ActivityOutputContract ToDomain(ActivityOutputContractView output) => new(
        output.ReferenceKey,
        output.Name,
        output.Type.ToDomain(),
        output.IsRequired,
        output.IsNullable,
        output.StorageDriverKey,
        output.Durability,
        output.DisplayName,
        output.Description,
        output.Category,
        output.Order,
        output.UiHint,
        output.UiSpecifications?.Clone());

    private static ActivityInputContract ToDomain(ActivityInputContractView input) => new(
        input.ReferenceKey,
        input.Name,
        input.Type.ToDomain(),
        input.IsRequired,
        input.IsNullable,
        input.Default is null ? null : new(input.Default.Syntax, input.Default.Value.Clone()),
        input.StorageDriverKey,
        input.Durability,
        input.DisplayName,
        input.Description,
        input.Category,
        input.Order,
        input.UiHint,
        input.UiSpecifications?.Clone());

    private static ActivityContractProposalChangeView ToView(ActivityContractProposalChange change)
    {
        ActivityInputContractView? input = null;
        ActivityOutputContractView? output = null;
        ActivityOutcomeContractView? outcome = null;
        if (change.Input is not null)
            input = new ActivityContract("1", [change.Input], [], []).ToView().Inputs[0];
        if (change.Output is not null)
            output = new ActivityContract("1", [], [change.Output], []).ToView().Outputs[0];
        if (change.Outcome is not null)
            outcome = new(change.Outcome.ReferenceKey, change.Outcome.Name, change.Outcome.IsEmitted, change.Outcome.Description);
        return new(change.ChangeId, change.Operation, change.MemberKind, change.ReferenceKey, input, output, outcome);
    }

    private static string ComputeFingerprint(
        string draftId,
        long revision,
        string providerKey,
        string providerSchemaVersion,
        string manifestFingerprint,
        IReadOnlyList<ActivityContractProposalChange> changes,
        IReadOnlyList<ActivityDiagnostic> diagnostics)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            draftId,
            revision,
            providerKey,
            providerSchemaVersion,
            manifestFingerprint,
            changes,
            diagnostics
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return $"sha256:{Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}";
    }

    private static ActivityAuthoringException BadRequest(string detail) =>
        new(400, ActivityErrorCodes.RequestInvalid, "Invalid activity authoring request", detail);

    private static ActivityAuthoringException StaleProposal(string detail) =>
        new(409, "activity.contract.proposal-stale", "Activity contract proposal is stale", detail);

    private static ActivityAuthoringException InvalidProviderProposal() =>
        new(502, "activity.provider.proposal-invalid", "Activity provider proposal is invalid", "The provider returned a malformed contract proposal.");
}

public sealed class ProposeReusableActivityContractHandler(ActivityContractProposalService service)
    : IRequestHandler<ProposeReusableActivityContract, ActivityContractProposalView>
{
    public Task<ActivityContractProposalView> Handle(ProposeReusableActivityContract request, CancellationToken cancellationToken) =>
        service.ProposeAsync(request, cancellationToken);
}

public sealed class ApplyReusableActivityContractProposalHandler(ActivityContractProposalService service)
    : ICommandHandler<ApplyReusableActivityContractProposal, ReusableActivityDraftView>
{
    public Task<ReusableActivityDraftView> Handle(ApplyReusableActivityContractProposal command, CancellationToken cancellationToken) =>
        service.ApplyAsync(command, cancellationToken);
}
