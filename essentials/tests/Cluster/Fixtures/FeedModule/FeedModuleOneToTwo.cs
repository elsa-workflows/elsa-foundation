using Elsa.Persistence.EntityFramework;

namespace Elsa.Cluster.Fixtures.FeedModule;

/// <summary>The family's one step, from 1 to 2: its rows are the same at both versions.</summary>
[EfSchemaUpcaster(FeedModule.PreviousVersion, FeedModule.CurrentVersion)]
public sealed class FeedModuleOneToTwo : IEfSchemaUpcaster
{
    public EfSchemaRowContent Upcast(EfSchemaRowContent row) => row;
}
