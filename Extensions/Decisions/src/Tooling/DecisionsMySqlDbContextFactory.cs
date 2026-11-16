using Elsa.EntityFrameworkCore.Tooling;
using Elsa.Decisions.Persistence.EntityFrameworkCore;

namespace Elsa.Decisions.Tooling;

/// <summary>Design-time factory for the Decisions-owned MySql migration set.</summary>
internal sealed class DecisionsMySqlDbContextFactory() : ModuleDesignTimeFactory<DecisionsMySqlDbContext>(DecisionsEfModule.HistoryTableName);
