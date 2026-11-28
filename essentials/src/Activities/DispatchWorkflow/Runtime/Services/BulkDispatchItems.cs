using System.Collections;
using System.Text.Json;

namespace Elsa.Activities.DispatchWorkflow.Runtime.Services;

/// <summary>
/// Indexed view over the <c>Items</c> input. A JSON array (the runtime materializes <c>object</c> inputs as JSON) gives
/// O(1) count and item access, so refilling the window does not materialize the collection again. Null is an empty
/// collection; a string or any other non-collection value is refused, as <c>ForEach</c> refuses it (spec 1242 edge cases).
/// Kept local for the same reason as <see cref="BulkDispatchIteration"/>.
/// </summary>
internal readonly struct BulkDispatchItems
{
    private readonly JsonElement _jsonArray;
    private readonly IReadOnlyList<object?>? _materialized;
    private readonly bool _isJson;

    private BulkDispatchItems(JsonElement jsonArray)
    {
        _jsonArray = jsonArray;
        _isJson = true;
    }

    private BulkDispatchItems(IReadOnlyList<object?> materialized) => _materialized = materialized;

    public int Count => _isJson ? _jsonArray.GetArrayLength() : _materialized?.Count ?? 0;

    public object? ItemAt(int index)
    {
        if (!_isJson)
            return _materialized![index];

        var item = _jsonArray[index];
        return item.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : item;
    }

    public static BulkDispatchItems Resolve(object? items) =>
        items switch
        {
            null => new(Array.Empty<object?>()),
            JsonElement element => element.ValueKind switch
            {
                JsonValueKind.Null or JsonValueKind.Undefined => new(Array.Empty<object?>()),
                JsonValueKind.Array => new(element),
                _ => throw new InvalidOperationException($"BulkDispatchWorkflows Items input JSON value kind '{element.ValueKind}' is not an array.")
            },
            string => throw new InvalidOperationException("BulkDispatchWorkflows Items input must be a collection, not a string."),
            IEnumerable enumerable => new(enumerable.Cast<object?>().ToArray()),
            _ => throw new InvalidOperationException($"BulkDispatchWorkflows Items input of type '{items.GetType().FullName}' is not a collection.")
        };
}
