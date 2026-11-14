# Qualification guide: workflow release promotion

Status: planned validation, not implemented commands or completed evidence. Runnable example scripts and exact command syntax
are delivered with the API/CLI/Studio slices. Do not infer product acceptance from this guide.

## Owned environment setup

Use the existing source [backend quickstart](../../docs/contributing/backend-source-quickstart.md) and
[Studio proof runner](../../docs/contributing/studio-ux-acceptance.md) as composition references. The feature witness will allocate
one source and two independent target content roots, ports, stores and stable environment IDs. It owns and terminates only its
processes. Use current supported native/BPMN stores and SQLite ETL runtime; unavailable provider combinations must refuse honestly.
All targets use different non-secret resource identities; credentials stay in their own target stores. No shared listener/store.

## Required journeys

1. Author/publish native and ordinary/direct BPMN versions, standalone ETL v1/v2 pipelines and workflows invoking them. Include a
shared transitive reusable definition, exact child dispatch, embedded model/native worker and role-specific ETL resources.
2. Create a release, download it twice and compare manifest/blob/owner identities. Transfer offline, then remove source runtime
retention where safe and prove the retained release still downloads. Validate a changed blob, missing edge, cycle, unsupported kind
and same-identity/different-content control before target mutation.
3. Authenticate separately to target A and B. Preview the same release with different mappings; include missing/type/incarnation/
permission/tenant/provider conflicts. No dependency-only route/schedule may become active just because content was imported.
Attempt generic native/direct-BPMN starts by known dependency IDs and prove refusal, while retained child invocation succeeds
and any pre-existing serving selection remains intact. Repeat equal IDs/idempotency keys across distinct trusted partitions.
4. Apply via actual Studio and packaged CLI against real target APIs; use direct API parity requests as a separate witness. Verify
exact source versions (including a noninitial ETL integer version), bytes/hashes and target-local locator associations.
5. Change a resource, owner revision, capability or evidence policy after preview. Verify stale refusal, identical same-key retry,
same-key/different-payload conflict, no-op and binding-only update. Separately race a different-key unresolved overlapping attempt
and an external publisher. Preserve owner-compensation versus external-supersession outcomes.
6. Interrupt real hosts before/after each material staging/owner effect/receipt/history boundary. Delay an old worker across fence
revocation/takeover. Restart, inspect unknown/partial outcomes, authenticate and resume forward; verify no late untracked effects.
7. Keep real native/BPMN/ETL work waiting while activating/rebinding/rolling back. Delay ETL admission and exact child/model worker
start until after rebinding. Resume old work with original pins/scoped mapping choices and normal credential rotation; new starts
follow intended owner selection. Verify fresh rollback blockers and preserved retained content.
8. Reopen release/attempt/history pages and inspect empty/loading/denied/read-only/stale/no-op/failed/partial/recovering/superseded
states. Exercise focus, keyboard, relevant themes and narrow layouts. Download/open exact-commit screenshots/traces and record
independent UX acceptance using the existing review loop.
9. Run relevant domain/consumer/architecture/maps checks, current-head review and required hosted checks. Merge under current rules,
then repeat critical resulting-main witnesses. Keep unsuccessful/unavailable evidence distinct and link the exact source/artifacts.

## Evidence record

Each witness records commit/dirty state, manifest/content/context IDs, target identities, invocation/attempt/owner receipts,
assertion and execution outcomes, cleanup, package/provider observations and downloadable artifacts. Controlled external substitutes
never count as actual target integration. The final docs must include executable pipeline examples and documented JSON/exit statuses.
