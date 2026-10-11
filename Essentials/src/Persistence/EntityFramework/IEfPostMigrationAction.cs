using Microsoft.EntityFrameworkCore;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// A named, audited, never-auto-run unit of work a module declares for after its migrations apply (ADR 0076
/// D8, FR-054): the data step a schema migration cannot express, such as rewriting rows a changed projection
/// algorithm left stale.
/// </summary>
/// <remarks>
/// <para>
/// Takes only the <see cref="DbContext"/> and uses no dependency injection, because the <c>dotnet elsa
/// persistence</c> worker has no shell container to resolve anything from. An implementation must therefore
/// be a concrete type with a public parameterless constructor, and is declared by <see cref="Type"/> on its
/// module's <see cref="EfModuleAttribute.PostMigration"/>; <see cref="EfPostMigrationActions.Create"/> is
/// the one place that turns such a declaration into an instance.
/// </para>
/// <para>
/// <see cref="AuditAsync"/> must never mutate. It is called after every apply, under both
/// <see cref="EfMigratePolicy"/> values, and by <c>apply</c> and <c>validate</c> — none of which may rewrite
/// data as a side effect. <see cref="RunAsync"/> is reached from exactly one place, <c>dotnet elsa
/// persistence post-migrate</c>, so a bounded-batch data rewrite stays an operator's decision.
/// </para>
/// </remarks>
public interface IEfPostMigrationAction
{
    /// <summary>The stable identifier the manifest, the audit refusal and <c>post-migrate</c>'s report all name this action by.</summary>
    string Id { get; }

    /// <summary>What kind of work this is — <c>projection-reindex</c>, say — as <c>migration-plan.json</c>'s <c>kind</c> records it (FR-048).</summary>
    string Kind { get; }

    /// <summary>The condition that makes this action required, as the manifest's <c>requiredWhen</c> records it.</summary>
    string RequiredWhen { get; }

    /// <summary>The audit entry point this action calls, as the manifest's <c>audit</c> records it, so a DBA can read what the check actually is.</summary>
    string Audit { get; }

    /// <summary>
    /// Reports whether this action still has work to do against <paramref name="context"/>. Read-only: it must
    /// leave every row exactly as it found it. A database it cannot read throws rather than answering
    /// <c>false</c> — an audit that reported "nothing required" because it failed would be indistinguishable
    /// from a healthy one.
    /// </summary>
    Task<bool> AuditAsync(DbContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Does the work. Only <c>dotnet elsa persistence post-migrate</c> calls this; nothing runs it as a side
    /// effect of applying migrations or of starting a host.
    /// </summary>
    Task RunAsync(DbContext context, CancellationToken cancellationToken = default);
}
