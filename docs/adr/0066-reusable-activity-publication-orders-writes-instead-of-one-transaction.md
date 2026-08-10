# Reusable-Activity Publication Orders Its Writes Instead of Requiring One Transaction

Status: accepted (2026-08-07) and implemented. The two limits recorded under Consequences were the absence of
the same thing — a driver that finishes a publication its caller abandoned. That driver now exists
([issue #1171](https://github.com/elsa-workflows/elsa-foundation/issues/1171)): the receipt limit is closed in
mechanism, and the retention limit is not, because the redrive delivers the receipt without retiring the
stranded source reference. Neither is closed in a default host until something schedules the sweep.

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
   atomically within the runtime target. A template written without the rest is unreachable — nothing can
   resolve it until phase 2 lands — so the partial state is inert, which is all the ordering needs. It is not
   thereby collectable; see Consequences, where reference garbage collection's narrower sense of
   "unreferenced" is settled by test.

   The two documents are *not* idempotent in the same way, and conflating them hid a defect long enough to
   reach review. The template is content-addressed and an identical one already present is skipped, so a
   repeat genuinely is a no-op. The source reference is create-only with no such check, so a naive repeat
   conflicts on it — which would have made a publication interrupted between phases 1 and 2 permanently
   unfinishable. A source reference carrying this publication's own artifact id is therefore recognised as
   phase one already done; one carrying a different artifact id remains a genuine conflict, and a co-located
   publication, which can never observe the state, keeps the strict create-only check.

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

**That redrive now exists**, so the paragraph above is history rather than a standing limit. The design commit
stages an intent in the `designPostCommitIntent` outbox, which makes the receipt obligation durable at exactly
the instant the publication becomes done; `GroundworkDesignPostCommitRedrive` claims those intents under a
fenced lease and writes the receipt whether or not the caller ever returns
([issue #1171](https://github.com/elsa-workflows/elsa-foundation/issues/1171)). Two qualifications. The sweep
is callable but nothing schedules it yet, so a default host still converges only when someone drives it. And
the redrive delivers the receipt only — it does not retire the stranded source reference, so the retention
limit recorded below is still open even where the receipt limit is not.

Recognising the retry requires comparing the stored publication against the one being replayed. A different
publication reusing the same id is not a resume and falls through to the ordinary create-only preflight, so
the resume path cannot turn a genuine conflict into a success. The same reasoning applies to the receipt
write itself: a conflicting receipt is only treated as benign when its stored content is identical.

Two new failure windows exist and are covered by the ordering rather than by compensation:

- **Between runtime and design** — the retry recognises its own source reference and skips phase one. Also
  asserted: a reference carrying a different artifact id is still a conflict, and a co-located publication
  still rejects an existing reference outright.
- **Between design and receipt** — the retry resumes at the receipt write. Also asserted: a fully committed
  publication is still rejected as a replay.

Both windows are now reached two ways. Seeding the interrupted state and asserting the retry completes covers
what the phases must tolerate; **crash injection** covers what they must leave. The injected crash refuses the
one commit carrying the design publication kind, which cuts the sequence at exactly the first phase boundary,
and pins that a host dying there leaves the template and its source reference and nothing else — no version,
no publication, no dependency projection, no receipt, and a draft still active. A restarted host retrying from
that state converges without repeating phase one: both runtime documents keep their original document version
and the dependency projection is still at its first sequence.

Each of these is mutation-checked, and two of the mutations are worth recording because of what they reveal
about the coverage this ADR previously claimed:

- Neutering the resume path fails the between-design-and-receipt test **and nothing else**. That test was
  written with the fix but never carried its `[Fact]` attribute, so until now the entire resume path could
  have been deleted and the suite would have stayed green. The earlier claim that both windows were
  mutation-checked was not true of that one.
- Committing design before runtime fails both crash tests and nothing else. Ordering — the property this ADR
  is named for — had no test at all until the crash injection existed.

The claim that an orphaned template is collectable by reference garbage collection **does not hold as it was
stated**, and is now settled by a test rather than by reasoning about content-addressing. The two senses of
"unreferenced" were being conflated. The stranded material *is* unreachable: nothing resolves a template
without its publication. It is not *unreferenced* in the only sense the collector acts on, because phase one
commits the template and its `Published` source reference atomically, and that reference is a live retention
root pointing straight at the template. The collector drops a reference only when it is retired or expired, so
a sweep over the stranded state does no work at all; retiring the reference is what makes both collectable, in
one subsequent sweep. Nothing retires it today.

That does not weaken the ordering — an unreachable template is still inert, which is what makes the partial
state safe — but it does mean the runtime lane is not self-cleaning. A caller that retries adopts the stranded
material and it stops being stranded; a caller that never retries leaves it in place indefinitely. Reclaiming
it needs a driver that retires the source reference of a publication that never finished. The redrive above is
the natural home for that — an intent kind that retires the reference, alongside the one that writes the
receipt — but it does not do it today, so this limit stays open on
[issue #1171](https://github.com/elsa-workflows/elsa-foundation/issues/1171) after the receipt limit closes.

Hosts that co-locate the three lanes are unaffected either way.
