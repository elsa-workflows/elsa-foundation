# Checked legacy broadcast inventory

The normal entrypoint is the offline `-BroadcastPreflightDatabases` parameter set of
[e2e preflight](../../../e2e-tests/bpmn/Test-BpmnLibraryAdoption.ps1). It creates a coherent private
snapshot before invoking this command. Live sources include committed WAL content; frozen gzip
captures retain their original hashes. The command takes that private snapshot and its expected
SHA256, refuses live sidecars, opens read-only and reuses the real runtime outbox checked reader.
It performs no delivery, update or migration.

```sh
dotnet build tools/bpmn/DeliveryPreflight/DeliveryPreflight.csproj
python3 -m unittest discover -s e2e-tests/bpmn -p test_inspect_broadcast_delivery.py -v
```

The receipt classifies only checked legacy sends: Delivered is settled; all other statuses retain
responsibility. Invalid source/family/projection/state yields InventoryUnavailable. Attempt zero
never proves that no recipient effect occurred. Results cover only supplied databases; they do not
establish mixed-writer quiescence, production-wide inventory or broadcast recovery acceptance.

[T046 receipt](../../../specs/1006-bpmn-library-adoption/evidence/broadcast-preflight.md) records actual
stores, controls and remaining gates. The later Core contracts are recorded separately; provider/delivery/recovery T047–T051 remain open.
