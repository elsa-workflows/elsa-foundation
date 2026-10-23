using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Core.Stores;

public interface IActivityAvailabilitySettingsStore
{
    Task<ActivityAvailabilitySettings?> LoadAsync(string scope, CancellationToken cancellationToken = default);

    Task SaveAsync(ActivityAvailabilitySettings settings, CancellationToken cancellationToken = default);
}
