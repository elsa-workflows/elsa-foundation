using System.Diagnostics;
using System.Text.Json;
using CShells;
using CShells.Lifecycle;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// An operator-only control adapter loaded into an owned real Workbench. Never used for timing.
public static class StartupHook
{
    private static IDisposable? subscription;
    private static int captured;
    public static void Initialize()
    {
        var root = Environment.GetEnvironmentVariable("ELSA_EFFICIENCY_CLUSTER_CONTROL_ROOT");
        if (root is null) return;
        if (!Path.IsPathFullyQualified(root) || !Directory.Exists(root)) throw new ArgumentException("Use an owned absolute control directory.");
        Emit(root, "initialized.json", new { processId = Environment.ProcessId, timingEligible = false });
        subscription = DiagnosticListener.AllListeners.Subscribe(new HostingListener(root));
    }
    private static void Emit(string root, string name, object value)
    {
        using var file = new FileStream(Path.Combine(root, name), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        JsonSerializer.Serialize(file, value, new JsonSerializerOptions { WriteIndented = true });
    }
    private sealed class HostingListener(string root) : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>
    {
        private readonly List<IDisposable> listeners = [];
        public void OnNext(DiagnosticListener value) { if (value.Name == "Microsoft.Extensions.Hosting") listeners.Add(value.Subscribe(this)); }
        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Key != "HostBuilt" || value.Value is not IHost host || Interlocked.Exchange(ref captured, 1) != 0) return;
            Emit(root, "host-captured.json", new { host = host.GetType().FullName });
            host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStarted.Register(() => _ = Task.Run(() => Control(host, root)));
        }
        public void OnCompleted() { }
        public void OnError(Exception error) { Emit(root, "listener-error.json", new { errorType = error.GetType().FullName }); }
    }
    private static async Task Control(IHost host, string root)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        try
        {
            var ct = deadline.Token;
            var membership = host.Services.GetRequiredService<IClusterMembership>();
            var local = membership.GetLocalStanding();
            if (!local.Identity.HostId.StartsWith("eff-i68-", StringComparison.Ordinal) || membership.ProviderKind != ClusterProviderKind.Durable)
                throw new InvalidOperationException("Control requires an owned durable-membership fixture.");
            var shell = await host.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync("default", ct);
            Emit(root, "ready.json", new { hostId = local.Identity.HostId, provider = membership.GetType().FullName, fleet = await membership.ReadFleetAsync(FleetReadMode.Fresh, ct) });
            var requestPath = Path.Combine(root, "cancel.json");
            while (!File.Exists(requestPath)) await Task.Delay(50, ct);
            var request = JsonSerializer.Deserialize<CancelRequest>(await File.ReadAllTextAsync(requestPath, ct))!;
            if (!request.OwnerHostId.StartsWith("eff-i68-", StringComparison.Ordinal) || request.OwnerHostId == local.Identity.HostId ||
                !request.CommandKey.StartsWith("eff-i68-", StringComparison.Ordinal)) throw new ArgumentException("Use distinct owned sender/owner identities and a synthetic command key.");
            await using var shellScope = shell.BeginScope();
            var factory = shellScope.ServiceProvider.GetRequiredService<IPersistenceOperationScopeFactory>();
            var partition = new WorkflowExecutionPartition(PersistenceScope.DefaultValue);
            await using var operation = await factory.CreateAsync(new PersistenceScope(partition.Value), ct);
            var services = operation.ServiceProvider;
            var state = await services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(request.WorkflowExecutionId, ct)
                ?? throw new InvalidOperationException("The requested execution is absent.");
            var placement = await services.GetRequiredService<IExecutionPlacementStore>().FindAsync(request.WorkflowExecutionId, ct)
                ?? throw new InvalidOperationException("The requested execution is unplaced.");
            if (state.PinnedExecutable.ArtifactId != request.ArtifactId || state.Status != WorkflowExecutionStatus.Running || placement.OwnerId != request.OwnerHostId)
                throw new InvalidOperationException("The actual live execution pin/placement must match the owned request.");
            var actors = services.GetRequiredService<IWorkflowExecutionActorProvider>();
            Emit(root, "before-cancel.json", new { state, placement, sender = local.Identity.HostId, actorProvider = actors.GetType().FullName,
                transport = services.GetRequiredService<IExecutionCommandTransport>().GetType().FullName,
                fleet = await membership.ReadFleetAsync(FleetReadMode.Fresh, ct) });
            var now = DateTimeOffset.UtcNow;
            var metadata = new Dictionary<string, string> { ["qualification"] = "runtime-efficiency-i67" };
            var command = new WorkflowExecutionCommand(request.CommandKey, request.WorkflowExecutionId, WorkflowExecutionCommandKind.Cancel, now, null, metadata);
            var envelope = new WorkflowExecutionCommandEnvelope(request.CommandKey + "-envelope", request.WorkflowExecutionId, command,
                request.CommandKey, WorkflowExecutionCommandDeliveryMode.AtLeastOnce, now, metadata: metadata, partition: partition);
            var actor = await actors.GetAgentAsync(new(request.WorkflowExecutionId, WorkflowExecutionActorActivationReason.ControlPlaneCommand,
                now, "runtime-efficiency-qualification", WorkflowExecutionActorCapabilities.None, metadata, partition), ct);
            var receipts = await Task.WhenAll(actor.EnqueueAsync(envelope, ct).AsTask(), actor.EnqueueAsync(envelope, ct).AsTask());
            Emit(root, "dispatch.json", new { receipts });
            foreach (var receipt in receipts)
                // A command for an execution owned by another host is forwarded through the durable transport and reports Deferred.
                if (receipt.Status is not (WorkflowExecutionCommandDispatchStatus.Accepted or WorkflowExecutionCommandDispatchStatus.Duplicate or WorkflowExecutionCommandDispatchStatus.Deferred))
                    throw new InvalidOperationException("A duplicate remote cancellation was not accepted, forwarded or recognized.");
            var settled = false;
            while (!settled)
            {
                await using var read = await factory.CreateAsync(new PersistenceScope(partition.Value), ct);
                var provider = read.ServiceProvider;
                var final = await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(request.WorkflowExecutionId, ct);
                var queue = await provider.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListAllAsync(request.WorkflowExecutionId, ct);
                var incidents = await provider.GetRequiredService<IIncidentStateStore>().CountAsync(request.WorkflowExecutionId, ct);
                var outbox = await provider.GetRequiredService<IPostCommitOutboxLookupStore>().FindUndeliveredDurableAsync(request.WorkflowExecutionId, [], ct);
                settled = final?.Status == WorkflowExecutionStatus.Cancelled && queue.Count == 0 && incidents == 0 && outbox is null;
                if (settled) Emit(root, "settled.json", new { final, queue = queue.Count, incidents, undeliveredOutbox = false });
                else await Task.Delay(100, ct);
            }
            Emit(root, "finished.json", new { status = "passed", request, sender = local.Identity.HostId, timingEligible = false });
        }
        catch (Exception error) { Emit(root, "finished.json", new { status = "failed", errorType = error.GetType().FullName, timingEligible = false }); }
    }
    private sealed record CancelRequest(string WorkflowExecutionId, string ArtifactId, string OwnerHostId, string CommandKey);
}
