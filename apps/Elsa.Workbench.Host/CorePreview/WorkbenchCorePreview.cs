using CShells.Lifecycle;
using Elsa.ActorFoundation.Hosting;
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

    /// <summary>Why a Timer or Cron publish is refused on the Core Preview, whose PostgreSQL foundation hosts no schedule actor yet.</summary>
    internal const string SchedulesUnavailable =
        "Timer and Cron triggers don't run on this host yet. Remove the Timer or Cron, or start the workflow from an HTTP endpoint, an event or a Dispatch.";

    internal static bool ServesPublications(IConfiguration configuration) =>
        configuration.GetValue<bool>("Elsa:CorePreview:Enabled") && configuration.GetValue<bool>(PublicationsKey);

    /// <summary>
    /// Enables the kernel's shell feature in the default shell when the Core Preview serves publications, so that one switch
    /// composes both sides: the host's serving state here, and the shell's coordinator there. The feature is written as
    /// shells.json writes one with no settings, an empty object. Returns the shell it composed the feature into, or null.
    /// </summary>
    internal static string? ComposeKernelShell(IConfigurationManager configuration)
    {
        if (!ServesPublications(configuration)) return null;
        var shell = configuration[$"{ShellReadinessOptions.SectionName}:{nameof(ShellReadinessOptions.DefaultShellName)}"] is { Length: > 0 } named ? named : "default";
        var json = new System.Text.Json.Nodes.JsonObject
        {
            ["CShells"] = new System.Text.Json.Nodes.JsonObject
            {
                ["Shells"] = new System.Text.Json.Nodes.JsonObject
                {
                    [shell] = new System.Text.Json.Nodes.JsonObject
                    {
                        ["Features"] = new System.Text.Json.Nodes.JsonObject { [WorkflowsRuntimeKernelFeature.FeatureName] = new System.Text.Json.Nodes.JsonObject() },
                    },
                },
            },
        };
        configuration.AddJsonStream(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString())));
        return shell;
    }

    /// <summary>The startup line that says the Core Preview composed the kernel's feature into the default shell.</summary>
    internal static void LogKernelShell(ILogger logger, string shell) =>
        logger.LogInformation("The Core Preview composed the {Feature} feature into shell {Shell}: its publications serve on the kernel.",
            WorkflowsRuntimeKernelFeature.FeatureName, shell);

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
        // Its foundation hosts no schedule actor yet, so serving refuses a Timer or Cron before anything is minted.
        var serving = publications ? new KernelServing(CronosCalendar.Error, SchedulesUnavailable) : null;
        if (serving is not null)
        {
            services.AddSingleton(serving);
            services.AddSingleton<IScheduleActors>(new UnavailableScheduleActors(SchedulesUnavailable));
            KernelToolbox.Register(services);
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
                // An instance started on a version no slot serves since the restart runs it from the default shell's artifacts.
                ResolveWorkflow = serving is null ? null : (workflow, token) => ResolveWorkflowAsync(shells, shellName, workflow, token),
            });
            // The node starts once the default shell is active, so its first turns find the evaluator.
            var logger = provider.GetRequiredService<ILogger<PostgresPreviewHostedService>>();
            return new ShellGatedHostedService(new PostgresPreviewHostedService(Open, node, new WorkflowActor([model]), Environment.FailFast,
                    logger: logger),
                token => shells.GetOrActivateAsync(shellName, token), Environment.FailFast,
                // Before the node runs a turn, the default shell serves every slot its publications left active, so a due turn
                // of an instance in flight finds its workflow served.
                starting: serving is null ? null : token => ServeActiveSlotsAsync(shells, shellName, logger, token));
        });
        if (configuration["Elsa:CorePreview:ReadsTenant"] is { } readsTenant)
            services.AddSingleton(new WorkbenchCorePreviewReads(Open, EmbeddedTenant.Bind(readsTenant)));

        KernelDb Open() => timedJobs ? PostgresPreviewHosting.CreateTimedNativeJobContext(options) :
            nativeJobs ? PostgresPreviewHosting.CreateNativeJobContext(options) : new(options);
    }

    /// <summary>The workflow an instance runs, from the default shell's executable store, for one the registry does not hold.</summary>
    private static async ValueTask<Workflow?> ResolveWorkflowAsync(IShellRegistry shells, string shellName, string workflow, CancellationToken cancellationToken)
    {
        var shell = shells.GetActive(shellName) ?? throw new InvalidOperationException($"The shell '{shellName}' is not active.");
        await using var scope = shell.BeginScope();
        return await NativeExecutableMapper.MapStoredAsync(scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>(), workflow, cancellationToken);
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
