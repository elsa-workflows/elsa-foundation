using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

internal sealed class BeforeExecutableCoordinationRead(Func<Task> beforeRead) : DbCommandInterceptor
{
    private int callbacks;

    public int Callbacks => Volatile.Read(ref callbacks);
    public string? TriggeredCommand { get; private set; }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains(
                RuntimeArtifactEfModule.WorkflowExecutableCoordinationTableName,
                StringComparison.OrdinalIgnoreCase) &&
            Interlocked.CompareExchange(ref callbacks, 1, 0) == 0)
        {
            TriggeredCommand = command.CommandText;
            await beforeRead();
        }

        return result;
    }
}
