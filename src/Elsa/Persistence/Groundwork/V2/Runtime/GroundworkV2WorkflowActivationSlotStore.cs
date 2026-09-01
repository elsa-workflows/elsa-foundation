using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Persistence.Groundwork.Composition;
using Groundwork.Kernel;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Runtime;

public sealed class GroundworkV2WorkflowActivationSlotStore
{
    private readonly IGroundworkStorageSessionSource sessions;
    private readonly string? targetName;
    private readonly StorageUnit unit;

    public GroundworkV2WorkflowActivationSlotStore(IGroundworkStorageSessionSource sessions, string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        this.sessions = sessions;
        this.targetName = targetName;
        unit = sessions.Unit(ElsaRuntimeV2StorageManifest.WorkflowActivationSlotDocumentKind, targetName);
    }

    public StoredEntry? Read(string slotId, StorageAccess access) => Open(access).Read(GroundworkRuntimeRowStore.Key(slotId));
    public WriteOutcome Insert(WorkflowActivationSlot slot, StorageAccess access) => Open(access).Insert(GroundworkV2WorkflowActivationSlotStorageConventions.Values(slot), WriteOptions.CreateOnly);
    public WriteOutcome ConditionalUpsert(WorkflowActivationSlot slot, long expectedVersion, StorageAccess access) =>
        Open(access) is IConcurrencyStorageSession concurrency
            ? concurrency.ConditionalUpsert(GroundworkV2WorkflowActivationSlotStorageConventions.Values(slot), WriteOptions.IfVersion(expectedVersion))
            : throw new NotSupportedException("The selected Groundwork provider does not advertise optimistic activation-slot concurrency.");
    public IStorageSession Open(StorageAccess access) => sessions.Open(unit.Id.Value, access, targetName);
    public StorageUnit Unit => unit;
}
