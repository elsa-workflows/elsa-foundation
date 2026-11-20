using Elsa.EntityFrameworkCore.Tooling;
using Elsa.Decisions.Persistence.EntityFrameworkCore;

namespace Elsa.Decisions.Tooling;

/// <summary>Design-time factory for the Decisions-owned SqlServer migration set.</summary>
internal sealed class DecisionsSqlServerDbContextFactory() : ModuleDesignTimeFactory<DecisionsSqlServerDbContext>(DecisionsEfModule.HistoryTableName);
