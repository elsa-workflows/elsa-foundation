# Reusable-Activity Publication Orders Its Writes Instead of Requiring One Transaction

Status: **proposed — not implemented.** The co-location constraint below is what ships today; the ordered
saga is the decided direction and is not yet built.

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

2. **Design target is the linearization point.** Commit every activities-design kind, the management-projection
   batch, and the design-operation idempotency marker in one design-target commit. **The publication is done
   when this commits.** The idempotency anchor moves here from the publishing receipt, reusing the marker
   ledger the design lane already maintains.

3. **Publishing receipt is a post-commit intent.** It is delivered through a design-lane outbox and redriven
   to convergence. A missing receipt after a successful design commit is a delivery lag, not a failed
   publication.

**When all three lanes resolve to one target, the sequence folds back into today's single atomic commit** via
an explicit code path, not as an emergent property. The overwhelmingly common host keeps the guarantee it has.

## What ships today instead

Until the above is built, splitting those three lanes is **refused**: both publication commands resolve their
lanes' targets and throw `ActivityPublicationLaneSplitException` with the actual lane-to-target mapping when
they differ. Every other split topology works.

This is a deliberate interim, and the reason is worth recording. Before the guard existed, the commands held
one ambient document store, so a split host would have written the runtime template and source reference into
the **design** database and reported success. Building the saga on top of a substrate that silently misfiles
documents would have meant debugging phase boundaries against a broken base.

## Consequences

The saga converts a currently-atomic guarantee into an ordered, eventually-consistent one for split hosts.
That is a real weakening and is why this is an ADR rather than an implementation detail. It buys the ability
to publish reusable activities on a host whose design and runtime data are separated — the last operation
blocking a fully split topology.

Three new failure windows appear and need crash-between-phase coverage: after the runtime commit, after the
design commit, and after the outbox claim. Each must converge on redrive, and the orphaned-template case must
be shown collectable rather than merely harmless.

A design-lane outbox does not exist yet; the runtime lane's post-commit outbox is runtime-lane machinery. The
design-operation marker ledger is the natural home for it.

Hosts that co-locate the three lanes are unaffected either way.
