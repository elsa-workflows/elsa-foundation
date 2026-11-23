using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Stores;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Primitives.Diagnostics;

namespace Elsa.Activities.Design.Api.Services;

/// <summary>The recommended-definition picker projection the Design endpoints dispatch to.</summary>
public sealed class RecommendedActivityDefinitionReader(
    IRecommendedActivityDefinitionPickerStore picker,
    IActivityAvailabilityEvaluator availabilityEvaluator,
    IActivityAvailabilitySettingsStore settingsStore,
    IActivityAuthoringContextAsync context) : IRecommendedActivityDefinitionReader
{
    public async Task<RecommendedActivityDefinitionPageView> ListAsync(
        ListRecommendedActivityDefinitions request,
        CancellationToken cancellationToken)
    {
        if (request.Offset < 0 || request.Limit is < 1 or > 100)
            throw new ActivityAuthoringException(400, ActivityErrorCodes.RequestInvalid, "Invalid picker request", "Offset must be non-negative and limit must be between 1 and 100.");
        var page = await picker.ReadAsync(context.TenantId, request.Offset, request.Limit, cancellationToken);
        var settings = await settingsStore.LoadAsync(ActivityAvailabilitySettings.HostDefaultScope, cancellationToken);
        var availableKeys = availabilityEvaluator.FilterAddable(page.Items.Select(x => x.Definition), settings)
            .Select(x => x.ActivityTypeKey)
            .ToHashSet(StringComparer.Ordinal);
        return new(
            page.Items.Select(item =>
            {
                var available = availableKeys.Contains(item.Definition.ActivityTypeKey);
                return new RecommendedActivityDefinitionView(
                    item.Definition.Id,
                    item.Definition.ActivityTypeKey,
                    item.Definition.TenantId,
                    item.Definition.Category,
                    string.IsNullOrWhiteSpace(item.Definition.DisplayName) ? item.Definition.ActivityTypeKey : item.Definition.DisplayName,
                    item.Definition.Description,
                    item.Version.DefinitionVersionId,
                    item.Version.Version,
                    available,
                    available ? null : "Excluded by the effective activity availability policy.");
            }).ToArray(),
            page.NextOffset);
    }
}

/// <summary>The recommended-definition picker seam.</summary>
public interface IRecommendedActivityDefinitionReader
{
    Task<RecommendedActivityDefinitionPageView> ListAsync(ListRecommendedActivityDefinitions request, CancellationToken cancellationToken);
}
