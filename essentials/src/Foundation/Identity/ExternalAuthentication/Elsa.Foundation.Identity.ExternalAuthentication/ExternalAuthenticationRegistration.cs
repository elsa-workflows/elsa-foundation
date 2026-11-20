using CShells.Lifecycle;
using Microsoft.Extensions.Hosting;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Foundation.Identity.ExternalAuthentication.Connections;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;
using Elsa.Foundation.Identity.ExternalAuthentication.Admission;
using Elsa.Foundation.Identity.ExternalAuthentication.Flows;
using Elsa.Workflows.Runtime.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Foundation.Identity.ExternalAuthentication;

public static class ExternalAuthenticationRegistration
{
    public static IServiceCollection AddFoundationExternalAuthentication(this IServiceCollection services, Action<ExternalAuthenticationOptions>? configure = null)
    {
        services.AddFoundationIdentityAbstractions();
        services.AddPersistenceCore();
        services.AddDataProtection();
        services.AddOptions<ExternalAuthenticationOptions>();
        if (configure is not null) services.Configure(configure);
        services.TryAddSingleton(TimeProvider.System);
        services.EnsureReplacementContract<IConnectionCatalogStore, ProviderConnectionCatalog>();
        services.EnsureReplacementContract<IConnectionRegistry, DefaultConnectionRegistry>();
        services.EnsureReplacementContract<IConnectionWriter, ConnectionWriter>();
        services.EnsureReplacementContract<IConnectionWriteAuthorizer, PermissionConnectionAuthorizer>();
        services.EnsureReplacementContract<IExternalLinkAuthorizer, ExternalLinkAuthorizer>();
        services.EnsureReplacementContract<IExternalLinkRecovery, ExternalLinkRecovery>();
        services.TryAddScoped<ConnectionProofStore>();
        services.EnsureReplacementContract<IConnectionReadiness, ConnectionRuntimeServices>();
        services.EnsureReplacementContract<ILocalLoginRecovery, ConnectionRuntimeServices>();
        services.EnsureReplacementContract<IConnectionRecovery, ConnectionRuntimeServices>();
        services.EnsureReplacementContract<IConnectionSignInEvidence, ConnectionRuntimeServices>();
        services.EnsureReplacementContract<IExternalAdmissionService, ExternalAdmissionService>();
        services.EnsureReplacementContract<IExternalAuthenticationFlowService, ExternalAuthenticationFlowService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IExternalProvisioningPolicy, JitProvisioningPolicy>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IExternalProvisioningPolicy, MatchProvisioningPolicy>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IShellInitializer, ExternalAuthenticationActivation>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ExternalAuthenticationActivation>());
        return services;
    }
}
