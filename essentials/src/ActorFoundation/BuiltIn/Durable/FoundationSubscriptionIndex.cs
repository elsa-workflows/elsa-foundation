namespace Kernel.Durable;

internal readonly record struct SubscriptionPosition(string Instance, string Wait) : IComparable<SubscriptionPosition>
{
    public int CompareTo(SubscriptionPosition other)
    {
        var instance = StringComparer.Ordinal.Compare(Instance, other.Instance);
        return instance != 0 ? instance : StringComparer.Ordinal.Compare(Wait, other.Wait);
    }
}

public sealed partial class InMemoryStore
{
    private readonly Dictionary<string, Dictionary<string, SortedList<SubscriptionPosition, SubscriptionRow>>> _foundationWaits =
        new(StringComparer.Ordinal);

    private void AddRegisteredSubscription(SubscriptionRow subscription)
    {
        if (!_subscriptions.Add(subscription) || subscription.FoundationTenant is not { } tenant) return;
        if (!_foundationWaits.TryGetValue(tenant, out var keys))
            _foundationWaits.Add(tenant, keys = new(_foundationEventEquality));
        if (!keys.TryGetValue(subscription.Key, out var waits))
            keys.Add(subscription.Key, waits = new());
        waits.Add(new(subscription.Instance, subscription.Wait), subscription);
    }

    private void RemoveRegisteredSubscription(SubscriptionRow subscription)
    {
        if (!_subscriptions.Remove(subscription)) return;
        RemoveCatchUpCoverage(subscription);
        if (subscription.FoundationTenant is not { } tenant) return;
        var keys = _foundationWaits[tenant];
        var waits = keys[subscription.Key];
        waits.Remove(new(subscription.Instance, subscription.Wait));
        if (waits.Count == 0) keys.Remove(subscription.Key);
        if (keys.Count == 0) _foundationWaits.Remove(tenant);
    }

    private IEnumerable<SubscriptionRow> FoundationWaits(string tenant, string key, SubscriptionPosition? after = null)
    {
        if (!_foundationWaits.TryGetValue(tenant, out var keys) || !keys.TryGetValue(key, out var waits)) yield break;
        var first = 0;
        if (after is { } cursor)
        {
            var end = waits.Count;
            while (first < end)
            {
                var middle = first + (end - first) / 2;
                if (waits.Keys[middle].CompareTo(cursor) <= 0) first = middle + 1;
                else end = middle;
            }
        }
        for (var index = first; index < waits.Count; index++) yield return waits.Values[index];
    }
}
