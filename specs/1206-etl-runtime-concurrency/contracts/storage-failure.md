# Runtime store failure contract

The existing `IEtlRuntimeStore`, `IEtlConnectorRuntimeStore` and `IEtlConnectorRuntimeInspector` operations may raise a public sealed `EtlStoreException` in `Elsa.Etl.Core.Runtime` when their storage boundary fails.

- `Operation` names the requested public operation.
- `Scope` and `RunId` retain the known input identity, or null when the operation spans scopes/runs.
- `InnerException` retains the original terminal failure, including provider wrappers.
- The exception message contains no raw connection, path, credential or record value.
- Exhaustion and nonretryable infrastructure failures share this shape; neither returns a fabricated replay/conflict/lease-loss outcome.
- Cancellation, established validation/corruption handling and actionable schema-write refusals remain distinct.
- Caller-owned, ambient and enlisted transactions are unsupported and refused before tracking or database mutation; no store-level retry is performed inside or outside that caller transaction.

This adds no store operation, persistence shape, error payload or HTTP status. API fallback remains the existing safe `etl.operation-failed` response. The design-store boundary is separate (#3758).
