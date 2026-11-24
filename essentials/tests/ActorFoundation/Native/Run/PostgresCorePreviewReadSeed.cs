using System.Text.Json;
using Kernel.EntityFramework;
using Kernel.Foundation.Postgres;
using Kernel.Native.Preview;
using Kernel.WorkflowActor;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;
using PublicActor = Kernel.WorkflowActor.WorkflowActor;

namespace Kernel.Native.Run;

internal static class PostgresCorePreviewReadSeed
{
    internal static async Task<int> RunAsync(string original, string seedFile, bool timed)
    {
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Use the dedicated Workbench preview test database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        var store = new EfStore(() => timed ? PostgresPreviewHosting.CreateTimedNativeJobContext(options) : new(options), postgres: true);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var tenant = EmbeddedTenant.Bind("workbench-preview");
        var reads = await store.OpenPreviewRootReadsAsync(cancellationToken: token);
        var definition = DirectApprovalWorkflow.Build().Id;
        var current = await reads.ListAsync(tenant, new(PublicActor.ActorType), cancellationToken: token);
        var actor = current.Items.SingleOrDefault(view => view.Header.Ref.Id == original)?.Header;
        var neighbors = current.Items.Where(view => view.Header.Ref.Id != original).Select(view => view.Header).ToArray();
        var expectedStatus = timed ? Contract.ActorStatus.Finished : Contract.ActorStatus.Running;
        var expectedDefinition = timed ? DirectApprovalWorkflow.TimedDefinition : definition;
        var validNeighbors = timed
            ? neighbors is [var neighbor] && neighbor.Status == Contract.ActorStatus.Finished && neighbor.EndedAt is > 0 &&
                neighbor.Labels.GetValueOrDefault("definition") == definition
            : neighbors.Length == 0;
        if (File.Exists(seedFile) || !validNeighbors || actor?.Status != expectedStatus ||
            actor.Labels.GetValueOrDefault("definition") != expectedDefinition || timed && actor.EndedAt is not > 0)
            throw new InvalidOperationException("The page fixture requires a fresh Workbench birth receipt and test database.");

        var ingress = await store.OpenPreviewRootIngressAsync(PublicActor.ActorType, TimeSpan.FromHours(1), token);
        var runtime = new WorkflowRuntime(ingress, reads);
        var roots = new List<string> { original };
        roots.AddRange(neighbors.Select(neighbor => neighbor.Ref.Id));
        // One more actor than the Workbench's fixed page. All actors are born by the live Workbench owner.
        for (var index = 0; index < 25 - neighbors.Length; index++)
        {
            var operation = FormattableString.Invariant($"read-page-start-{index}");
            var result = await runtime.StartAsync(tenant, operation, NativeExecutionModel.Start(definition), cancellationToken: token);
            if (result is not { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } root })
                throw new InvalidOperationException("The provider did not admit a fresh actor for the page test.");
            roots.Add(root.Id);
        }
        await File.WriteAllTextAsync(seedFile, JsonSerializer.Serialize(roots), token);
        Console.WriteLine("ok admitted 26 exact actors for the Workbench preview read pages");
        return 0;
    }
}
