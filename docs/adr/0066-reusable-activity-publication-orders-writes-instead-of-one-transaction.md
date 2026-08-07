# Reusable-Activity Publication Orders Its Writes Instead of Requiring One Transaction

Status: accepted (2026-08-07) and implemented, with one deliberate limit recorded under Consequences.

Tracking: [issue #1156](https://github.com/elsa-workflows/elsa-foundation/issues/1156).
Constrained by [ADR 0065](0065-groundwork-persistence-targets-are-named-and-lanes-bind-to-them.md).

## Context

Reusable-activity publication is the only Elsa operation that writes Design, Runtime and Publishing document
kinds in a single commit. `GroundworkActivityPublicationCommand` and `GroundworkSourceActivityPublicationCommand`
express it as one `IDocumentStore.SaveAllAsync(DocumentCommitScope.Of(kinds), …)` spanning
`executableActivityTemplate` and `workflowExecutableSourceReference` (runtime), the activities-design kinds and
their management projection, and `activityPublicationReceipt` (publishing).

Groundwork's unit of work is explicitly bounded to a single store: *"a multi-document atomic unit of work over
a single `IDocumentStore`"*. There is no cross-store transaction and no distributed-transaction machinery. So
once ADR 0065 lets a host put those lanes in different databases, that commit cannot hold as written.

Plain **workflow** publish is already not atomic across lanes: `PublishWorkflowRequestHandler` writes the
executable, then the source reference, then activates the publication, with a compensating retire on
activation failure. The reusable-activity path is the outlier, not the norm.

## Decision

Reusable-activity publication becomes an **ordered, forward-converging sequence** rather than one transaction.
Ordering is chosen so that every partial state is inert and every phase is idempotent, which means convergence
needs redrive only — no compensating deletes.

1. **Runtime target first.** Commit `executableActivityTemplate` and `workflowExecutableSourceReference`
   atomically within the runtime target. Both are content-addressed and create-only, so a repeat is a no-op.
   A template written without the rest is unreferenced: nothing can reach it until phase 2 lands, and the
   existing reference-garbage-collection machinery plus the template hash claim can collect it.

2. **Design target is the linearization point.** Commit every activities-design kind and the
   management-projection batch in one design-target commit. **The publication is done when this commits.**
   Draft and authoring state still move under their own optimistic versions, so a concurrent publication
   loses here exactly as it did under one transaction.

3. **Publishing receipt is written last, and is recoverable rather than authoritative.** It is derived from
   the caller's own commit description, so a retry with the same idempotency key resumes at this step instead
   of redoing the publication. An already-present receipt is success, not a conflict: a racing retry got
   there first. A missing receipt after a successful design commit is a recoverable artifact, not a failed
   publication.

**When all three lanes resolve to one target, the sequence folds back into today's single atomic commit** via
an explicit code path, not as an emergent property. The overwhelmingly common host keeps the guarantee it has.

## Sequencing note

The co-location refusal shipped first, deliberately. Before it, the commands held one ambient document store,
so a split host would have written the runtime template and source reference into the **design** database and
reported success. Ordering the phases on a substrate that silently misfiles documents would have meant
debugging phase boundaries against a broken base. The refusal is now replaced by the ordered sequence;
`ActivityPublicationLaneSplitException` remains for cases the sequence cannot resolve.

## Consequences

The saga converts a currently-atomic guarantee into an ordered, eventually-consistent one for split hosts.
That is a real weakening and is why this is an ADR rather than an implementation detail. It buys the ability
to publish reusable activities on a host whose design and runtime data are separated — the last operation
blocking a fully split topology.

**Convergence is caller-driven, and that is the limit worth stating plainly.** No new document kind was
added. A split publication that is interrupted after the design commit is finished by retrying it with the
same idempotency key: the retry recognises that the design publication already exists and matches the commit
being replayed, and completes at the receipt write instead of redoing the publication or rejecting itself as
a duplicate. A caller that never retries leaves the receipt absent — the publication is done and observable,
but its idempotency artifact is missing until someone retries. A background redrive would remove even that
dependence on the caller, and would need a design-lane outbox; the design-operation marker ledger is the
natural home. This is a smaller gap than it looks, because an idempotency key only has a purpose for a caller
that retries.

Recognising the retry requires comparing the stored publication against the one being replayed. A different
publication reusing the same id is not a resume and falls through to the ordinary create-only preflight, so
the resume path cannot turn a genuine conflict into a success. The same reasoning applies to the receipt
write itself: a conflicting receipt is only treated as benign when its stored content is identical.

Two new failure windows exist and are covered by the ordering rather than by compensation: a crash after the
runtime commit leaves an unreferenced, collectable template, and a crash after the design commit leaves the
publication done with the receipt recoverable on retry. The second is tested by interrupting a split
publication before its receipt and asserting the retry completes it, and by asserting that a fully committed
publication is still rejected as a replay. The first — that an orphaned template is genuinely collectable by
reference garbage collection — is asserted by reasoning about content-addressing rather than by a test, and
is worth covering alongside the background redrive.

Hosts that co-locate the three lanes are unaffected either way.
