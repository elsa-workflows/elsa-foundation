using System.Reflection;
using Elsa.Persistence.EFCore;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.EFCore.Commands;
using Elsa.Workflows.Design.Persistence.EFCore.Contracts;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Elsa.Workflows.Design.Persistence.EFCore.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Design.Persistence.EFCore;

public abstract class EFCoreWorkflowsPersistenceFeatureBase : EFCorePersistenceShellFeatureBase<WorkflowsDesignDbContext>
{
    // The saving/loading handlers (WorkflowDefinition{Version,Draft}{Saving,Loading}Handler) live in
    // THIS assembly. The base registers both directions from this list, so neither can be forgotten.
    protected override IEnumerable<Assembly> EntityHandlerAssemblies =>
        [GetType().Assembly, typeof(EFCoreWorkflowsPersistenceFeatureBase).Assembly];

    protected override void OnAfterConfigured(IServiceCollection services)
    {
        // Entity construction: factories that build read-models for new definitions/versions/drafts.
        services.AddScoped<IWorkflowDefinitionFactory, WorkflowDefinitionFactory>();
        services.AddScoped<IWorkflowDefinitionVersionFactory, WorkflowDefinitionVersionFactory>();
        services.AddScoped<IWorkflowDefinitionDraftFactory, WorkflowDefinitionDraftFactory>();
        services.TryAddScoped<IActivityStructureService, DefaultActivityStructureService>();

        if (UseCommands)
        {
            services
                .AddScoped<IAddWorkflowDefinitionCommand, AddWorkflowDefinition>()
                .AddScoped<ISubmitWorkflowDefinitionCommand, SubmitWorkflowDefinition>()
                // Lifecycle origination + cloning + discard (NOT mutations — kept distinct, FR-003)
                .AddScoped<ICreateDraftCommand, CreateDraft>()
                .AddScoped<ICloneDraftFromVersionCommand, CloneDraftFromVersion>()
                .AddScoped<IPromoteDraftToVersionCommand, PromoteDraftToVersion>()
                .AddScoped<IDiscardDraftCommand, DiscardDraft>()
                // The single coarse diff-based Draft-mutation command (Unit 2) + its diff engine.
                // Supersedes the former 20 granular mutation commands.
                .AddScoped<IDraftStateDiffEngine, DraftStateDiffEngine>()
                .AddScoped<IUpdateDraftCommand, UpdateDraft>();
        }

        if (UseQueries)
        {
            services.AddScoped<IWorkflowDefinitionStore, EFCoreWorkflowDefinitionStore>();
            services.AddScoped<IWorkflowDefinitionVersionStore, EFCoreWorkflowDefinitionVersionStore>();
            services.AddScoped<IWorkflowDefinitionLookup, WorkflowDefinitionLookup>();
        }
    }
}
