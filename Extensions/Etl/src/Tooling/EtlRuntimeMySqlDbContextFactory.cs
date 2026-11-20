using Elsa.EntityFrameworkCore.Tooling;
using Elsa.Etl.Persistence.EntityFrameworkCore;

namespace Elsa.Etl.Tooling;

/// <summary>Design-time factory for the ETL runtime store's MySql migration set.</summary>
internal sealed class EtlRuntimeMySqlDbContextFactory() : ModuleDesignTimeFactory<EtlRuntimeMySqlDbContext>(EtlRuntimeEfModule.HistoryTableName);
