using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Publishing.Releases.Persistence.EntityFrameworkCore;

// Shared consumer mapping has no built-in module or migration/startup participant.
[assembly: EfSchemaFamily(ReleaseArchiveModelBuilderExtensions.SchemaFamily, ReleaseArchiveModelBuilderExtensions.SchemaVersion,
    Entities = new[] { typeof(ReleaseArchiveRow), typeof(ReleaseArchiveEntryRow) })]
