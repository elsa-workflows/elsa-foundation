using Acme.Widgets;
using Elsa.Persistence.EntityFramework;

// A third-party module declares itself exactly the way a first-party one does, with a name outside the
// 13-name vocabulary and no Elsa change of any kind. MySQL and SQLite are
// deliberately left undeclared: a null provider property means that provider is unsupported for this
// module, which must produce a named refusal rather than a null-reference failure.
[assembly: EfModule(
    "Acme.Widgets",
    typeof(WidgetsDbContext),
    HistoryModule = "AcmeWidgets",
    SqlServer = typeof(WidgetsSqlServerDbContext),
    PostgreSql = typeof(WidgetsPostgreSqlDbContext),
    DisplayName = "Acme Widgets")]
