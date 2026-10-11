using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;

// The finalization tables' schema family (spec 180, FR-001 extension; spec 181, FR-001 and FR-002): shared mapping
// code every EF module's context calls into, MapSchemaFinalization, with no single owning module. The two-argument
// form declares exactly that: EfSchemaFamilyCatalog reads Module as null, since this assembly declares no [EfModule]
// of its own. A host's readability report (spec 183, FR-020) is derived from it alone; every family the stores check
// must be declared here, at the version they read; that holds by review until #2606 restores a compiled check.
[assembly: EfSchemaFamily(EfSchemaFinalization.SchemaFamily, EfSchemaFinalization.SchemaVersion)]

// Content and integrity columns: see Essentials/src/Persistence/EntityFramework/EXTENSION_POINTS.md, "Content and integrity columns" (spec 180, FR-008, FR-009
// and FR-014).
[assembly: EfSchemaContent(EfSchemaFinalization.SchemaFamily, typeof(EfSchemaFinalizationRecordRow),
    nameof(EfSchemaFinalizationRecordRow.IntentJson), nameof(EfSchemaFinalizationRecordRow.HoldsJson), nameof(EfSchemaFinalizationRecordRow.HistoryJson),
    nameof(EfSchemaFinalizationRecordRow.FinishJson), nameof(EfSchemaFinalizationRecordRow.FinishHistoryJson))]
