using Microsoft.EntityFrameworkCore;

namespace Elsa.Persistence.EntityFramework.SchemaBackfill;

/// <summary>What one unit of the backfill's work runs with: a fresh service scope of the shell, and a fresh context of the module.</summary>
public sealed record EfSchemaBackfillScope(IServiceProvider Services, DbContext Context);

/// <summary>
/// Runs <paramref name="action"/> once, in a fresh service scope of the shell with a fresh context of the module (spec 186,
/// FR-004 and FR-009): what the module's migrator gives the backfill, and what a test gives it in its place. A runner that
/// cannot open such a scope throws; it never returns without running the action.
/// </summary>
public delegate Task EfSchemaBackfillScopeRunner(Func<EfSchemaBackfillScope, Task> action, CancellationToken cancellationToken);

internal static class EfSchemaBackfillScopes
{
    /// <summary>
    /// Runs <paramref name="action"/> in a fresh scope and returns what it returned. A runner that returned without running
    /// it is refused: a count or a page it never read would otherwise read as "nothing found", and a verification pass built
    /// on that would record a completion no pass proved.
    /// </summary>
    public static async Task<T> WithScopeAsync<T>(this EfSchemaBackfillScopeRunner runner, Func<EfSchemaBackfillScope, Task<T>> action, CancellationToken cancellationToken)
    {
        var ran = false;
        T result = default!;
        await runner(async scope =>
        {
            result = await action(scope);
            ran = true;
        }, cancellationToken);
        return ran
            ? result
            : throw new InvalidOperationException("The backfill's scope runner returned without running its work, so what that work would have found is unknown.");
    }

    /// <inheritdoc cref="WithScopeAsync{T}"/>
    public static Task WithScopeAsync(this EfSchemaBackfillScopeRunner runner, Func<EfSchemaBackfillScope, Task> action, CancellationToken cancellationToken) =>
        runner.WithScopeAsync(async scope =>
        {
            await action(scope);
            return true;
        }, cancellationToken);
}
