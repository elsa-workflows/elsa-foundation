using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Groundwork.Kernel;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Resolves one explicitly scoped v2 runtime session for the current persistence operation.</summary>
internal sealed class GroundworkV2RuntimeLivenessContext(
    IGroundworkStorageSessionSource sessions,
    IPersistenceAccessContextAccessor accessContextAccessor,
    string? targetName)
{
    private readonly StorageUnit unit = sessions.Unit(ElsaRuntimeV2StorageManifest.ExecutionLivenessStateDocumentKind, targetName);

    public GroundworkRuntimeRowStore Open()
    {
        var context = accessContextAccessor.Current ?? throw new InvalidOperationException("Runtime persistence access context is missing.");
        if (context.Scope is null || context.AcrossScopes)
        {
            throw new InvalidOperationException(
                "Groundwork runtime liveness requires one explicit persistence scope; global and across-scope access are refused.");
        }

        var access = StorageAccess.Scoped(new StorageScope(context.Scope.Value));
        return new GroundworkRuntimeRowStore(sessions.Open(unit.Id.Value, access, targetName));
    }

    public StorageUnit Unit => unit;
}
