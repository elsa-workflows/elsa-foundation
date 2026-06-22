using Elsa.Studio.Core.Events;
using Elsa.Studio.Core.Services;
using Elsa.Studio.Diagnostics.OpenTelemetry.Handlers;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Diagnostics.OpenTelemetry;

public static class DiagnosticsOpenTelemetryStudioServiceCollectionExtensions
{
    public static IServiceCollection AddDiagnosticsOpenTelemetryStudio(this IServiceCollection services)
    {
        return services.AddStudioEventHandler<OnStudioModuleManifestsCollecting, ContributeDiagnosticsOpenTelemetryStudioModule>();
    }
}
