using CShells.Lifecycle;
using Elsa.ActorFoundation.Hosting;
using Elsa.Workbench.Readiness;
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
        var workflows = new WorkflowRegistry().Add(DirectApprovalWorkflow.Build());
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
            });
            // The node starts once the default shell is active, so its first turns find the evaluator.
            return new ShellGatedHostedService(new PostgresPreviewHostedService(Open, node, new WorkflowActor([model]), Environment.FailFast),
                token => shells.GetOrActivateAsync(shellName, token), Environment.FailFast);
        });
        if (configuration["Elsa:CorePreview:ReadsTenant"] is { } readsTenant)
            services.AddSingleton(new WorkbenchCorePreviewReads(Open, EmbeddedTenant.Bind(readsTenant)));

        KernelDb Open() => timedJobs ? PostgresPreviewHosting.CreateTimedNativeJobContext(options) :
            nativeJobs ? PostgresPreviewHosting.CreateNativeJobContext(options) : new(options);
    }
}
