using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;

// The finalization tables' schema family (spec 180, FR-001 extension; spec 181, FR-001 and FR-002): shared mapping
// code every EF module's context calls into, MapSchemaFinalization, with no single owning module. The two-argument
// form declares exactly that: EfSchemaFamilyCatalog reads Module as null, since this assembly declares no [EfModule]
// of its own. A host's readability report (spec 183, FR-020) is derived from it alone;
// EfSchemaFamilyDeclarationGuardTests fails the build when a family the stores check is not declared here, or is
// declared at another version.
[assembly: EfSchemaFamily(EfSchemaFinalization.SchemaFamily, EfSchemaFinalization.SchemaVersion)]

// The family's content columns (spec 180, FR-009 and FR-014): the documents a read upcasts through the family's chain
// before it deserializes them, and a write that changes them restamps. Every document column of the family's tables is
// declared here; EfSchemaContentDeclarationTests fails the build when one is not, or when one declared is not in the
// model, and EfSchemaFamilyDeclarationGuardTests holds every read and write of them to the chain and the stamp.
[assembly: EfSchemaContent(EfSchemaFinalization.SchemaFamily, typeof(EfSchemaFinalizationRecordRow),
    nameof(EfSchemaFinalizationRecordRow.IntentJson), nameof(EfSchemaFinalizationRecordRow.HoldsJson), nameof(EfSchemaFinalizationRecordRow.HistoryJson),
    nameof(EfSchemaFinalizationRecordRow.FinishJson), nameof(EfSchemaFinalizationRecordRow.FinishHistoryJson))]
