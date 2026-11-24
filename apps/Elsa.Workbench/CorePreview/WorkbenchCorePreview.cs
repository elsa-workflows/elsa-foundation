using Kernel.EntityFramework;
using Kernel.Foundation.Postgres;
using Kernel.Native;
using Kernel.Native.Preview;
using Microsoft.EntityFrameworkCore;
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
            workflows.Add(DirectApprovalWorkflow.BuildWithTimedJob()).Add(DirectApprovalWorkflow.BuildWithTimedJob(timesOut: true))
                .Add(DirectApprovalWorkflow.BuildWithTimedTakeover());
        var model = new NativeExecutionModel(workflows);
        var actor = new WorkflowActor([model]);
        var node = $"workbench-core-preview-{Environment.ProcessId}-{Guid.NewGuid():N}";
        services.AddHostedService(_ => new PostgresPreviewHostedService(Open, node, actor, Environment.FailFast));

        KernelDb Open() => timedJobs ? PostgresPreviewHosting.CreateTimedNativeJobContext(options) :
            nativeJobs ? PostgresPreviewHosting.CreateNativeJobContext(options) : new(options);
    }
}
