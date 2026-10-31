namespace Elsa.Workflows.Runtime.Services.Coalescing;

/// <summary>An ordered snapshot of one overlay generation, with allocation-free successor lookup.</summary>
internal sealed class RuntimeCoalescingOrderedView<T> where T : class
{
    private readonly (string Id, T Value)[] _items;

    public RuntimeCoalescingOrderedView(IEnumerable<T> values, Func<T, string> identity)
    {
        _items = values.Select(value => (Id: identity(value), Value: value)).ToArray();
        Array.Sort(_items, static (left, right) => StringComparer.Ordinal.Compare(left.Id, right.Id));
    }

    public T? Next(string? after)
    {
        var low = 0;
        var high = _items.Length;
        if (after is not null)
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (StringComparer.Ordinal.Compare(_items[middle].Id, after) <= 0)
                    low = middle + 1;
                else
                    high = middle;
            }

        return low < _items.Length ? _items[low].Value : null;
    }
}
