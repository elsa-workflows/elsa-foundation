using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Update;
using Microsoft.EntityFrameworkCore.Update.Internal;
using MySql.EntityFrameworkCore.Infrastructure.Internal;

namespace Kernel.EntityFramework;

#pragma warning disable EF1001 // EF Core 10.0.15 and MySql.EntityFrameworkCore 10.0.12; see ENGINES.md.
internal sealed class MySqlCommandBatchPreparer(CommandBatchPreparerDependencies dependencies)
    : CommandBatchPreparer(dependencies)
{
    internal static void Configure(DbContextOptionsBuilder optionsBuilder)
    {
        if (optionsBuilder.Options.FindExtension<MySQLOptionsExtension>() is not null)
            optionsBuilder.ReplaceService<ICommandBatchPreparer, MySqlCommandBatchPreparer>();
    }

    // Oracle flushes pending bulk inserts inside TryAddCommand. When a subsequent command takes it over
    // its parameter limit, rollback can discard previously accepted inserts along with the last command.
    // Finish the group before that command. EF still enforces the configured command count and store transaction.
    public override IReadOnlyList<List<IReadOnlyModificationCommand>> TopologicalSort(IEnumerable<IReadOnlyModificationCommand> commands)
    {
        const int maximumParameters = 2098; // Oracle's IsValid tests ParameterValues.Count + 1 < 2100.
        var sets = new List<List<IReadOnlyModificationCommand>>();
        foreach (var ordered in base.TopologicalSort(commands))
        {
            var start = 0;
            var parameters = 0;
            for (var i = 0; i < ordered.Count; i++)
            {
                var columns = ordered[i].ColumnModifications;
                var next = columns.Count(c => c.UseOriginalValueParameter) + columns.Count(c => c.UseCurrentValueParameter);
                // A homogeneous run of inserts has no internal IsValid failure: parameters are added
                // only when Oracle completes that run. Keep it intact for E7's large flushes.
                if (i > start && parameters + next > maximumParameters && !SameInsertGroup(ordered[i - 1], ordered[i]))
                {
                    sets.Add(ordered.GetRange(start, i - start));
                    start = i;
                    parameters = 0;
                }
                parameters += next;
            }
            if (start == 0) sets.Add(ordered);
            else sets.Add(ordered.GetRange(start, ordered.Count - start));
        }

        return sets;
    }

    private static bool SameInsertGroup(IReadOnlyModificationCommand previous, IReadOnlyModificationCommand next) =>
        previous.EntityState == EntityState.Added && next.EntityState == EntityState.Added
        && previous.Table == next.Table
        && previous.ColumnModifications.Select(c => (c.Column, c.IsWrite, c.IsRead))
            .SequenceEqual(next.ColumnModifications.Select(c => (c.Column, c.IsWrite, c.IsRead)));
}
#pragma warning restore EF1001
