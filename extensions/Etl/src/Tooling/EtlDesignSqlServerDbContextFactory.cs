using Elsa.EntityFrameworkCore.Tooling;
using Elsa.Etl.Persistence.EntityFrameworkCore;

namespace Elsa.Etl.Tooling;

/// <summary>Design-time factory for the ETL design store's SqlServer migration set.</summary>
internal sealed class EtlDesignSqlServerDbContextFactory() : ModuleDesignTimeFactory<EtlDesignSqlServerDbContext>(EtlDesignEfModule.HistoryTableName);
