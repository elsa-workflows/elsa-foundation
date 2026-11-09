using Elsa.EntityFrameworkCore.Tooling;
using Elsa.Rules.Persistence.EntityFrameworkCore;

namespace Elsa.Rules.Tooling;

/// <summary>Design-time factory for the Rules-owned MySql migration set.</summary>
internal sealed class RulesMySqlDbContextFactory() : ModuleDesignTimeFactory<RulesMySqlDbContext>(RulesEfModule.HistoryTableName);
