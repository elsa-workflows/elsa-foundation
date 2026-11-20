# Decisions data model

Proposed under [the plan](plan.md); production admission remains pending. Every stored key contains a trusted tenant scope. Direct supplied-artifact evaluation has no persistence/host-identity dependency.

| Entity | State and identity |
| --- | --- |
| Decision asset | Stable opaque tenant/asset ID, display name, sensitivity policy, current draft/version references, revision and UTC timestamps. Name is not identity. |
| Decision draft | Asset ID, revision token, original UTF-8 XML, source SHA-256 and validation eligibility. Every write supplies expected revision. |
| Decision version | Immutable version ID/publication number, original XML/hash, canonical executable bytes/hash, profile/semantics IDs, schema, sensitivity and UTC publication instant. |
| Decision artifact | Frozen Core value for direct callers, with decision ID, optional provenance, canonical executable/hash and profile/semantics IDs. Collections/bytes are copied defensively. Untrusted provenance cannot claim a server publication. |
| Decision case | Tenant/asset/case ID, name, revision, typed inputs, expected status/result, sensitivity and UTC timestamps. |
| Decision test run | Completed receipt: operation ID, trusted actor, exact candidate/case identities and hashes, safe outcomes/comparisons, UTC evaluation/completion instants and optional protected history payload. No durable pending test job. |
| Compatibility report | Located diagnostics plus separate preservation, editing, execution and publication eligibility. Retained/viewable does not imply executable. |

Evaluation returns exact artifact identity, status, typed result, actual matched/selected rule IDs, cell-test truth states, safe located diagnostics and consumed operations. NoMatch differs from Success with null. Cancellation/budget exhaustion never expose partial successful output.

Expected-result comparison uses typed structural equality, ordinal object keys, ordered lists and exact numeric equality ignoring insignificant decimal zeroes. No implicit number/string conversion or numeric tolerance. COLLECT interoperability additionally compares multisets as stated in the profile.

## Invariants and transitions

- Create asset atomically creates one draft.
- Draft/case writes perform CAS inside the authoritative write, never read-then-unconditional-write. Commands return confirmation/status/revision; independent queries return snapshots.
- Publish validates a frozen expected draft, then atomically appends a version and updates the selected-version pointer only if that revision is still current. A race produces conflict, not an orphan version or stale pointer.
- Publication operation IDs are tenant/asset/actor scoped. Identical retries return the same version confirmation; divergent reuse conflicts.
- Exact reads never fall back to another tenant/version/latest/draft.
- Tests are bounded synchronous calls. After authorization, first look up an existing completed operation receipt and verify its request fingerprint before loading mutable cases. Exact replay returns that receipt; divergent reuse conflicts.
- For a new operation, require explicit candidate/case revisions and hashes, copy those snapshots in memory and evaluate. Atomically insert one completed receipt under tenant/actor/operation key plus fingerprint. Pure concurrent duplicate computation is harmless; one authoritative receipt wins. A race involving different request content conflicts.
- An interruption before receipt commit leaves no durably accepted test job. It is explicitly unrecorded, not a successful or recoverable pending run. Retry may evaluate only the same expected revisions/content; if edits/deletion made them unavailable, return conflict. Never substitute current content or claim an earlier computation completed.
- Receipt commit retains identities/hashes/safe summary; optional raw-history retention controls whether value-bearing candidate/case/comparison payloads are stored. Detailed authorized comparisons may be returned transiently in the completion response. Summary-only replay explicitly reports detailed payload unavailable and never reconstructs deleted values through reevaluation.
- Deleting/archiving a design asset cannot change the frozen executable already embedded in a published workflow. New selection/publication policy may restrict reuse.
- Infrastructure failures are wrapped at domain boundaries with localizing IDs; raw EF/XML errors and sensitive payload fragments do not escape.

## Visibility and retention

Saved cases intentionally retain their declared values with explicit access protection. In-flight test snapshots exist only in the bounded request. Completed receipts retain identities and safe summaries by default; storing raw inputs, expected/actual values or source snapshots is opt-in host policy and requires sensitive-value access. Delete-case/delete-run remove retained payloads under policy. No pending-test coordinator, admission ledger, recovery worker or cleanup scheduler is introduced. If later product requirements need first-class durable decision/test executions, use the existing shared model-hosting authority rather than creating a second runtime in this store.

Original XML and canonical executable have distinct hashes: formatting/diagram changes may alter source identity without changing evaluation. Presentation and opaque extension content stay beside, rather than inside, reduced executable semantics.

The EF implementation must prove tenant isolation, concurrent CAS, immutability, operation-key replay and atomic publication with real connections/providers. SQLite evidence alone is not four-provider qualification.
