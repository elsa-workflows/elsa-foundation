# Proposed release and target model

Status: proposed. Field names describe required information; production public API/codec review remains open.
See [bundle](contracts/release-bundle.md) and [promotion](contracts/promotion.md) for invariants rather than duplicating them here.

| Record / owner | Immutable identity and content | Mutable observations / relationships |
|---|---|---|
| Release manifest / Publishing | Format/profile, typed selected roots, complete member/blob digests and edges, owner-version values, prerequisites and scoped requirement keys; release digest | Separate title/description and creation actor/time; evidence associations. Complete blobs frozen before release visibility. |
| Selected root / model owner | Tagged definition/version plus exact content identity and desired activate-serving/make-available action | Selected-version association stays distinct from deduplicated artifact producer identity. |
| Member/blob / artifact owner | Native executable; reusable version/contract/template; direct BPMN material; ETL v1/v2 artifact; explicit codec and owner identities | Target catalog rows/projections and local source/activation IDs are separate. Unknown kind/codec refuses. |
| Target identity / target host | Stable configured/persisted authority ID, authenticated tenant and trusted persistence partition | Full scope keys contexts/previews/attempt idempotency/mappings/receipts. Capability/provider inventory and observed revision are separate; display label is not identity. |
| Requirement / model/resource owner | Defining member, authored occurrence/placement/model scope, input/reference identity and resource kind | Optional explicit compatible sharing; no implicit merge by name. Unknown/dynamic required mapping refuses unless owner contract defines it. |
| Runtime deployment context / Runtime | Content subject, bounded resource mapping capture and model-locator associations, context digest and target tenant | Readiness observations separate. No release store lookup during execution; retained inline material or Runtime-owned exact retained read is required. |
| Resource mapping / resource owner | Scoped requirement -> target resource identity/incarnation; expected kind/scope | Revision/configuration observations for preview/evidence. No secret values; resource rotation/revocation remains ordinary policy. |
| Model locator mapping / BPMN owner | Source-qualified exact model/content/occurrence -> target local source/incarnation; association verified by owner | Physical destination locator may differ; exact immutable model content/hash does not. Never copy physical incarnation or rewrite tested body. |
| Preview / Publishing | Request subject and non-secret bindings, target scope, intended changes | Observed owner/resource/capability/policy revisions and fingerprint. It is not a reservation. |
| Attempt / Publishing | ID/key/fingerprint, action, release/mapping/preview identities, original actor, fixed ordered stage requests and locator-resolution recipe, candidate/command IDs | Append-only exact owner-stage receipts; one-time frozen activation payloads/fingerprints and resolved runtime context; CAS journal revision, lease, member observations, current status, resume/revoke actors, diagnostics and evidence references. |
| Command / transition owner | Stable ID/payload fingerprint, expected slot/owner/revision, exact desired content/context | Ready/Switched/Settled/Compensated/Rejected/Revoked plus command revision/fence. Guard/effect/receipt commit in owner transaction; owner compensation has a separate guarded receipt. |
| Receipt / transition owner | Observed original/new selection/revisions, content/context identities and outcome | Append-only owner fact; not serving authority. Release history may lag this fact. |
| Retention root / owning stores | Exact closure/context subject and root purpose (bundle capture, retained release, rollback, unresolved attempt) | Explicit expiry/removal policy. Existing execution roots remain independent; missing evidence retains conservatively. |
| Evidence association / Publishing | Actual frozen candidate/closure/report identities, covered roots/occurrences and exact suite/case revisions/digests where applicable | Mode, execution/assertion/cleanup/completeness states, target/bindings/prerequisite observations and timestamps. Never part of release content digest. |

## Runtime propagation

Capture context with the same authority that selects the executable/material. Carry it through source references, saved stimulus
recipients, durable start commands, model/native work, first/subsequent checkpoints, exact retained child dispatch and ETL admission.
Verify equality during retry/resume/duplicate detection. Independent live-source child starts select that source's context;
retained dependency starts inherit the captured scoped requirements. Never resolve a latest release mapping during resumption.

Native/reusable identity remains full artifact/template identity. BPMN direct material retains its protocol/profile, exact model
identity and worker closure. ETL retains its own positive integer version, format/hash and artifact ID; ETL continuation/run/lease/
snapshot/effect state is execution state and never release content. ETL owner revision retention continues after admission.

## Lifecycle and validation

The runtime context digest identifies mapping/locator choices, not mutable resource revision observations. Preview/evidence
fingerprints additionally capture actual non-secret revisions/configuration and prerequisites. A configuration change can
invalidate a preview or integration claim while ordinary existing-run resource rotation continues. ETL retains its own exact
host revision/handle after admission; this does not turn every workflow mapping into historical settings retention.

The [attempt state table](contracts/promotion.md#state-and-recovery) governs transitions and terminal uncertainty.
An overall application can make a standalone ETL catalog root available without inventing a serving slot for it. Root/member
outcomes distinguish catalog availability from active future-start selection. Dependency-only content never creates endpoints.

Initial rollback policy retains imported release closures until explicit release-retention removal. An operator must see that
removal ends that rollback promise; removal cannot invalidate serving/execution/unresolved-attempt roots. History remains a record
of past facts rather than a guarantee the target still serves the recorded release.
