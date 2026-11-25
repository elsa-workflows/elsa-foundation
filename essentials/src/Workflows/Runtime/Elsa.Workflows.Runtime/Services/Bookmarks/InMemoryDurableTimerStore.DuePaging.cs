using System.Globalization;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Bookmarks;

public sealed partial class InMemoryDurableTimerStore
{
    public ValueTask<RuntimeStorePage<DurableTimer>> ListDuePageAsync(DateTimeOffset asOf, RuntimeStorePageRequest page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page); cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            var binding = RuntimeHistoryIdentity.CreateBinding([Scope, asOf.UtcTicks.ToString(CultureInfo.InvariantCulture), page.Limit.ToString(CultureInfo.InvariantCulture)]);
            DuePageCursor? cursor = null;
            if (page.ContinuationToken is { } token)
            {
                try
                {
                    cursor = JsonSerializer.Deserialize<DuePageCursor>(Convert.FromBase64String(token), options: Elsa.Serialization.Core.JsonBaseline.Options) ?? throw new FormatException();
                    if (cursor.Binding != binding || cursor.Due < 0 || cursor.Due > asOf.UtcTicks ||
                        cursor.Key is null || cursor.Key.Length != 64 || cursor.Key.Any(c => !(c is >= '0' and <= '9' or >= 'a' and <= 'f')))
                        throw new FormatException();
                }
                catch (Exception error) when (error is JsonException or FormatException or ArgumentException)
                { throw new ArgumentException("The due-timer continuation does not belong to this scope and query.", nameof(page), error); }
            }
            // Opaque identities are not length-bounded by the memory store. A framed, exact UTF16
            // digest gives ties a stable provider-owned order without embedding those identities in a token.
            var source = _timers.Values.Where(state => !IsRetired(state)).Select(state => state.Timer)
                .Where(timer => timer.DueTime <= asOf)
                .Select(timer => (Timer: timer, Key: RuntimeHistoryIdentity.CreateBinding([timer.WorkflowExecutionId, timer.TimerId])));
            if (cursor is not null)
                source = source.Where(item => item.Timer.DueTime.UtcTicks > cursor.Due || item.Timer.DueTime.UtcTicks == cursor.Due &&
                    StringComparer.Ordinal.Compare(item.Key, cursor.Key) > 0);
            var rows = source.OrderBy(item => item.Timer.DueTime).ThenBy(item => item.Key, StringComparer.Ordinal)
                .Take(page.Limit + 1).ToArray();
            var more = rows.Length > page.Limit;
            if (more) rows = rows[..page.Limit];
            var next = more ? Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new DuePageCursor(binding,
                rows[^1].Timer.DueTime.UtcTicks, rows[^1].Key), options: Elsa.Serialization.Core.JsonBaseline.Options)) : null;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new RuntimeStorePage<DurableTimer>(page, rows.Select(item => item.Timer).ToArray(), next));
        }
    }

    private sealed record DuePageCursor(string Binding, long Due, string Key);
}
