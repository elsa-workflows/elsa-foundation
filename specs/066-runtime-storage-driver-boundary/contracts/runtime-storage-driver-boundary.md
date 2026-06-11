# Contract: Runtime Storage Driver Boundary

## Removed Contracts

- `Elsa.Workflows.Runtime.Core.Contracts.IStorageDriver`
- `Elsa.Workflows.Runtime.Core.Contracts.IStorageDriverContext`

These contracts are intentionally removed from Runtime.Core because they model generic object storage around expression variables. They do not encode durable value lifecycle, storage mode, source activity execution, checkpoint commit semantics, or pinned executable artifact identity.

## Replacement Boundary

Runtime durable values are represented by:

- `DurableValueState`
- `IDurableValueStateStore`
- checkpoint projections that write durable value state

Runtime input/output behavior may reference durable values through compiled runtime bindings and `RuntimeDurableValueReference`, but direct storage-driver reads and writes are not part of the runtime execution seam.

## Compatibility Note

Elsa 3 storage-driver metadata remains migration input evidence only. It does not define an Elsa 4 live runtime continuation contract.
