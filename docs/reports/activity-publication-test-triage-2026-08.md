# What became of ActivityDefinitionPublicationTests (2026-08)

Closes [#1427](https://github.com/elsa-workflows/elsa-foundation/issues/1427). The companion to
[#1426](https://github.com/elsa-workflows/elsa-foundation/issues/1426), which ported two suites; this one
is deliberately **not** a port.

`ActivityDefinitionPublicationTests` (2472 lines, 48 tests) was deleted with the v1 substrate in
`ae436cef8`. Recover it with:

```
git show ae436cef8^:tests/Elsa/Workflows/Publishing/Api/GroundworkTests/ActivityDefinitionPublicationTests.cs
```

The point of this record is that the next person does not have to re-derive the triage. Its conclusion is
short: **12 tests died with the design, 31 were already covered elsewhere, and 5 invariants were covered
by nothing at all** — those five are now `ActivityPublicationCommitTests`.

## (a) Died with the design — 12 tests, not recoverable

#1420 made publication one transaction across design, runtime and publishing. That deleted the post-commit
intent, the redrive, the receipt deliverer, the runtime-phase probe, the resume path, and the
colocated/split branching. These tests assert that machinery, so porting them would resurrect assertions
about a design that was intentionally removed.

| Test | Machinery it asserts |
|---|---|
| `A_crash_after_the_runtime_commit_leaves_nothing_but_inert_runtime_material` | split ordered path |
| `A_retry_after_a_crash_at_the_runtime_boundary_converges_without_duplicate_effects` | split ordered path |
| `Split_publication_interrupted_before_its_design_commit_is_finished_by_a_retry` | split ordered path |
| `A_source_reference_belonging_to_another_artifact_is_still_a_conflict` | split ordered path |
| `A_colocated_publication_still_rejects_an_existing_source_reference` | colocated branch |
| `Split_publication_interrupted_before_its_receipt_is_finished_by_a_retry` | split + intent |
| `A_completed_split_publication_leaves_no_outstanding_intent` | post-commit intent |
| `A_colocated_publication_records_no_intent` | colocated branch + intent |
| `A_split_publication_abandoned_after_its_design_commit_is_finished_by_the_redrive` | redrive |
| `Redelivering_a_receipt_that_already_landed_still_retires_the_intent` | receipt deliverer + outbox |
| `Split_publication_that_fully_committed_still_rejects_a_replay` | split ordered path |
| `Negative_provider_resource_measurements_are_rejected_before_admission` | admission via the split harness |

The mechanical check behind this table: every one of these bodies references at least one of
`CreateSplitAsync`, `CrashBeforeDesignCommitStore`, `ActivityPublicationReceiptDelivery`,
`ActivityPublicationReceiptIntentDeliverer`, `DesignPostCommitIntentDocumentKind`,
`GroundworkDesignPostCommitOutbox` or `GroundworkDesignPostCommitRedrive` — **none of which exist anywhere
under `src/`.**

`SplitActivityPublicationOrderingTests` was deleted in the same family for the same reason and is likewise
not a candidate.

## (b) Already covered — 31 tests

The publisher, preflight, review-token and receipt surfaces survived the migration unchanged, and they are
covered by `tests/Elsa/Workflows/Publishing/Api/Tests` (426 tests) and
`tests/Elsa/Workflows/Publishing/Persistence/Groundwork/Tests`. Re-asserting them would be duplication,
not recovery. This covers the publisher rejection paths (build-metadata precedence, foreign draft, stale
layout, insufficient bump, admission and structure-contract rejection), the preflight and review-token
family, the receipt identity/replay/tenant-scoping family, and the compiler/diff/behaviour-hash family.

## (c) Covered by nothing — 5 invariants, now recovered

`GroundworkActivityPublicationCommand` is what the single-transaction rewrite produced: it went from 679
to ~440 lines when publication became one transaction. **Nothing under `tests/` referenced it.** Its
behaviour ships and no test asserted any of it.

These are now in `tests/Elsa/Workflows/Publishing/Api/GroundworkTests/ActivityPublicationCommitTests.cs`:

| Invariant | Test |
|---|---|
| One transaction leaves design version, runtime template, source reference and receipt all present | `Publication_commits_design_runtime_and_publishing_together` |
| The authoring head and recommended version advance, and the draft becomes `Published`, in that same commit | `Publication_advances_the_authoring_head_and_publishes_the_draft` |
| Authoring is resolved by definition, not by document id | `Publication_finds_authoring_by_definition_when_its_document_id_differs` |
| A tenant publishing an authorized global resource commits under its own operation scope, and the resource stays global | `Publication_uses_the_tenant_operation_scope_for_an_authorized_global_resource` |
| A failure inside the transaction leaves **no** part of the publication behind | `A_failure_inside_the_transaction_leaves_no_partial_publication` |

The last is the one with no substitute anywhere, and it is built to avoid the way this test usually goes
wrong. The transaction **opens and accepts every write**, and only the commit is refused; the test then
asserts that the publication staged more than zero rows before that happened. Refusing
`BeginUnitOfWork` instead would have proved nothing — the publication would never have staged anything,
so "nothing was written" would be true by construction rather than because anything rolled back.

It and the first test assert `Null` and `NotNull` over the same four artifacts, so neither can pass
vacuously: the same predicates discriminate between the two scenarios.

## What was not carried over, and why

The old harness is not portable. It composed lane stores, manifest bindings and split targets
(`GroundworkLaneStores`, `GroundworkLaneTargets`, `GroundworkManifestBindings`,
`SplitBindings()`) to select between the colocated and ordered paths — the exact machinery the single
transaction removed. The v2 command takes nine dependencies and has one entry point, so the new harness
builds them directly over `GroundworkV2TestPersistence` with all three lanes declared against one
provider, as a single-target host has them.

The seed data and the commit fixture *were* carried over: those are domain objects, not infrastructure,
and rebuilding them from scratch would have risked a fixture that no longer describes a real publication.

## An incidental finding

`GroundworkActivityPublicationCommand` compiles with
`CS9113: Parameter 'sourceReferences' is unread` on its `IWorkflowExecutableSourceReferenceStore`
dependency. It is dead wiring, not a missing write: the source reference is staged into the publication's
own transaction by the static
`GroundworkV2WorkflowExecutableSourceReferenceStore.StageCreate(transaction, commit.SourceReference)`,
which is what puts it inside the atomic commit in the first place. Writing it through the injected
instance would have put it *outside* the transaction.

`Publication_commits_design_runtime_and_publishing_together` asserts the reference is present afterwards
and `A_failure_inside_the_transaction_leaves_no_partial_publication` asserts it is absent when the
transaction fails, so both halves of that are now covered. The parameter is left in place here because
removing a public constructor parameter is an API change, not a test-recovery change.
