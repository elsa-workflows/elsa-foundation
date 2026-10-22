# Test fixture records and lifecycle

No production entity, schema or external API changes are planned.

| Record | Fields/invariants |
|---|---|
| Hosting profile | In-process or built-in clustered; declared scenario applicability. No workflow semantic settings. |
| Scenario | Unique owned root, immutable package/version and artifact reference; shared database/journal/lock paths; distinct clustered roles. |
| Host observation | Role, OS PID, configured identity and content root; stop/resume only the owned process. |
| Body observation | Process ID, invocation ID, attempt ID, key and unique event identity. Physical retries remain distinct from logical completion. |
| Held commit | Original execution/commit IDs, expected lease/owner/token and participant IDs; original commit/token remain in the suspended call. No accepted marker on capture or delegation. |
| Ownership observation | Actual successor lease, token and expiry returned by production acquire; it must remain live during the resumed commit. |
| Fence outcome | Exact exception/reason/presented/current token, cancellation state on delegation, marker and participant observations before/after. |

Lifecycle: `original captured → original suspended → successor acquired/held → original resumed → real EF rejection → survivor released → native completed`. Each transition needs an observed event or durable state, not elapsed time alone. Failure records both logs and observations and releases/terminates only owned processes.

Journal barriers coordinate tests; they are never runtime authority. Persisted state decides acceptance and completion. Diagnostic writes are atomic and per-process/event where writers overlap. Losing diagnostics is a failed proof, not successful evidence.
