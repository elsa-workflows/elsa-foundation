using Elsa.EntityFrameworkCore.Tooling;
using Elsa.Rules.Persistence.EntityFrameworkCore;

namespace Elsa.Rules.Tooling;

/// <summary>Design-time factory for the Rules-owned PostgreSql migration set.</summary>
internal sealed class RulesPostgreSqlDbContextFactory() : ModuleDesignTimeFactory<RulesPostgreSqlDbContext>(RulesEfModule.HistoryTableName);
