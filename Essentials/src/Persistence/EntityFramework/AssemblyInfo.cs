using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;

// The finalization tables' schema family: shared mapping
// code every EF module's context calls into, MapSchemaFinalization, with no single owning module. The two-argument
// form declares exactly that: EfSchemaFamilyCatalog reads Module as null, since this assembly declares no [EfModule]
// of its own. A host's readability report is derived from it alone; every family the stores check
// must be declared here, at the version they read; that holds by review until #2606 restores a compiled check.
[assembly: EfSchemaFamily(EfSchemaFinalization.SchemaFamily, EfSchemaFinalization.SchemaVersion)]

// Content and integrity columns: see Essentials/src/Persistence/EntityFramework/EXTENSION_POINTS.md, "Content and integrity columns".
[assembly: EfSchemaContent(EfSchemaFinalization.SchemaFamily, typeof(EfSchemaFinalizationRecordRow),
    nameof(EfSchemaFinalizationRecordRow.IntentJson), nameof(EfSchemaFinalizationRecordRow.HoldsJson), nameof(EfSchemaFinalizationRecordRow.HistoryJson),
    nameof(EfSchemaFinalizationRecordRow.FinishJson), nameof(EfSchemaFinalizationRecordRow.FinishHistoryJson))]

[assembly: EfSchemaFamily(Elsa.Persistence.EntityFramework.EfSourceModel.SchemaFamily, Elsa.Persistence.EntityFramework.EfSourceModel.SchemaVersion)]

[assembly: EfSchemaContent(Elsa.Persistence.EntityFramework.EfSourceModel.SchemaFamily, typeof(Elsa.Persistence.Core.SourceWorkingCopy), nameof(Elsa.Persistence.Core.SourceWorkingCopy.Content))]
[assembly: EfSchemaContent(Elsa.Persistence.EntityFramework.EfSourceModel.SchemaFamily, typeof(Elsa.Persistence.Core.SourceSnapshot), nameof(Elsa.Persistence.Core.SourceSnapshot.Content))]
