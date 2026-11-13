using System.Data.Common;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Converts physical provider failures at the standalone resource and reservation ports.</summary>
internal static class EfTriggerBoundary
{
    internal static async ValueTask<T> RunAsync<T>(Func<ValueTask<T>> operation, string? scope, string? identity)
    {
        try { return await operation(); }
        catch (Exception exception) when (exception is IOException or JsonException or DbException or DbUpdateException)
        {
            var corrupt = exception is InvalidDataException or JsonException;
            throw new TriggerOperationException(corrupt ? "conflict" : "unavailable",
                corrupt ? "Stored trigger authority is invalid; reload and inspect the resource." : "Trigger persistence is unavailable.",
                new Dictionary<string, string> { ["scope"] = scope ?? "", ["identity"] = identity ?? "" }, exception);
        }
    }
}
