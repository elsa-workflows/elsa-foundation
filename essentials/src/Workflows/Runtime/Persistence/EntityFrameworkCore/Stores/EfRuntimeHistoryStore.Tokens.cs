using System.Globalization;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfRuntimeHistoryStore
{
    private const string EventPurpose = "runtime-history-events-ef-v1";
    private const string BookmarkPurpose = "runtime-history-bookmarks-ef-v1";
    private const int MaximumScan = 1000;
    private const int MaximumParentDepth = 256;
    private const int MaximumAncestryRows = 2048;
    private sealed record Cursor(string Binding, long After, long Upper, bool CatchUp, string? Policy);
    private static string Binding(params object?[] fields) => RuntimeHistoryIdentity.CreateBinding(fields.Select(
        field => field is IFormattable value ? value.ToString(null, CultureInfo.InvariantCulture) : field?.ToString()).ToArray());
    private static string? Policy(RuntimeHistoryHead? head) => head is null ? null : Binding(
        new object?[] { head.Policy.PolicyId, head.Policy.Version, head.Policy.Required }.Concat(head.Policy.SupportedKinds).ToArray());
    private string Encode(string purpose, Cursor value) => codec.Encode(purpose, JsonSerializer.SerializeToUtf8Bytes(value));
    private Cursor? Decode(string purpose, string? value, string binding)
    {
        if (value is null) return null;
        try
        {
            var cursor = JsonSerializer.Deserialize<Cursor>(codec.Decode(purpose, value));
            if (cursor is null || cursor.Binding != binding || cursor.After < 0 || cursor.Upper < cursor.After || cursor.CatchUp && cursor.After != cursor.Upper)
                throw new FormatException();
            return cursor;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or FormatException or InvalidOperationException)
        { throw new ArgumentException("The history continuation is invalid or belongs to another query or authorization context.", nameof(value), error); }
    }
    private static void ValidateWindow(string run, RuntimeHistoryHead? head, Cursor? cursor, long after, long upper)
    {
        EfRuntimeHistoryProjectionMapping.ValidateHead(run, head);
        if (after < 0 || upper < after || upper > (head?.LastSequence ?? 0) || cursor is not null && cursor.Policy != Policy(head) && !(cursor.CatchUp && cursor.Policy is null))
            throw new ArgumentException("The history continuation no longer identifies an available range.", "continuationToken");
    }
}
