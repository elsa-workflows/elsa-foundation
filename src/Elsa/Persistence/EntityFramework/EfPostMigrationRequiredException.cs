namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// The post-migration audit's own fail-closed signal: the migrations applied and the database was read
/// successfully, and at least one declared <see cref="IEfPostMigrationAction"/> reports itself required.
/// Kept as its own type — the same reason <see cref="EfPendingMigrationsException"/> is one — so a caller can
/// tell this negative result apart both from a pending migration and from a genuine connectivity, credential
/// or schema failure an audit can throw before it ever reaches a verdict.
/// </summary>
public sealed class EfPostMigrationRequiredException(string module, IReadOnlyList<string> actionIds, string command)
    : InvalidOperationException(
        $"Module '{module}' has post-migration action(s) that have not been run: {string.Join(", ", actionIds)}. " +
        "Migrations applied; this step rewrites data and is never run automatically. Run:\n" +
        $"  {command}")
{
    /// <summary>The canonical module name whose actions are outstanding.</summary>
    public string Module { get; } = module;

    /// <summary>Every <see cref="IEfPostMigrationAction.Id"/> the audit reported as required, in declaration order.</summary>
    public IReadOnlyList<string> ActionIds { get; } = actionIds;

    /// <summary>The exact <c>dotnet elsa persistence post-migrate</c> invocation that clears them.</summary>
    public string Command { get; } = command;
}
