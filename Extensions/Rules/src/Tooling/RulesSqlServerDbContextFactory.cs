using Elsa.EntityFrameworkCore.Tooling;
using Elsa.Rules.Persistence.EntityFrameworkCore;

namespace Elsa.Rules.Tooling;

/// <summary>Design-time factory for the Rules-owned SqlServer migration set.</summary>
internal sealed class RulesSqlServerDbContextFactory() : ModuleDesignTimeFactory<RulesSqlServerDbContext>(RulesEfModule.HistoryTableName);
