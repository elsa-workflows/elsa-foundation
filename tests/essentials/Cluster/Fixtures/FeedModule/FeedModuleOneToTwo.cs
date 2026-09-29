using Elsa.Persistence.EntityFramework;

namespace Elsa.Cluster.Fixtures.FeedModule;

/// <summary>The family's one step, from 1 to 2: its content is the same at both versions.</summary>
[EfSchemaUpcaster(FeedModule.PreviousVersion, FeedModule.CurrentVersion)]
public sealed class FeedModuleOneToTwo : IEfSchemaUpcaster
{
    public string Upcast(EfSchemaContent content) => content.Value;
}
