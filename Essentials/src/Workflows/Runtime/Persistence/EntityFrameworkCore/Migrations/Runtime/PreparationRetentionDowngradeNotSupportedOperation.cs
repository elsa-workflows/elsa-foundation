using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Migrations.Runtime;

/// <summary>
/// Refuses automatic removal of permanent preparation roots and terminal replay history.
/// </summary>
/// <remarks>
/// Schema qualification materializes both migration directions during forward construction.
/// Keep construction safe; the supported providers refuse this unsupported operation when
/// downgrade commands are generated, before returning commands or deleting migration history.
/// Provider SQL-generation controls must continue to prove that refusal on dependency updates.
/// </remarks>
internal sealed class PreparationRetentionDowngradeNotSupportedOperation : MigrationOperation;
