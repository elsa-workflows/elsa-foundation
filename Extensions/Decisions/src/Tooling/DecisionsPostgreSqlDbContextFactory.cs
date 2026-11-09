using Elsa.EntityFrameworkCore.Tooling;
using Elsa.Decisions.Persistence.EntityFrameworkCore;

namespace Elsa.Decisions.Tooling;

/// <summary>Design-time factory for the Decisions-owned PostgreSql migration set.</summary>
internal sealed class DecisionsPostgreSqlDbContextFactory() : ModuleDesignTimeFactory<DecisionsPostgreSqlDbContext>(DecisionsEfModule.HistoryTableName);
