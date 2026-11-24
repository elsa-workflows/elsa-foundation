namespace Kernel.Durable;

/// <summary>The owner's ready queues. A pass offers each ready actor at most once before flushing.</summary>
internal sealed class PassTurnSchedule(IReadOnlyDictionary<string, int> weights)
{
    internal readonly record struct ReadyTurn(string Id, string Tenant, long Observed);

    private sealed class TenantTurns(string tenant, int weight)
    {
        public string Tenant { get; } = tenant;
        public int Weight { get; } = weight;
        public int Credit { get; set; } = weight;
        public int Available { get; set; }
        public Queue<string> Actors { get; } = new();
    }

    private readonly LinkedList<TenantTurns> _round = new();
    private readonly Dictionary<string, LinkedListNode<TenantTurns>> _tenants = new(StringComparer.Ordinal);
    private readonly Queue<LinkedListNode<TenantTurns>> _pass = new();

    internal int Refresh(IEnumerable<ReadyTurn> readyTurns, int budget)
    {
        var ready = readyTurns.ToDictionary(turn => turn.Id, StringComparer.Ordinal);
        for (var node = _round.First; node is not null;)
        {
            var next = node.Next;
            var tenant = node.Value;
            var count = tenant.Actors.Count;
            for (var i = 0; i < count; i++)
            {
                var actor = tenant.Actors.Dequeue();
                if (ready.TryGetValue(actor, out var turn) && turn.Tenant == tenant.Tenant)
                {
                    ready.Remove(actor);
                    tenant.Actors.Enqueue(actor);
                }
            }
            if (tenant.Actors.Count == 0)
            {
                _round.Remove(node);
                _tenants.Remove(tenant.Tenant);
            }
            node = next;
        }

        foreach (var turn in ready.Values.OrderBy(turn => turn.Observed))
        {
            if (!_tenants.TryGetValue(turn.Tenant, out var node))
            {
                node = _round.AddLast(new TenantTurns(turn.Tenant, weights.GetValueOrDefault(turn.Tenant, 1)));
                _tenants.Add(turn.Tenant, node);
            }
            node.Value.Actors.Enqueue(turn.Id);
        }

        _pass.Clear();
        var available = 0;
        for (var node = _round.First; node is not null; node = node.Next)
        {
            node.Value.Available = node.Value.Actors.Count;
            available += node.Value.Available;
            _pass.Enqueue(node);
        }
        return Math.Min(budget, available);
    }

    internal string? Next()
    {
        if (!_pass.TryPeek(out var node) || node.Value.Available == 0) return null;
        var tenant = node.Value;
        var actor = tenant.Actors.Dequeue();
        tenant.Actors.Enqueue(actor);
        tenant.Available--;
        if (--tenant.Credit == 0)
        {
            tenant.Credit = tenant.Weight;
            _round.Remove(node);
            _round.AddLast(node);
            _pass.Dequeue();
            if (tenant.Available > 0) _pass.Enqueue(node);
        }
        // If the head uses all this pass's offers before its credit runs out, the caller flushes.
        // The next pass can give it the rest of its credit if any actors are still ready.
        return actor;
    }

    internal void Clear()
    {
        _pass.Clear();
        _round.Clear();
        _tenants.Clear();
    }
}
