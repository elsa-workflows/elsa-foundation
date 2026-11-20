using Elsa.EntityFrameworkCore.Tooling;
using Elsa.Decisions.Persistence.EntityFrameworkCore;

namespace Elsa.Decisions.Tooling;

/// <summary>Design-time context factory for the Decisions-owned SQLite migration set.</summary>
internal sealed class DecisionsSqliteDbContextFactory : ModuleDesignTimeFactory<DecisionsSqliteDbContext>
{
    public DecisionsSqliteDbContextFactory() : base(DecisionsEfModule.HistoryTableName)
    {
    }
}
