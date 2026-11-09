# Model

Start selection: direct, or optionId + opaque modeId + module JSON configuration. Workflow inputs are a separate map.
Option identity: compiled artifact hash plus provider/driver, executable node, routing descriptor and canonical metadata. Mode: stable ID, label, default, configuration version/schema, safe readonly context.
Prepared target: existing TestRun scope ID/expiry/tenant/partition, pinned artifact/source reference, exact binding, reserved execution ID and idempotency key, normalized inputs/default variables and driver-owned resource state. Process-local bounded projection; restart invalidates target.
Driver action: opaque kind, API-relative URL/method and optional safe details. Module Studio contribution owns transport and response rendering. Prepared has no execution; actual dispatch associates the reserved execution.
Stop closes scope, releases driver resources and requests ordinary cancellation. Expiry rejects dispatch independently of sweep timing. Captured run evidence is never rewritten by setup changes.
