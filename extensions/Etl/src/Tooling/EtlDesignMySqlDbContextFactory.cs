using Elsa.EntityFrameworkCore.Tooling;
using Elsa.Etl.Persistence.EntityFrameworkCore;

namespace Elsa.Etl.Tooling;

/// <summary>Design-time factory for the ETL design store's MySql migration set.</summary>
internal sealed class EtlDesignMySqlDbContextFactory() : ModuleDesignTimeFactory<EtlDesignMySqlDbContext>(EtlDesignEfModule.HistoryTableName);
