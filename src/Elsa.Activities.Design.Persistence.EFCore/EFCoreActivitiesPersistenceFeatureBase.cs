using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.EFCore.DbContext;
using Elsa.Activities.Design.Persistence.EFCore.Services;
using Elsa.Events.Core.Extensions;
using Elsa.Persistence.EFCore;
using Elsa.Persistence.EFCore.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Design.Persistence.EFCore;

public abstract class EFCoreActivitiesPersistenceFeatureBase : EFCorePersistenceShellFeatureBase<ActivitiesDesignDbContext>
{
    protected override void OnBeforeConfiguring(IServiceCollection services)
    {
        if (UseCommands)
        {
            services
                .AddScoped<IAddActivityDefinitionCommand, AddActivityDefinitionCommand>()
                // Activity-catalog saving handlers now run via OnEntitySaving (§2.6.1); pick
                // them up by assembly scan. The legacy IEntitySavingHandler<,> scan stays for
                // any other features that haven't migrated yet — it is a no-op for the
                // activity-catalog assembly because its saving handler is now a domain-event
                // handler.
                .AddEventHandlersFrom(GetType().Assembly)
                .AddEventHandlersFrom(typeof(EFCoreActivitiesPersistenceFeatureBase).Assembly)
                .AddEntitySavingHandlersFrom(GetType().Assembly)
                .AddEntitySavingHandlersFrom(typeof(EFCoreActivitiesPersistenceFeatureBase).Assembly);
        }

        if (UseQueries)
        {
            services.AddScoped<IActivityDefinitionLookup, ActivityDefinitionLookup>();
        }
    }
}
