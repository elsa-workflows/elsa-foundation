using Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.DbContext;
using Elsa.Persistence.EFCore.Sqlite;
using JetBrains.Annotations;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.Sqlite;

[UsedImplicitly]
public sealed class OpenTelemetryDbContextFactory : SqliteDesignTimeDbContextFactory<OpenTelemetryDbContext>;
