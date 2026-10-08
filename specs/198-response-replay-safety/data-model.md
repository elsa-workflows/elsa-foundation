# Data Model: Response Replay Safety

This work adds no durable model or database schema. These existing records and test observations must remain distinct.

| Record or state | Relevant values | Lifetime and proof use |
|---|---|---|
| Immutable published External fixture | pre-candidate-external-closure.json plus manifest with baseline source revision, design/version IDs, artifact ID/version/hash, resolved profile, and fixture-byte SHA-256 | Captured once by the normal publisher/export path before candidate classification. The test loads the same bytes without editing profile or content. Routine CI does not build historical Git revisions. |
| Candidate published executable | Candidate artifact ID/version/hash and resolved activity profile | Published through the normal candidate API/compiler in the test-owned database. The same authored graph has the selected pinned-profile change; enumerate and exclude only identity/version/hash/publication-provenance differences from the behavioral comparison. |
| Durable request seed and activity claim | Execution ID, reserved stimulus/trigger durable-value IDs and original request correlation, HttpEndpoint node ID, activity execution ID, claim/work-item IDs, pinned artifact identity | External HttpEndpoint claim is the durable request boundary. Startup persists request and trigger seeds; it does not require a resume-path TriggerDelivery record. The parent independently reads these records and necessary recovery work before continuing the child. |
| Durable recovery work and execution ownership | Exact InvokeActivity work item and payload; execution lease ID, owner, positive fencing token and expiry | The selected Coalesced fixture retains a ready durable invoke backstop with null item claim/visibility timestamps and token zero. Its in-memory dispatch does not require a persisted per-item claim. The separate execution ownership lease identifies the live child and fences writes. |
| Invocation input snapshot | For each of StatusCode, Body, ContentType, and Headers: current literal/object/default forms and every currently accepted request, variable, causally prior ActivityResult, and canonical pure-expression binding; stable external reference only where supported | Coverage is generated from the current input definitions/compiler contract. For every field/source pair, record accepted behavior or the current unsupported/refused shape. Do not presume every binding family is valid for every field or add hypothetical provider cases. |
| Coalescing session overlay | Execution ID, active segment identity, buffered completion, hop/buffer state | Ephemeral child state. The child attests it over IPC after the coalescing store returns; parent labels it as child-reported memory state. |
| Committed response instruction | Status, headers, content type, body | Durable after flush. Compare value equality after replay. Assert header dictionary and array values at the serialized/projected ActivityCompletion boundary; mutate originals only after that boundary. Do not infer deep-copy semantics at ExecuteAsync return. |
| Terminal workflow output | Deterministic Alice Smith result and terminal status | Durable result for the same execution and trigger/request. Ignore attempt IDs and timestamps in equivalence. |
| Bounded evidence window | Artifact/profile identity, execution ID, cold-cache/setup state, provider/settings, request/drain counts, background/resumption counts, DB command attempts | Test-only evidence. Capture through segment flush, delivery, and independently confirmed terminal/settled state. Exclude parent observer reads; report failed/repeated commands. |

## Complete input coverage matrix

| Input | Existing base/default behavior | Binding coverage | Additional assertion |
|---|---|---|---|
| StatusCode | Current literal/default conversion; nonpositive values resolve to HTTP 200 | Each source family accepted by the current StatusCode input definition; record unsupported type/source pairs and existing secret refusal | Published instruction and synchronous HTTP status agree |
| Body | Current literal string/object serialization and default | Each source family accepted by the current Body input definition, including request, variable, causally prior activity result, and pure expression when supported | Serialized response body remains equivalent after replay |
| ContentType | Current literal/default behavior | Each source family accepted by the current ContentType input definition; preserve current refusal behavior | Published instruction and synchronous response content type agree |
| Headers | Current literal/object/default behavior | Each source family accepted by the current Headers input definition; preserve current refusal behavior | After completion projection/serialization, mutate original dictionary and its arrays; committed/delivered serialized snapshot does not change |

For each row, cover literal/object/default values as applicable; request value, variable, prior activity result, and canonical pure expression only where the current contract accepts them. Retain existing secret-binding refusals. Exercise external payload-reference replay only for a supported binding/provider that actually emits such a reference. Document unsupported shapes as unsupported rather than as failed proof. The full input contract is not proven by the current execution/live-write tests alone.

## Crash-window evidence

Expected sequence:

    request accepted
      → HttpEndpoint trigger/claim durably committed
      → deterministic SetVariable completed in an active Coalesced segment
      → WriteHttpResponse completion buffered and absent from durable state
      → child process killed without graceful disposal
      → existing execution-owner lease expires in this bounded stale-owner scenario
      → normal resumption discovers and replays durable work
      → same execution commits equivalent response instruction and terminal output

The child attests over IPC its correlation IDs, active session identity, IsActive/AppliesTo/HasBufferedChanges/HopCount, matching buffered activity completion, and value digest. The parent independently queries its own SQLite context for exact durable trigger/request/claim identity, recovery work/lease, and absence of that response completion. These are separate evidence sources. If either side is missing, the test has not reached the required window.

The response barrier has no release acknowledgement: the child stays suspended until the parent's OS kill. A null scheduler visibility deadline makes the durable backstop discoverable immediately. Waiting for the persisted execution-owner lease is a deliberate stale-owner test condition, not a claim that backlog discovery waits for that lease or that the work item has its own persisted visibility lease.

## Invariants

- HttpEndpoint remains External and Immediate remains the default.
- Existing mandatory checkpoint boundaries continue to flush.
- A persisted artifact uses its pinned profile; candidate publication resolves the candidate declaration.
- The test waits for the configured existing execution-owner lease to expire before restarting normal resumption; no new lease or durability policy is introduced.
- A lost client connection is not recovered or promised exactly-once transport delivery.
- No user option, production runtime hook, durable record, or persistence policy is added.
