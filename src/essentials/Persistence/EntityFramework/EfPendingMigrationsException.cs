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
public class EfPendingMigrationsException(
    string module,
    IReadOnlyList<string> pending,
    string message,
    string? command = null) : InvalidOperationException(message), IEfModuleRefusal
{
    /// <summary>The exact invocation that applies <paramref name="module"/>'s pending migrations on <paramref name="provider"/>.</summary>
    /// <remarks>The connection is named by the variable it is read from, never its value (spec 171, D7); the host's path is the operator's to fill in.</remarks>
    public static string CommandFor(string module, string provider) =>
        $"dotnet elsa persistence apply --host <path> --modules {module} --provider {provider} --connection-env ELSA_EF_CONNECTION";

    /// <summary>The EF module whose migrations are pending, or the name of the context that has them when no module declares it.</summary>
    public string Module { get; } = module;

    /// <summary>The ids of the migrations that are pending.</summary>
    public IReadOnlyList<string> Pending { get; } = pending;

    /// <summary>The exact command that applies the pending migrations, when the module and provider are known.</summary>
    public string? Command { get; } = command;

    public virtual string Code => IEfModuleRefusal.PendingMigrationsCode;

    IReadOnlyList<string> IEfModuleRefusal.PendingMigrations => Pending;
}
