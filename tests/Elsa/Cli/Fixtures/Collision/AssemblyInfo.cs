using Acme.Widgets.Collision;
using Elsa.Persistence.EntityFramework;

// Same name as Acme.Widgets in a different casing. Module names are matched case-insensitively, so two
// assemblies declaring this pair have no answer to "which one did you mean" — discovery refuses both
// rather than picking one (FR-022).
[assembly: EfModule(
    "acme.widgets",
    typeof(CollidingWidgetsDbContext),
    HistoryModule = "AcmeWidgetsCollision",
    PostgreSql = typeof(CollidingWidgetsPostgreSqlDbContext))]
