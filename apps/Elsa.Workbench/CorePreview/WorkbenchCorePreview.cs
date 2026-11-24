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
        var model = new NativeExecutionModel(new WorkflowRegistry().Add(DirectApprovalWorkflow.Build()));
        var actor = new WorkflowActor([model]);
        var node = $"workbench-core-preview-{Environment.ProcessId}-{Guid.NewGuid():N}";
        services.AddHostedService(_ => new PostgresPreviewHostedService(() => new KernelDb(options), node, actor, Environment.FailFast));
    }
}
