using Elsa.EntityFrameworkCore.Tooling;
using Elsa.Rules.Persistence.EntityFrameworkCore;

namespace Elsa.Rules.Tooling;

/// <summary>Design-time context factory for the Rules-owned SQLite migration set.</summary>
internal sealed class RulesSqliteDbContextFactory : ModuleDesignTimeFactory<RulesSqliteDbContext>
{
    public RulesSqliteDbContextFactory() : base(RulesEfModule.HistoryTableName)
    {
    }
}
