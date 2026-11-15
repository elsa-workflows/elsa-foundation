using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal static class SourcePublicationWriteVersion
{
    internal static string For(RuntimeDbContext context, WorkflowExecutableSourceReference source, string? storedVersion) =>
        RuntimeSchemaWriteVersion.For(context, RuntimeArtifactEfModule.SchemaFamily,
            source.PublicationScope is null ? RuntimeArtifactEfModule.SchemaVersion : RuntimeArtifactEfModule.ScopedPublicationSchemaVersion, storedVersion);
}
