# Source and decision record

Baseline: main `b555bd8460270476e49496e519c69b65180e9505`, 2026-10-21. Root plus two Luna Extra High read-only audits. `git diff c641c223..b555bd846 -- Extensions/Etl` is empty. This is source/design evidence; no new HTTP behavior has been executed.

| Evidence | Decision and rejected alternative |
|---|---|
| `Core/EtlPipeline.cs`, `EtlFileBindings.cs`, `EtlArtifactJson.cs`; compiler/runtime reader/native pin source | Preserve strict v1; use explicit v2. Adding optional fields to the old canonical serializer can change published hashes. |
| `Core/Runtime/IEtlRuntimeStore.cs`, EF `CommitPreparationAsync` and `AcceptBatchAsync` | HTTP page/item positions and effects are explicit. Reject numeric sentinel ranges and whole-source preparation. Share the common fenced transitions, not file assumptions. |
| `Runtime/.../Preparation/EtlFilePreparer.cs`, `Execution/EtlInputBatchBuilder.cs` | File snapshot/index is not an HTTP cursor. Retain bounded pages per independently seeded shard. |
| `Output/FileChunkStore.cs`, `Execution/EtlBatchProcessor.cs` | Immutable object publication is reusable for retained pages/bodies; remote acknowledgement needs its own receipt. Never pretend a POST is an atomic file chunk. |
| `Transformation/EtlRowTransformer.cs` | Share typed evaluation below CSV parsing; add JSON input decoding and optional lookup for v2. Do not round-trip JSON through CSV strings. |
| `Essentials/src/Connections/Core/Contracts/IConnectionResolver.cs`, `IHttpRequestAuthenticator.cs`; default resolver/authenticators | Reuse these public ports. Endpoint and non-secret config pins are distinct from point-of-use secrets; rotation allowed, configuration drift/revocation diagnosed. |
| `Connections/Workflows/Services/HttpConnectionApplicator.cs` | Activity-context-bound, so not reused by ETL workers. ETL has its existing ETL publication permission and alias check and worker-safe composition of the public contracts. |
| `Activities/Http/Activities/SendHttpRequest.cs` | Single-request activity buffers responses and treats general 2xx as Done. Not an ETL retry/acknowledgement contract; 202 deliberately does not satisfy this slice. |
| Existing native composite/persisted publication tests and live ETL Studio journey | Reuse genuine parent publication/continuation and authoring seams. New source/root-only tests cannot substitute for composite proof. |

## Resolved scope choices

JSON Pointers for record-array/fields; finite token-query or JSON next-link pagination; host revision-pinned shard seeds; per-partition bounded retained page; CSV/HTTP input and JSONL/HTTP output combinations; optional CSV lookup. Preserve existing file v1. Synchronous POST acknowledgement 200/201/204; durable retry for documented transient cases and network uncertainty; stable request bytes/key with honest receiver-dependent deduplication. No API-wide snapshot guarantee is invented. The full connector/continuous/BPMN parent remains open.

## External primary references

- [RFC 9110, HTTP semantics](https://www.rfc-editor.org/rfc/rfc9110.html), especially 9.2.2 and 10.2.3: idempotency/retry distinction and Retry-After date/delay forms. A client-generated key alone is not a receiver guarantee.
- [Microsoft, IHttpClientFactory common issues](https://learn.microsoft.com/en-us/dotnet/core/extensions/httpclient-factory-troubleshooting): handler scopes differ from application scopes; avoid caching tenant state/cookies in shared handlers. Resolve auth per request.

Both consulted 2026-10-21. Product protocol and supported profile are recorded in the spec, not inferred from framework defaults.

## Readiness and remaining gates

No owner decision is required for the bounded profile under standing design authority. Three bounded review passes checked the dynamic source position and compatibility transitions; the final focused pass has no remaining concrete blocker. #3109's integrated source is present; CI39529263265 attempt 1 hit its 45-minute job limit; completed summaries reported 17,737 passed/32 skipped/zero failed, but Workbench and downstream gates did not complete. Another session requested attempt 2, still running. Its post-merge gate remains pending, not passed. BPMN protected-review waits and Orleans do not block this native/HTTP work. Spec allocation includes archived/current paths and all open PR files: main highest994, PR3150 reserves995, this issue reserves996.

## Design review corrections

The first source audit read the pre-main Connections permission shape. A fresh read at b555 shows `IConnectionPermissionGate` deliberately has no use right: workflow binding is authorized by workflow-design manage permission. ETL follows its existing publish permission plus host-approved scoped aliases; it does not invent a connection-use permission. The credential resolver/authenticator contracts remain reusable.

The correctness reviewer found fully filtered nonempty batches were omitted from the position-advancement prose. They now explicitly settle exact input/count/quota with zero effects and no HTTP send. A second review identified per-attempt timeout and transport diagnostic proof as separate from retry budgets and safe Studio views; both are explicit in the plan/contract. The Connections masking catalog needs a scoped clarification for non-workflow consumers, not a new dependency on a workflow mask implementation. No code behavior is claimed from these document corrections.

Second review clarified anonymous versus authenticated policy, null-tenant authentication refusal, exact approved paths for next links and a real pre-change in-flight database migration/recovery fixture. It also corrected an overbroad telemetry promise: .NET platform activities retain origin/path and belong to host configuration; ETL can guarantee only its owned logs/errors/spans. References: [built-in activities](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-builtin-activities) and [URI redaction](https://learn.microsoft.com/en-us/dotnet/core/compatibility/networking/9.0/query-redaction-logs), consulted 2026-10-21. No global tracing changes are authorized by the connector profile.
