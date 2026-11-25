using CShells.Lifecycle;
using Elsa.ActorFoundation.Hosting;
using Elsa.Activities.Design.Core.Options;
using Elsa.Workbench.Readiness;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.KernelHost;
using Kernel.EntityFramework;
using Kernel.Foundation.Postgres;
using Kernel.Native;
using Kernel.Native.Preview;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WorkflowActor = Kernel.WorkflowActor.WorkflowActor;

namespace Elsa.Workbench.CorePreview;

internal static class WorkbenchCorePreview
{
    /// <summary>
    /// With <c>Elsa:CorePreview:Publications</c> on (and the Core Preview enabled), the default shell's publications run on
    /// the kernel: it serves each published definition on the preview node, so a published workflow starts on the kernel.
    /// </summary>
    internal const string PublicationsKey = "Elsa:CorePreview:Publications";

    /// <summary>Why a Timer or Cron publish fails on the Core Preview: its PostgreSQL foundation hosts no schedule actor.</summary>
    internal const string SchedulesUnavailable =
        "Timer and Cron start triggers do not run on the PostgreSQL Core Preview in release 1: its foundation hosts no schedule actor. " +
        "Remove the Timer or Cron, or start the workflow another way (an HTTP endpoint, an event or a dispatch).";

    internal static bool ServesPublications(IConfiguration configuration) =>
        configuration.GetValue<bool>("Elsa:CorePreview:Enabled") && configuration.GetValue<bool>(PublicationsKey);

    /// <summary>
    /// Enables the kernel's shell feature in the default shell when the Core Preview serves publications, so that one switch
    /// composes both sides: the host's serving state here, and the shell's coordinator there.
    /// </summary>
    internal static void ComposeKernelShell(IConfigurationManager configuration)
    {
        if (!ServesPublications(configuration)) return;
        var shell = configuration[$"{ShellReadinessOptions.SectionName}:{nameof(ShellReadinessOptions.DefaultShellName)}"] is { Length: > 0 } named ? named : "default";
        configuration.AddInMemoryCollection([new($"CShells:Shells:{shell}:Features:{WorkflowsRuntimeKernelFeature.FeatureName}", "true")]);
    }

    internal static void AddWorkbenchCorePreview(this IServiceCollection services, IConfiguration configuration)
    {
        if (!configuration.GetValue<bool>("Elsa:CorePreview:Enabled"))
            return;

        var connection = configuration.GetConnectionString("KernelCorePreview");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("The core preview requires its own ConnectionStrings:KernelCorePreview.");

        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        var nativeJobs = configuration.GetValue<bool>("Elsa:CorePreview:NativeJobs");
        var timedJobs = configuration.GetValue<bool>("Elsa:CorePreview:TimedNativeJobs");
        if (nativeJobs && timedJobs)
            throw new InvalidOperationException("Choose one Native job format for the Core Preview database.");
        // With publications on, the kernel's serving state is shared by this host's node and the default shell, which publishes:
        // the node runs the registry it serves into. Registered by instance, so each shell resolves this one.
        var publications = configuration.GetValue<bool>(PublicationsKey);
        var serving = publications ? new KernelServing(CronosCalendar.Error) : null;
        if (serving is not null)
        {
            services.AddSingleton(serving);
            services.AddSingleton<IScheduleActors>(new UnavailableScheduleActors(SchedulesUnavailable));
            // Studio's toolbox hides the activities the kernel does not run in release 1 (NativeActivityCatalog).
            services.Configure<ActivityAvailabilityOptions>(options =>
            {
                options.Sets[NativeActivityCatalog.KernelUnsupportedSet] = [.. NativeActivityCatalog.Unsupported];
                options.Exclude.Sets = [.. options.Exclude.Sets.Where(set => set != NativeActivityCatalog.KernelUnsupportedSet), NativeActivityCatalog.KernelUnsupportedSet];
            });
        }
        var workflows = (serving?.Registry ?? new WorkflowRegistry()).Add(DirectApprovalWorkflow.Build());
        if (nativeJobs) workflows.Add(DirectApprovalWorkflow.BuildWithJob());
        if (timedJobs)
        {
            workflows.Add(DirectApprovalWorkflow.BuildWithTimedJob()).Add(DirectApprovalWorkflow.BuildWithTimedJob(timesOut: true))
                .Add(DirectApprovalWorkflow.BuildWithTimedTakeover());
            var releaseFile = configuration["Elsa:CorePreview:TimedJobReleaseFile"];
            if (!string.IsNullOrWhiteSpace(releaseFile))
            {
                if (!Path.IsPathFullyQualified(releaseFile))
                    throw new InvalidOperationException("The Core Preview job release file requires an absolute path.");
                workflows.Add(DirectApprovalWorkflow.BuildWithTimedTakeover(releaseFile));
            }
        }
        // The host's engines for the activities that need one: RunJavaScript runs on the old core's isolated Jint evaluator,
        // with its limits from Elsa:CorePreview:JavaScript (the Jint defaults where unset), and SendHttpRequest's job sends
        // with a client that follows no redirect and keeps no cookie.
        var javaScript = new Elsa.Expressions.JavaScript.Jint.Options.FeatureOptions();
        configuration.GetSection("Elsa:CorePreview:JavaScript").Bind(javaScript);
        var scripts = new JintNativeScripts(Elsa.Expressions.JavaScript.Jint.Services.JintScriptEvaluators.Create(javaScript));
        var node = $"workbench-core-preview-{Environment.ProcessId}-{Guid.NewGuid():N}";
        services.AddHostedService(provider =>
        {
            // Host expressions (JavaScript, Liquid) run on the default shell's portable evaluator, in a scope of that shell.
            var shells = provider.GetRequiredService<IShellRegistry>();
            var shellName = provider.GetRequiredService<IOptions<ShellReadinessOptions>>().Value.DefaultShellName;
            var expressions = new PortableNativeExpressions(() =>
            {
                var shell = shells.GetActive(shellName) ?? throw new NativeExpressionsUnavailableException($"The shell '{shellName}' is not active.");
                IShellScope scope;
                try { scope = shell.BeginScope(); }
                catch (Exception closing) when (closing is ObjectDisposedException or InvalidOperationException)
                { throw new NativeExpressionsUnavailableException($"The shell '{shellName}' is going away.", closing); }
                return (scope.ServiceProvider, scope);
            });
            var model = new NativeExecutionModel(workflows, new NativeExecutionOptions
            {
                Scripts = scripts,
                Http = new NativeHttpOptions { Client = NativeHttpOptions.CreateClient() },
                Expressions = expressions,
                // A Dispatch names a definition; the child starts the version the definition serves.
                ServedWorkflow = serving is null ? null : serving.ServedWorkflow,
            });
            // The node starts once the default shell is active, so its first turns find the evaluator.
            var logger = provider.GetRequiredService<ILogger<PostgresPreviewHostedService>>();
            return new ShellGatedHostedService(new PostgresPreviewHostedService(Open, node, new WorkflowActor([model]), Environment.FailFast,
                    logger: logger),
                token => shells.GetOrActivateAsync(shellName, token), Environment.FailFast,
                // Once the node runs, the default shell serves every slot its publications left active.
                started: serving is null ? null : token => ServeActiveSlotsAsync(shells, shellName, logger, token));
        });
        if (configuration["Elsa:CorePreview:ReadsTenant"] is { } readsTenant)
            services.AddSingleton(new WorkbenchCorePreviewReads(Open, EmbeddedTenant.Bind(readsTenant)));

        KernelDb Open() => timedJobs ? PostgresPreviewHosting.CreateTimedNativeJobContext(options) :
            nativeJobs ? PostgresPreviewHosting.CreateNativeJobContext(options) : new(options);
    }

    /// <summary>
    /// Serves the default shell's active slots on the kernel, as a host that starts does. A slot that cannot be served is
    /// logged and the others still serve; a shell whose coordinator is not the kernel's is logged, since its feature is missing.
    /// </summary>
    private static async Task ServeActiveSlotsAsync(IShellRegistry shells, string shellName, ILogger logger, CancellationToken cancellationToken)
    {
        try { await ServeAsync(); }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(error, "The Core Preview could not serve the publications of shell {Shell} on the kernel.", shellName);
        }

        async Task ServeAsync()
        {
            var shell = shells.GetActive(shellName) ?? throw new InvalidOperationException($"The shell '{shellName}' is not active.");
            await using var scope = shell.BeginScope();
            if (scope.ServiceProvider.GetRequiredService<IWorkflowActivationCoordinator>() is not KernelActivationCoordinator coordinator)
            {
                logger.LogError("The Core Preview serves publications, but shell {Shell} does not compose the {Feature} feature, so its publications do not run on the kernel.",
                    shellName, WorkflowsRuntimeKernelFeature.FeatureName);
                return;
            }
            var results = await coordinator.ServeActiveSlotsAsync(cancellationToken);
            foreach (var result in results.Where(result => !result.Succeeded))
                logger.LogError("Definition {Definition} slot {Slot} does not serve on the kernel: {Diagnostic}", result.Slot.WorkflowDefinitionId, result.Slot.SlotName, result.Diagnostic);
            logger.LogInformation("The Core Preview serves the publications of shell {Shell} on the kernel: {Served} of {Active} active slots serve.",
                shellName, results.Count(result => result.Succeeded), results.Count);
        }
    }
}
