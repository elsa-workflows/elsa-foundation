namespace Kernel.Bpmn.Run;

/// <summary>One node's owner of one partition, with the BPMN model.</summary>
public sealed class Owner
{
    public Owner(string node, int partition, IKernelStore store, ProcessRegistry registry, Func<long>? monotonic = null, BpmnOptions? options = null)
    {
        Model = new BpmnModel(registry, options);
        Worker = new Worker(node, partition, store, new WorkflowInstance([Model]), new WorkerOptions { Monotonic = monotonic ?? (static () => Environment.TickCount64) });
        Host = new DurableHost([Worker]);
    }

    public Worker Worker { get; }
    public DurableHost Host { get; }
    public BpmnModel Model { get; }

    public async Task StartAsync()
    {
        await Host.ActivateAsync(Worker.Partition);
        await Worker.PollAsync();   // caught up: from here on it hands over in memory
    }

    public InstanceStatus? Status(string id) => Worker.Instances.FirstOrDefault(i => i.Id == id).Row?.Status;

    public BpmnState? State(string id) => Worker.Instances.FirstOrDefault(i => i.Id == id) is { State: { } json } ? Json.Read<BpmnState>(json)
        : Worker.Instances.FirstOrDefault(i => i.Id == id).Row is { } row ? Json.Read<BpmnState>(row.State) : null;

    /// <summary>
    /// Run what is ready until <paramref name="until"/> holds and nothing is busy. Polls once at the start when asked, and
    /// whenever idle unless <paramref name="keepPolling"/> is off (the cost measurement counts every poll).
    /// </summary>
    public async Task PumpAsync(Func<bool> until, bool poll = false, int timeoutMs = 10_000, bool keepPolling = true)
    {
        if (poll) await Worker.WokenAsync(null);
        var deadline = Environment.TickCount64 + timeoutMs;
        while (true)
        {
            var busy = await Worker.RunAsync() == TimeSpan.Zero;     // one pass
            if (until() && !busy) return;
            if (Environment.TickCount64 > deadline)
                throw new TimeoutException("the pump did not reach its condition; in memory:\n  " + string.Join("\n  ",
                    Worker.Instances.Select(i => $"{i.Id} {i.Row?.Status} r{i.Row?.Revision} busy={i.Busy} {i.State ?? i.Row?.State}")));
            if (!busy)
            {
                await Task.Delay(1);
                if (keepPolling) await Worker.WokenAsync(null);    // the next pass reads the inbox
            }
        }
    }
}
