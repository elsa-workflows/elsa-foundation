using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Options;
using Elsa.Activities.Design.Core.Stores;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Microsoft.Extensions.Options;

namespace Elsa.Activities.Design.Api.Services;

/// <summary>The availability settings and diagnostics operations the Design endpoints dispatch to.</summary>
public sealed class ActivityAvailabilityOperations(
    IActivityDefinitionStore definitionStore,
    IActivityAvailabilitySettingsStore settingsStore,
    IActivityAvailabilityDiagnosticsProjector diagnosticsProjector,
    IOptions<ActivityAvailabilityOptions> options) : IActivityAvailabilityOperations
{
    public async Task<ActivityAvailabilitySettings> GetSettingsAsync(GetActivityAvailabilitySettings request, CancellationToken cancellationToken)
    {
        var scope = string.IsNullOrWhiteSpace(request.Scope)
            ? ActivityAvailabilitySettings.HostDefaultScope
            : request.Scope;

        return await settingsStore.LoadAsync(scope, cancellationToken) ?? new ActivityAvailabilitySettings { Scope = scope };
    }

    public async Task<ActivityAvailabilityDiagnostics> ListDiagnosticsAsync(ListActivityAvailabilityDiagnostics request, CancellationToken cancellationToken)
    {
        var definitions = await definitionStore.ListAsync(new ActivityDefinitionFilter(), cancellationToken);
        var settings = await settingsStore.LoadAsync(ActivityAvailabilitySettings.HostDefaultScope, cancellationToken);

        return diagnosticsProjector.Project(definitions, options.Value, settings);
    }

    public async Task<ActivityAvailabilitySettings> SaveSettingsAsync(SaveActivityAvailabilitySettings command, CancellationToken cancellationToken)
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

/// <summary>The availability operations seam, one method per route.</summary>
public interface IActivityAvailabilityOperations
{
    Task<ActivityAvailabilitySettings> GetSettingsAsync(GetActivityAvailabilitySettings request, CancellationToken cancellationToken);
    Task<ActivityAvailabilityDiagnostics> ListDiagnosticsAsync(ListActivityAvailabilityDiagnostics request, CancellationToken cancellationToken);
    Task<ActivityAvailabilitySettings> SaveSettingsAsync(SaveActivityAvailabilitySettings command, CancellationToken cancellationToken);
}
