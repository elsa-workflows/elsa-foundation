using CShells.Features;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Reconciliation.Contracts;
using Elsa.Workflows.Runtime.Reconciliation.Core.Contracts;
using Elsa.Workflows.Runtime.Reconciliation.Options;
using Elsa.Workflows.Runtime.Reconciliation.Services;
using Elsa.Workflows.Runtime.Reconciliation.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Reconciliation;

/// <summary>
/// Executable-artifact reconciliation base feature: it arms the import lifecycle — the reconciler, its options and
/// the startup pass — and leaves <see cref="Sources"/> to concrete source-variant features.
/// </summary>
/// <remarks>
/// <para>
/// Abstract and carrying <b>no</b> <c>[ShellFeature]</c> attribute, so it is never composable on its own: arming
/// the lifecycle without a source would run a pass over nothing on every boot. Concrete features (JSON today; a
/// blob or OCI source later) extend it, which is §2.5's sanctioned cross-feature structural coupling — the same
/// shape the design-side <c>WorkflowsDesignReconciliationFeature</c> uses. It is deliberately <b>not sealed</b>
/// (§2.23.3): sealing a feature class amputates that pattern.
/// </para>
/// <para>
/// Registering the startup task here rather than per source-variant means enabling <em>any</em> concrete
/// reconciliation feature activates the previously-dormant lifecycle exactly once, however many sources are
/// composed — the reconciler already loops over all of them.
/// </para>
/// </remarks>
public abstract class WorkflowsArtifactReconciliationFeature : IShellFeature
{
    public WorkflowArtifactReconcilerStartupTaskOptions StartupTaskOptions { get; set; } = new();

    /// <summary>
    /// Sources contributed directly by the host, in addition to whatever a concrete feature registers in DI. A
    /// fan-in surface: sources contribute, they never replace one another.
    /// </summary>
    public virtual IEnumerable<IWorkflowArtifactReconciliationSource> Sources { get; set; } = [];

    public virtual void ConfigureServices(IServiceCollection services)
    {
        // Idempotent per ADR 0029 — every registration inside is TryAdd — so composing this feature beside any
        // other runtime feature is safe, and a runtime-only engine gets the execution spine it needs to actually
        // run what this feature imports.
        services.AddWorkflowRuntime();

        foreach (var source in Sources)
            services.AddSingleton(source);

        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(StartupTaskOptions));

        // TryAdd, not Add: the reconciler is a single-implementation contract, so §2.6.2 forbids letting a host's
        // replacement be decided by registration order. First-wins is the convention the three runtime
        // replacement contracts already use, so a host registers its own BEFORE composing the feature — the same
        // gesture everywhere, rather than "before" here and "after" there.
        services.TryAddScoped<IWorkflowArtifactReconciler, WorkflowArtifactReconciler>();

        // T124. The foreign-owner outcome is the default, not the whole story: a deployment that expects its mount
        // to own its definitions may want the same condition reported as a rejection. Same §2.6.2 shape and same
        // first-wins gesture as the reconciler above — what it can answer is deliberately narrow, and never
        // includes taking the slot.
        services.TryAddScoped<IArtifactForeignOwnerPolicy, SkipArtifactForeignOwnerPolicy>();
        services.AddScoped<IStartupTask, WorkflowArtifactReconcilerStartupTask>();
    }
}
