using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;

namespace Elsa.Workflows.Runtime.Services.Triggers;

internal sealed class StandaloneTriggerTargetDiscovery(TriggerTargetResolver resolver,
    IWorkflowExecutableSourceReferenceStore references, TimeProvider clock) : IStandaloneTriggerTargetDiscovery
{
    public async ValueTask<TriggerTargetDescription?> DescribeAsync(TriggerScope scope, string definitionId, string slotName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId); ArgumentException.ThrowIfNullOrWhiteSpace(slotName);
        var target = await resolver.ResolveAsync(scope, definitionId, slotName, cancellationToken);
        return Describe(definitionId, slotName, target);
    }

    public async ValueTask<TriggerTargetPage> ListAsync(TriggerScope scope, TriggerTargetQuery query, CancellationToken cancellationToken = default)
    {
        resolver.RequireScope(scope);
        RuntimeStorePageRequest.ValidateLimit(query.Limit, nameof(query));
        var result = new List<TriggerTargetDescription>();
        var cursor = query.After;
        // One source per page permits returning the provider's exact continuation without losing an overfetched slot.
        // Limit each HTTP query's scan; a continuation may accompany an empty filtered result.
        for (var scanned = 0; scanned < RuntimeStorePageRequest.MaximumLimit; scanned++)
        {
            var page = await references.ListPageAsync(new(WorkflowExecutableReferenceScope.Published, true,
                clock.GetUtcNow(), 1, cursor), cancellationToken);
            cursor = page.NextContinuationToken;
            if (page.Items.Count == 1)
            {
                var source = page.Items[0];
                if (source.TenantId == scope.TenantId && source.SlotId is not null && source.ActivationId is not null &&
                    (string.IsNullOrWhiteSpace(query.Search) || source.DefinitionId.Contains(query.Search, StringComparison.OrdinalIgnoreCase)))
                {
                    var target = await resolver.ResolveAsync(scope, source.DefinitionId, SlotName(source), cancellationToken);
                    if (target.Source?.SourceReferenceId == source.SourceReferenceId)
                        result.Add(Describe(source.DefinitionId, target.Slot!.SlotName, target));
                }
            }
            if (cursor is null || result.Count >= query.Limit) break;
        }
        return new(result, cursor);
    }

    private static TriggerTargetDescription Describe(string definitionId, string slotName, ResolvedTriggerTarget target) =>
        new(definitionId, slotName, definitionId, target.Model?.ModelKey ?? target.Material?.Model?.Protocol.Key ?? "native",
            target.Available ? TriggerTargetState.Available : target.Slot?.ActiveActivationId is null ? TriggerTargetState.Unpublished : TriggerTargetState.Unavailable,
            target.InputContract, target.InputContract is null ? null : RuntimeIngressAdmission.ComputeInputContractFingerprint(target.InputContract),
            target.Source?.DefinitionVersionId, target.Source?.ArtifactVersion, target.Source?.ActivationId, target.Slot?.Revision,
            target.Model?.SupportsWorkflowOwnedHttpResponse ?? false, target.Findings);

    private static string SlotName(WorkflowExecutableSourceReference source)
    {
        var prefix = $"activation-slot:{source.DefinitionId.Length}:{source.DefinitionId}:";
        if (source.SlotId is not { } id || !id.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidDataException("A publication source has invalid logical slot provenance.");
        var suffix = id[prefix.Length..];
        var separator = suffix.IndexOf(':');
        if (separator < 1 || !int.TryParse(suffix[..separator], System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var length) || length <= 0 || suffix.Length - separator - 1 != length)
            throw new InvalidDataException("A publication source has invalid logical slot provenance.");
        return suffix[(separator + 1)..];
    }
}
