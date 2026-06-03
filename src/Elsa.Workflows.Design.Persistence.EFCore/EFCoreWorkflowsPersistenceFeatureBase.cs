using Elsa.Persistence.EFCore;
using Elsa.Persistence.EFCore.Extensions;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.EFCore.Commands;
using Elsa.Workflows.Design.Persistence.EFCore.Contracts;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Elsa.Workflows.Design.Persistence.EFCore.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Design.Persistence.EFCore
{
    public abstract class EFCoreWorkflowsPersistenceFeatureBase : EFCorePersistenceShellFeatureBase<WorkflowsDesignDbContext>
    {
        protected override void OnAfterConfigured(IServiceCollection services)
        {
            if (UseCommands)
            {
                services
                    .AddScoped<IAddWorkflowDefinitionCommand, AddWorkflowDefinition>()
                    // Lifecycle origination + cloning + discard (NOT mutations — kept distinct, FR-003)
                    .AddScoped<ICreateDraftCommand, CreateDraft>()
                    .AddScoped<ICloneDraftFromVersionCommand, CloneDraftFromVersion>()
                    .AddScoped<IDiscardDraftCommand, DiscardDraft>()
                    // The single coarse diff-based Draft-mutation command (Unit 2) + its diff engine.
                    // Supersedes the former 20 granular mutation commands.
                    .AddScoped<IDraftStateDiffEngine, DraftStateDiffEngine>()
                    .AddScoped<IUpdateDraftCommand, UpdateDraft>()
                    .AddEntitySavingHandlersFrom(GetType().Assembly)
                    .AddEntitySavingHandlersFrom(typeof(EFCoreWorkflowsPersistenceFeatureBase).Assembly);
            }

            if (UseQueries)
            {
                services
                    .AddScoped<IWorkflowDefinitionLookup, WorkflowDefinitionLookup>()
                    .AddEntityLoadingHandlersFrom(GetType().Assembly)
                    .AddEntityLoadingHandlersFrom(typeof(EFCoreWorkflowsPersistenceFeatureBase).Assembly);
            }
        }
    }
}