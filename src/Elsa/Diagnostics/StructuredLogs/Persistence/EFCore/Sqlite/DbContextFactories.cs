using Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.DbContext;
using Elsa.Persistence.EFCore.Sqlite;
using JetBrains.Annotations;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.Sqlite;

[UsedImplicitly]
public sealed class StructuredLogsDbContextFactory : SqliteDesignTimeDbContextFactory<StructuredLogsDbContext>;
