using CShells.Features;
using Elsa.Activities.Composition.Runtime.Constructors;
using Elsa.Activities.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Composition.Runtime
{
    /// <summary>
    /// Runtime-side of the Workflow activity kind: the <c>WorkflowDefinitionActivity</c> backing type
    /// and the <c>WorkflowActivityConstructor</c> (descriptor type
    /// <c>Elsa.Workflows.Primitives.WorkflowIdentity</c>). References no <c>Elsa.*.Design.*</c> project
    /// (Elsa §E2.2). Contributes its constructor to the runtime constructor registry via DI.
    /// </summary>
    [ShellFeature(
        name: "ActivitiesCompositionRuntime",
        DisplayName = "Activities Composition (Runtime)",
        Description = "Runtime construction of workflow-backed activities (the Workflow kind)."
    )]
    public class ActivitiesCompositionRuntimeFeature : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<IActivityConstructor, WorkflowActivityConstructor>();
        }
    }
}
