namespace Kernel.Durable;

/// <summary>
/// K1b: a host's node heartbeat and its pass gate (ownership and fencing, "The node's heartbeat" and "Liveness follows
/// the Worker's passes"). The host sends it on its own cadence; this decides whether it may, sends it, and hands the
/// answer to each Worker with the monotonic time just before it was sent. The hand-over renews each Worker's guard at once,
/// off the host's turn; each Worker's next pass runs the answer's stand-down side (<see cref="Worker.HeartbeatAnswered"/>),
/// and the host gives a pass to a Worker whose answer waits (<see cref="Worker.AnswerWaiting"/>).
/// <para>
/// The gate: the host renews only while each of its Workers that holds a partition, pending ones included, has completed
/// a pass within the last lease (<see cref="Worker.Live"/>). A withheld heartbeat closes every take of the host's Workers
/// (<see cref="Worker.TakesOpen"/>): gains, retakes after a reset and activations. Any exception from the heartbeat is a
/// missed renewal: nothing is handed over. A host with several Workers divides the answer: each judges the partitions it
/// holds, and the release side skips the partitions another Worker of the host claims.
/// </para>
/// </summary>
public sealed class NodeHeartbeat
{
    private readonly string _node;
    private readonly IKernelStore _store;
    private readonly IReadOnlyList<Worker> _workers;
    private readonly TimeSpan _lease, _window;

    public NodeHeartbeat(string node, IKernelStore store, IReadOnlyList<Worker> workers, TimeSpan lease, TimeSpan window)
    {
        (_node, _store, _workers, _lease, _window) = (node, store, workers, lease, window);
        foreach (var worker in workers) worker.TakesOpen = () => Open;
    }

    /// <summary>The pass gate: every Worker of the host that holds a partition has completed a pass within the last lease.</summary>
    public bool Open => _workers.All(w => w.Live);

    public int Withheld { get; private set; }
    public int Missed { get; private set; }

    /// <summary>
    /// One heartbeat, if the gate is open. Null when it was withheld or failed; otherwise the answer, handed over. It may run
    /// beside the Workers' passes: it renews, and leaves the stand-down, release and retake to each Worker's next pass.
    /// </summary>
    public async Task<NodeBeat?> BeatAsync(CancellationToken cancellationToken = default)
    {
        if (!Open)
        {
            Withheld++;
            return null;
        }
        var sent = _workers.Select(w => w.BeforeHeartbeat()).ToList();
        NodeBeat answer;
        try { answer = await _store.NodeHeartbeatAsync(_node, _lease, _window, cancellationToken); }
        catch (Exception)
        {
            Missed++;       // a heartbeat that fails renews nothing, whatever the engine's error (a deadlock victim rolled back whole)
            return null;
        }
        for (var i = 0; i < _workers.Count; i++)
        {
            var self = _workers[i];
            self.HeartbeatAnswered(sent[i], answer, partition => _workers.Any(w => w != self && w.Claims(partition)));
        }
        return answer;
    }
}
