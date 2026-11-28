using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Identifies the exact EF history reader that can inspect sources from the same physical owner.</summary>
internal interface IEfRuntimeHistorySourceOwner
{
    bool UsesPhysicalHistory(RuntimeDbContext context, IPersistenceAccessContextAccessor access);
}
