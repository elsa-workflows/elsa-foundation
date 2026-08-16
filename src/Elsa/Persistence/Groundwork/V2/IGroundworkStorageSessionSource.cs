using Groundwork.Kernel;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Composition;

/// <summary>
/// Elsa's v2 session boundary. Feature adapters name a declared unit and explicit Groundwork access;
/// provider selection, schema admission, and connection lifetime stay in composition.
/// </summary>
public interface IGroundworkStorageSessionSource
{
    IStorageSession Open(
        string unitId,
        StorageAccess access,
        string? targetName = null);

    IUnitOfWork BeginUnitOfWork(
        StorageAccess access,
        BatchWriteOptions options,
        IReadOnlyList<string> unitIds,
        string? targetName = null);

    StorageUnit Unit(string unitId, string? targetName = null);
}
