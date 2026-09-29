using Elsa.Persistence.Schema;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// <see cref="EfDatabaseMigrator"/>'s own fail-closed signal under <see cref="EfMigratePolicy.Validate"/>:
/// the database was reached and read successfully, and it has pending migrations. Kept as its own type —
/// rather than a plain <see cref="InvalidOperationException"/> — so a caller can tell this negative result
/// apart from a genuine connectivity, credential or schema failure that <c>GetPendingMigrationsAsync</c>
/// can throw as an <see cref="InvalidOperationException"/> too, before this check is ever reached.
/// </summary>
/// <remarks>
/// <para>
/// Not sealed for one reason: <see cref="SchemaFinalization.EfContractingMigrationRefusedException"/> is a pending batch
/// that also may not be applied yet (spec 185, FR-024), so every caller that already treats pending migrations as a
/// negative result rather than a database failure does the same for it without a change.
/// </para>
/// <para>
/// It is an <see cref="IEfModuleRefusal"/>, so a host that carries no copy of this assembly, such as
/// <c>Elsa.Foundation.Host</c> answering a shell reload, can name the module and the migrations without knowing this type.
/// </para>
/// </remarks>
public class EfPendingMigrationsException : InvalidOperationException, IEfModuleRefusal
{
    /// <summary>A refusal that names no module, migrations or command: only its <paramref name="message"/>.</summary>
    public EfPendingMigrationsException(string message)
        : this(string.Empty, [], message)
    {
    }

    public EfPendingMigrationsException(
        string module,
        IReadOnlyList<string> pending,
        string message,
        string? command = null) : base(message)
    {
        Module = module;
        PendingMigrations = pending;
        Command = command;
    }

    /// <summary>The EF module whose migrations are pending, or the name of the context that has them when no module declares it; empty when the refusal names none.</summary>
    public string Module { get; }

    /// <summary>The ids of the migrations that are pending; for a contracting refusal, every pending migration of the context it withheld, none of which was applied.</summary>
    public IReadOnlyList<string> PendingMigrations { get; }

    /// <summary>The exact command that applies the pending migrations, when the module and provider are known. Its <c>--host</c> is a placeholder the reader fills in, or a host that knows its directory replaces (<see cref="IEfModuleRefusal.HostPlaceholder"/>).</summary>
    public string? Command { get; }

    /// <summary>A stable machine-readable code for this refusal, one of the <c>*Code</c> constants of <see cref="IEfModuleRefusal"/>; a subclass that is a different refusal overrides it.</summary>
    public virtual string Code => IEfModuleRefusal.PendingMigrationsCode;
}
