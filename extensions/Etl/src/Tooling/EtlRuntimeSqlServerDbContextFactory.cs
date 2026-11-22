using Elsa.EntityFrameworkCore.Tooling;
using Elsa.Etl.Persistence.EntityFrameworkCore;

namespace Elsa.Etl.Tooling;

/// <summary>Design-time factory for the ETL runtime store's SqlServer migration set.</summary>
internal sealed class EtlRuntimeSqlServerDbContextFactory() : ModuleDesignTimeFactory<EtlRuntimeSqlServerDbContext>(EtlRuntimeEfModule.HistoryTableName);
