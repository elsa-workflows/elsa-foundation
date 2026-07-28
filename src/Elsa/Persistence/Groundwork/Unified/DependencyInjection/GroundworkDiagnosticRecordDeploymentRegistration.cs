using CShells.Lifecycle;
using Groundwork.DiagnosticRecords;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Persistence.Groundwork.Unified.DependencyInjection;

/// <summary>
/// Registers the shared diagnostic-record deployment initializer after document-schema preparation.
/// </summary>
public static class GroundworkDiagnosticRecordDeploymentRegistration
{
    public static IServiceCollection AddGroundworkDiagnosticRecordDeploymentInitializer(
        this IServiceCollection services,
        bool autoApplyOnStartup)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(descriptor =>
                descriptor.ServiceType == typeof(GroundworkDiagnosticRecordDeploymentInitializer)))
        {
            return services;
        }

        services.AddSingleton(provider => new GroundworkDiagnosticRecordDeploymentInitializer(
            autoApplyOnStartup,
            provider.GetServices<IDiagnosticRecordDeploymentManifestSource>(),
            provider.GetRequiredService<IDiagnosticRecordDeploymentApplier>(),
            provider.GetService<ILogger<GroundworkDiagnosticRecordDeploymentInitializer>>()
            ?? NullLogger<GroundworkDiagnosticRecordDeploymentInitializer>.Instance));
        services.AddHostedService(provider =>
            provider.GetRequiredService<GroundworkDiagnosticRecordDeploymentInitializer>());
        services.AddSingleton<IShellInitializer>(provider =>
            provider.GetRequiredService<GroundworkDiagnosticRecordDeploymentInitializer>());
        services.AddSingleton(new ShellInitializerRegistration(
            typeof(GroundworkDiagnosticRecordDeploymentInitializer),
            LifecyclePhase.Prepare,
            Order: 1,
            RegistrationIndex: -1,
            IsExplicit: true,
            Source:
            $"{nameof(GroundworkDiagnosticRecordDeploymentRegistration)}.{nameof(AddGroundworkDiagnosticRecordDeploymentInitializer)}"));
        return services;
    }
}
