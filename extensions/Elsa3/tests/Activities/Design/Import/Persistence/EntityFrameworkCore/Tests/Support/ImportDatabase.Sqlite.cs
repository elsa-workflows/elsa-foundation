using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;

namespace Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Tests.Support;

/// <summary>
/// The SQLite factory for <see cref="ImportDatabase"/>. It sits in its own file because the provider suite links
/// <c>ImportDatabase.cs</c> and that file therefore has to stay provider-neutral.
/// </summary>
internal sealed partial class ImportDatabase
{
    /// <summary>
    /// Opens the three SQLite contexts over one connection string. The result owns the contexts and not the file:
    /// disposing it closes the contexts and leaves the database where it is. Use <see cref="SqliteImportHarness"/>
    /// when the temporary file should be deleted as well.
    /// </summary>
    public static ImportDatabase Sqlite(string connectionString) => new(
        interceptors => new Elsa3ImportSqliteDbContext(SqliteImportHarness.Options<Elsa3ImportSqliteDbContext>(connectionString, interceptors)),
        interceptors => new ActivitiesDesignSqliteDbContext(SqliteImportHarness.Options<ActivitiesDesignSqliteDbContext>(connectionString, interceptors)),
        interceptors => new WorkflowsDesignSqliteDbContext(SqliteImportHarness.Options<WorkflowsDesignSqliteDbContext>(connectionString, interceptors)));
}
