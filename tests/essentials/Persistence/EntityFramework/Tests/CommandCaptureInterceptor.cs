using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// Records the text of every command EF executes through the contexts it is added to: readers, non-queries and
/// scalars, synchronous and asynchronous. A command is recorded as it starts, so one that then fails still counts.
/// Each EF test project that counts or inspects commands compiles this file in.
/// </summary>
internal sealed class CommandCaptureInterceptor : DbCommandInterceptor
{
    private readonly ConcurrentQueue<string> commands = new();

    /// <summary>The recorded command texts, in execution order.</summary>
    public IReadOnlyCollection<string> Commands => commands.ToArray();

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result) =>
        Record(command, result);

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Record(command, result));

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result) =>
        Record(command, result);

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Record(command, result));

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result) =>
        Record(command, result);

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Record(command, result));

    private T Record<T>(DbCommand command, T result)
    {
        commands.Enqueue(command.CommandText);
        return result;
    }
}
