using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Stores;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Diagnostics;

namespace Elsa.Activities.Design.Api.Handlers;

public sealed class ListRecommendedActivityDefinitionsHandler(
    IRecommendedActivityDefinitionPickerStore picker,
    IActivityAvailabilityEvaluator availabilityEvaluator,
    IActivityAvailabilitySettingsStore settingsStore,
    IActivityAuthoringContext context)
    : IRequestHandler<ListRecommendedActivityDefinitions, RecommendedActivityDefinitionPageView>
{
    public async Task<RecommendedActivityDefinitionPageView> Handle(
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
