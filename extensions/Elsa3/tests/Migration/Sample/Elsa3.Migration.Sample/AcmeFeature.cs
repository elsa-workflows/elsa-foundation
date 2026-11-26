using CShells.Features;
using Elsa.Expressions.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Acme;

// catalog:case-18 begin
// The user's own Elsa 4 feature. The migration host references this project and lists the feature in its
// local settings; the activities are found in the feature's assembly, so they need no registration here.
[ShellFeature("Acme", DisplayName = "Acme", Description = "Acme activities, script functions and variable types.")]
public class AcmeFeature : IShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        // Services the activities take through their constructors (case 10).
        services.TryAddSingleton<IInvoiceClient, InMemoryInvoiceClient>();

        // The trigger's stimulus provider (case 9).
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IActivityTriggerStimulusProvider, InvoiceReceivedStimulusProvider>());

        // JavaScript host functions (case 14).
        services.AddSingleton(AcmeScriptFunctions.LookupCustomer);

        // Variable types (case 16).
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IVariableTypeDescriptorProvider, AcmeVariableTypes>());
    }
}
// catalog:case-18 end
