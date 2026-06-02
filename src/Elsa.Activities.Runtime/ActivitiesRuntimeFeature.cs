using CShells.Features;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Events;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Events;
using Elsa.Activities.Runtime.Handlers;
using Elsa.Activities.Runtime.Resolvers;
using Elsa.Activities.Runtime.Services;
using Elsa.Activities.Runtime.Sources;
using Elsa.Events.Core.Extensions;
using Elsa.Tasks.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Runtime
{
    /// <summary>
    /// Runtime-side feature for activity construction. Registers the factory + resolver
    /// registry + descriptor-type registry + CLR resolver. Contribution handlers in this
    /// assembly publish the CLR kind to both registries at startup.
    /// </summary>
    [ShellFeature(
        name: "ActivitiesRuntime",
        DisplayName = "Activities Runtime",
        Description = "Activity construction factory, resolver registry, and CLR resolver."
    )]
    public class ActivitiesRuntimeFeature : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services)
        {
            // Registries — singletons; populated by their startup tasks.
            services.AddSingleton<IImplementationDescriptorRegistry, Design.Core.Models.ImplementationDescriptorRegistry>();
            services.AddSingleton<IActivityImplementationResolverRegistry, ActivityImplementationResolverRegistry>();

            // CLR resolver — registered concretely so the contributing handler can inject it.
            services.AddSingleton<ClrActivityImplementationResolver>();

            // Factory.
            services.AddScoped<IActivityFactory, ActivityFactory>();

            // Startup tasks publish the contribution events and flush results into the registries.
            services.AddScoped<IStartupTask, ImplementationDescriptorRegistryStartupTask>();
            services.AddScoped<IStartupTask, ActivityImplementationResolverRegistryStartupTask>();

            // Single aggregating handlers + the CLR sources they iterate. Other modules contribute
            // by registering an IActivityImplementationResolverSource / IImplementationDescriptorSource.
            services.AddEventHandler<OnActivityImplementationResolversInitializing, RegisterActivityImplementationResolvers>();
            services.AddEventHandler<OnImplementationDescriptorsInitializing, RegisterImplementationDescriptors>();
            services.AddScoped<IActivityImplementationResolverSource, ClrActivityImplementationResolverSource>();
            services.AddScoped<IImplementationDescriptorSource, ClrImplementationDescriptorSource>();
        }
    }
}
