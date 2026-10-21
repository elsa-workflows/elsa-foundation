using Elsa.Persistence.EntityFramework;
using Elsa.Rules.Persistence.EntityFrameworkCore;
using Elsa.Rules.Persistence.EntityFrameworkCore.Entities;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Elsa.Rules.Tests")]

[assembly: EfModule(
    Elsa.Rules.Persistence.EntityFrameworkCore.RulesEfModule.Name,
    typeof(Elsa.Rules.Persistence.EntityFrameworkCore.RulesDbContext),
    HistoryModule = Elsa.Rules.Persistence.EntityFrameworkCore.RulesEfModule.HistoryModuleName,
    Sqlite = typeof(Elsa.Rules.Persistence.EntityFrameworkCore.RulesSqliteDbContext),
    DisplayName = "Rules runtime",
    DefaultConnectionName = Elsa.Rules.Persistence.EntityFrameworkCore.RulesEfModule.DefaultConnectionName,
    DefaultSqliteConnectionString = Elsa.Rules.Persistence.EntityFrameworkCore.RulesEfModule.DefaultSqliteConnectionString)]
[assembly: ManifestExtension("efModules", "RulesRuntime")]
[assembly: EfSchemaFamily(
    Elsa.Rules.Persistence.EntityFrameworkCore.RulesEfModule.SchemaFamily,
    "RulesRuntime",
    Elsa.Rules.Persistence.EntityFrameworkCore.RulesEfModule.SchemaVersion)]
[assembly: EfSchemaContent(
    Elsa.Rules.Persistence.EntityFrameworkCore.RulesEfModule.SchemaFamily,
    typeof(RuleRunRecord),
    nameof(RuleRunRecord.InvocationJson), nameof(RuleRunRecord.FactsJson),
    nameof(RuleRunRecord.TerminalJson), nameof(RuleRunRecord.DeliveryPayloadJson),
    nameof(RuleRunRecord.DeliveryAcknowledgementJson))]
[assembly: EfSchemaContent(
    Elsa.Rules.Persistence.EntityFrameworkCore.RulesEfModule.SchemaFamily,
    typeof(RuleRoundReceiptRecord),
    nameof(RuleRoundReceiptRecord.ReceiptJson))]
