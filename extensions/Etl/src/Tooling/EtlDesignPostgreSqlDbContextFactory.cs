using Elsa.EntityFrameworkCore.Tooling;
using Elsa.Etl.Persistence.EntityFrameworkCore;

namespace Elsa.Etl.Tooling;

/// <summary>Design-time factory for the ETL design store's PostgreSql migration set.</summary>
internal sealed class EtlDesignPostgreSqlDbContextFactory() : ModuleDesignTimeFactory<EtlDesignPostgreSqlDbContext>(EtlDesignEfModule.HistoryTableName);
