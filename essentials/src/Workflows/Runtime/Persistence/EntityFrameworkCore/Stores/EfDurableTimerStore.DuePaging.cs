using System.Globalization;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeOperationalStoreSupport;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

public sealed partial class EfDurableTimerStore
{
    private const string DuePagePurpose = "ef-runtime-timer-due-v1";

    public ValueTask<RuntimeStorePage<DurableTimer>> ListDuePageAsync(DateTimeOffset asOf, RuntimeStorePageRequest page,
        CancellationToken cancellationToken = default) =>
        HistoryBoundary(null, "list-due-timer-page", () => ListDuePageCoreAsync(asOf, page, cancellationToken), cancellationToken);

    private async ValueTask<RuntimeStorePage<DurableTimer>> ListDuePageCoreAsync(DateTimeOffset asOf, RuntimeStorePageRequest page,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        var captured = accessContextAccessor.Current; CheckHistoryAccess(captured, cancellationToken);
        if (!EfTimerHistoryOwner.HasReadableTimerModel(context)) throw SourceUnavailable("Due traversal requires the actual unfiltered timer model.");
        var scope = captured.RequireScope().Value; var scopeKey = Encode(scope); var scopeHash = Hash(scope);
        var binding = RuntimeHistoryIdentity.CreateBinding([scope, asOf.UtcTicks.ToString(CultureInfo.InvariantCulture), page.Limit.ToString(CultureInfo.InvariantCulture)]);
        var source = context.DurableTimers.AsNoTracking().Where(row => row.ScopeKeyHash == scopeHash && row.ScopeKey == scopeKey &&
            !row.HistoryRetired && row.DueTimeUtcTicks <= asOf.UtcTicks);
        if (page.ContinuationToken is { } token)
        {
            DuePageCursor cursor;
            try
            {
                cursor = JsonSerializer.Deserialize<DuePageCursor>(continuationCodec.Decode(DuePagePurpose, token)) ?? throw new FormatException();
                if (cursor.Binding != binding || cursor.Due < 0 || cursor.Due > asOf.UtcTicks ||
                    string.IsNullOrWhiteSpace(cursor.TimerOrder) || string.IsNullOrWhiteSpace(cursor.RunOrder)) throw new FormatException();
            }
            catch (Exception error) when (error is JsonException or FormatException or ArgumentException or InvalidOperationException)
            { throw new ArgumentException("The due-timer continuation does not belong to this scope and query.", nameof(page), error); }
            source = source.Where(row => row.DueTimeUtcTicks > cursor.Due || row.DueTimeUtcTicks == cursor.Due &&
                (string.Compare(row.TimerIdOrderKey, cursor.TimerOrder) > 0 || row.TimerIdOrderKey == cursor.TimerOrder &&
                    string.Compare(row.WorkflowExecutionIdOrderKey, cursor.RunOrder) > 0));
        }
        var rows = await source.OrderBy(row => row.DueTimeUtcTicks).ThenBy(row => row.TimerIdOrderKey)
            .ThenBy(row => row.WorkflowExecutionIdOrderKey).Take(page.Limit + 1).ToArrayAsync(cancellationToken);
        CheckHistoryAccess(captured, cancellationToken);
        var more = rows.Length > page.Limit;
        if (more) rows = rows[..page.Limit];
        var items = rows.Select(row => ReadChecked(row, scope)).ToArray();
        var next = more ? continuationCodec.Encode(DuePagePurpose, JsonSerializer.SerializeToUtf8Bytes(new DuePageCursor(binding,
            rows[^1].DueTimeUtcTicks, rows[^1].TimerIdOrderKey, rows[^1].WorkflowExecutionIdOrderKey))) : null;
        CheckHistoryAccess(captured, cancellationToken); return new(page, items, next);
    }

    private sealed record DuePageCursor(string Binding, long Due, string TimerOrder, string RunOrder);
}
