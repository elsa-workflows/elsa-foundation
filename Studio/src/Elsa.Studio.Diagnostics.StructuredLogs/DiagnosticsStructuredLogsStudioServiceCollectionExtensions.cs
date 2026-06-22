using Elsa.Studio.Core.Events;
using Elsa.Studio.Core.Services;
using Elsa.Studio.Diagnostics.StructuredLogs.Handlers;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Diagnostics.StructuredLogs;

public static class DiagnosticsStructuredLogsStudioServiceCollectionExtensions
{
    public static IServiceCollection AddDiagnosticsStructuredLogsStudio(this IServiceCollection services)
    {
        return services.AddStudioEventHandler<OnStudioModuleManifestsCollecting, ContributeDiagnosticsStructuredLogsStudioModule>();
    }
}
