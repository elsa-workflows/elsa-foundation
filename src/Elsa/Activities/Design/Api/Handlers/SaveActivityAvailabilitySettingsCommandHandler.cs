using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Options;
using Elsa.Activities.Design.Core.Stores;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Api.Handlers;

public sealed class SaveActivityAvailabilitySettingsCommandHandler(IActivityAvailabilitySettingsStore settingsStore) : ICommandHandler<SaveActivityAvailabilitySettings, ActivityAvailabilitySettings>
{
    public async Task<ActivityAvailabilitySettings> Handle(SaveActivityAvailabilitySettings command, CancellationToken cancellationToken)
    {
        var settings = new ActivityAvailabilitySettings
        {
            Scope = string.IsNullOrWhiteSpace(command.Scope)
                ? ActivityAvailabilitySettings.HostDefaultScope
                : command.Scope,
            Mode = command.Mode,
            Rules = command.Rules ?? new ActivityAvailabilityRuleSet()
        };

        await settingsStore.SaveAsync(settings, cancellationToken);

        return settings;
    }
}
