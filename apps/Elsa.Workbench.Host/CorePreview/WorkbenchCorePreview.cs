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

    /// <summary>
    /// The tenant the Core Preview's HTTP edge starts workflows in. Unset, it is <c>Elsa:CorePreview:ReadsTenant</c>, or
    /// <see cref="DefaultTenant"/> when that is unset too; the reads list from it when <c>ReadsTenant</c> is unset. The
    /// Embedded foundation binds one tenant per process, so the two set to different tenants are refused.
    /// </summary>
    internal const string TenantKey = "Elsa:CorePreview:Tenant";

    internal const string ReadsTenantKey = "Elsa:CorePreview:ReadsTenant";

    internal const string DefaultTenant = "default";

    /// <summary>Serves the HTTP probe workflow (<see cref="HttpProbeWorkflow"/>), for the Workbench Core Preview check.</summary>
    internal const string HttpProbeKey = "Elsa:CorePreview:HttpProbe";
    internal const string HttpProbePath = "core-preview/probe";

    /// <summary>The probe: a POST to its route answers 200 with the posted body's name.</summary>
    internal static Workflow HttpProbeWorkflow() => Wf.Workflow("core-preview-http-probe", Wf.Sequence("main",
        Wf.HttpEndpoint("start", HttpProbePath),
        Wf.Respond("answer", Binding.Result("start", "Body.name"), 200)));

    internal const string HttpEchoPath = "core-preview/echo";

    /// <summary>The echo probe: a POST to its route answers 200 with the posted body as the edge read it.</summary>
    internal static Workflow HttpEchoWorkflow() => Wf.Workflow("core-preview-http-echo", Wf.Sequence("main",
        Wf.HttpEndpoint("start", HttpEchoPath),
        Wf.Respond("answer", Binding.Result("start", "Body"), 200)));

    /// <summary>The wait probe, which this host refuses: an HTTP Endpoint that waits after the start.</summary>
    internal static Workflow HttpWaitWorkflow() => Wf.Workflow("core-preview-http-wait", Wf.Sequence("main",
        Wf.HttpEndpoint("start", "core-preview/wait"),
        Wf.HttpEndpoint("decision", "core-preview/decision"),
        Wf.Respond("answer", "decided")));

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
        // The release profile: publications run on the shipped format 24, which carries Native jobs with timed settlement,
        // held replies, selected operators and workflow history. A database on an earlier preview format is recreated.
        if (publications && (nativeJobs || timedJobs))
            throw new InvalidOperationException(
                "With Elsa:CorePreview:Publications on, the Core Preview runs on the shipped format, which carries Native jobs: leave NativeJobs and TimedNativeJobs off.");
        // Format 24 runs no schedule actor, timers or child workflows yet, and the edge serves no wait routes: serving refuses
        // a Timer, Cron, Delay, Dispatch Workflow and an HTTP Endpoint that waits where it maps them. Whether the node takes
        // self-sends is the store format's to say; without them a burst runs on to its next wait.
        var capabilities = new KernelHostCapabilities(Schedules: false, Timers: false, Children: false,
            SelfSends: PostgresPreviewHosting.TakesSelfSends(Open), HttpWaits: false);
        string? waitProbeRefusal = null;
        var serving = publications ? new KernelServing(CronosCalendar.Error, capabilities) : null;
        if (serving is not null)
        {
            // An opt-in probe for the Workbench check: a host-served HTTP workflow at {base}/core-preview/probe that answers with
            // the posted name, so a running Workbench proves its edge without a publication.
            // The echo probe answers with the body the edge read, and the wait probe is refused, as a publication with one is.
            if (configuration.GetValue<bool>(HttpProbeKey))
            {
                serving.ServeHostWorkflowAsync(HttpProbeWorkflow()).AsTask().GetAwaiter().GetResult();
                serving.ServeHostWorkflowAsync(HttpEchoWorkflow()).AsTask().GetAwaiter().GetResult();
                try { serving.ServeHostWorkflowAsync(HttpWaitWorkflow()).AsTask().GetAwaiter().GetResult(); }
                catch (InvalidOperationException refused) { waitProbeRefusal = refused.Message; }
            }
            services.AddSingleton(serving);
            services.AddSingleton<IScheduleActors>(new UnavailableScheduleActors(KernelHostCapabilities.SchedulesUnavailable));
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
        // On the shipped format the node answers held callers: the held requests of this host's edge.
        var requests = publications ? PostgresPreviewHosting.CreateRequests(Open, node, WorkflowActor.ActorType) : null;
        var (edgeTenant, readsTenantOrNull) = Tenants(configuration[TenantKey], configuration[ReadsTenantKey]);
        KernelHttpEdgeHost? edge = null;
        if (requests is not null)
        {
            var timeout = configuration.GetValue<TimeSpan?>(KernelHttpEdgeHost.TimeoutKey) ?? KernelHttpEdgeHost.DefaultTimeout;
            if (timeout <= TimeSpan.Zero)
                throw new InvalidOperationException($"{KernelHttpEdgeHost.TimeoutKey} must be longer than zero; it is {timeout}.");
            var maxBody = configuration.GetValue<long?>(KernelHttpEdgeHost.MaxRequestBodyBytesKey) ?? KernelHttpEdgeHost.DefaultMaxRequestBodyBytes;
            if (maxBody <= 0)
                throw new InvalidOperationException($"{KernelHttpEdgeHost.MaxRequestBodyBytesKey} must be a positive number of bytes; it is {maxBody}.");
            services.AddSingleton(requests);
            edge = new KernelHttpEdgeHost(serving!, Open, requests, EmbeddedTenant.Bind(edgeTenant),
                KernelHttpEdgeHost.NormalizeBasePath(configuration[KernelHttpEdgeHost.BasePathKey]), timeout, maxBody);
            services.AddSingleton(edge);
        }
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
                // Without self-sends a turn cannot continue in the next: it runs on to its next wait, within its bounds.
                SelfSends = capabilities.SelfSends,
                // An instance started on a version no slot serves since the restart runs it from the default shell's artifacts.
                ResolveWorkflow = serving is null ? null : (workflow, token) => ResolveWorkflowAsync(shells, shellName, workflow, token),
            });
            // The node starts once the default shell is active, so its first turns find the evaluator.
            var logger = provider.GetRequiredService<ILogger<PostgresPreviewHostedService>>();
            if (waitProbeRefusal is not null) logger.LogInformation("The Core Preview refuses its HTTP wait probe: {Reason}", waitProbeRefusal);
            return new ShellGatedHostedService(new PostgresPreviewHostedService(Open, node, new WorkflowActor([model]), Environment.FailFast,
                    logger: logger, requests: requests),
                // Once the default shell is active, its HTTP base path must be the edge's, or Studio would show addresses the
                // edge does not serve: the node does not start, and the process ends.
                async token =>
                {
                    await shells.GetOrActivateAsync(shellName, token);
                    if (edge is not null) await RequireEdgeBasePathAsync(shells, shellName, edge);
                }, Environment.FailFast,
                // Before the node runs a turn, the default shell serves every slot its publications left active, so a due turn
                // of an instance in flight finds its workflow served.
                starting: serving is null ? null : token => ServeActiveSlotsAsync(shells, shellName, logger, token));
        });
        if (readsTenantOrNull is { } readsTenant)
            services.AddSingleton(new WorkbenchCorePreviewReads(Open, EmbeddedTenant.Bind(readsTenant)));

        KernelDb Open() => publications ? PostgresPreviewHosting.CreateShippedContext(options) :
            timedJobs ? PostgresPreviewHosting.CreateTimedNativeJobContext(options) :
            nativeJobs ? PostgresPreviewHosting.CreateNativeJobContext(options) : new(options);
    }

    /// <summary>
    /// The one tenant of the process: the edge's (Tenant, else ReadsTenant, else <see cref="DefaultTenant"/>) and the reads'
    /// (ReadsTenant, else Tenant, else none). Two different configured tenants are refused.
    /// </summary>
    internal static (string Edge, string? Reads) Tenants(string? tenant, string? readsTenant)
    {
        if (tenant is not null && readsTenant is not null && tenant != readsTenant)
            throw new InvalidOperationException(
                $"{TenantKey} ('{tenant}') and {ReadsTenantKey} ('{readsTenant}') name different tenants, and the Core Preview's " +
                "Embedded foundation binds one tenant per process. Set one of them, or both to the same tenant.");
        return (tenant ?? readsTenant ?? DefaultTenant, readsTenant ?? tenant);
    }

    /// <summary>Refuses an edge whose base path is not the default shell's HTTP base path (its HttpEndpointOptions).</summary>
    private static async Task RequireEdgeBasePathAsync(IShellRegistry shells, string shellName, KernelHttpEdgeHost edge)
    {
        var shell = shells.GetActive(shellName) ?? throw new InvalidOperationException($"The shell '{shellName}' is not active.");
        await using var scope = shell.BeginScope();
        if (scope.ServiceProvider.GetService<IOptions<Elsa.Activities.Http.Options.HttpEndpointOptions>>()?.Value.BasePath is not { } configured)
            return;     // the shell composes no HTTP activities, so Studio shows no endpoint address
        var shellBase = "/" + configured.Trim().Trim('/');
        if (!string.Equals(shellBase, edge.BasePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"The Core Preview's HTTP edge serves {edge.BasePath} ({KernelHttpEdgeHost.BasePathKey}), and shell '{shellName}' gives its HTTP endpoints " +
                $"the base path {shellBase}, which Studio shows. Set both to the same path.");
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
