namespace Elsa.Persistence.Schema;

/// <summary>
/// What an EF module's refusal to activate says of itself, in types both a host and a feed-loaded module load from
/// this shared assembly (ADR 0067, amended 2026-09-29). A package Nuplane loads carries a private copy of
/// <c>Elsa.Persistence.EntityFramework</c>, so a host that references none cannot name that copy's exception types, and
/// matching them by full name would break with a rename. It asks for this interface instead.
/// </summary>
/// <remarks>
/// Implemented by the exceptions that stop a module's shell from activating for a reason an operator resolves outside the
/// host: pending migrations, a contracting migration that may not be applied yet, and the finalization gate's refusal.
/// None of the members carries a connection string or any other restored secret (spec 171, FR-061).
/// </remarks>
public interface IEfModuleRefusal
{
    /// <summary><see cref="Code"/> for a module whose database has migrations that are not applied.</summary>
    const string PendingMigrationsCode = "pending-migrations";

    /// <summary><see cref="Code"/> for a pending batch withheld by a contracting migration that may not be applied yet.</summary>
    const string ContractingMigrationRefusedCode = "contracting-migration-refused";

    /// <summary><see cref="Code"/> for a module the finalization gate refuses to activate.</summary>
    const string SchemaActivationRefusedCode = "schema-activation-refused";

    /// <summary>
    /// What a <see cref="Command"/> prints for <c>--host</c> when its author does not know the host's directory. A host that
    /// does know it replaces exactly this text with the directory, quoted the same way.
    /// </summary>
    const string HostPlaceholder = "\"<host directory>\"";

    /// <summary>The EF module that was refused, as its <c>[EfModule]</c> names it.</summary>
    string Module { get; }

    /// <summary>A stable machine-readable code: one of the <c>*Code</c> constants of this interface.</summary>
    string Code { get; }

    /// <summary>The ids of the pending migrations the refusal concerns; empty when it concerns none.</summary>
    IReadOnlyList<string> PendingMigrations { get; }

    /// <summary>The exact command that resolves the refusal, when there is one an operator can run as it stands.</summary>
    string? Command { get; }
}
