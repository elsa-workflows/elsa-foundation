# Meeting agenda: Elsa 3 → Elsa 4 conversion (#3052), 2026-11-12

**Led by:** the coordinator. **With:** the approvers. **Recorded and transcribed.** Positions are numbered; the board maps them to people.

**How the meeting runs.** We take the items in the order below and see how far we get. The coordinator opens and closes each item
by saying its **anchor** aloud, for example *"D-17, precision loss"*. If the conversation moves to another item, the coordinator names
that item's anchor; we either decide it there or park it. For each item, each of us says:
- the restrictions and benefits of our view for **(a)** the architecture, **(b)** workflow users and **(c)** .NET Elsa
  developers;
- the evidence behind it.

Then we look for the middle ground everyone can agree on. At the end there is a read-back: every anchor with its outcome.

**Yardstick (the agreed platform principle):** "Elsa runs what the engineer builds. Guardrails and diagnostics, not
restrictions. The simplest path is the default, and advanced options stay possible without making the default complex."

**After the final round.** Position 2 approved every open PR at its current head
([summary](https://github.com/elsa-workflows/elsa-foundation/issues/3052#issuecomment-6800707535)), except D-14 on
#3389 and S-3 FR-022, the transient read on S-4, and D-15 and D-17, for which no written position exists. That settles
D-6, D-7 and D-8 in writing. No meeting-item comments were written, so the items below carry each position as it stands.

## Briefing: the minimum to resolve

- **D-17, precision loss** (ADR 0046 V9, #3422). Whether losing digits when a changed value is written back is refused
  by default or only warned about. **Blocks** S-8 and S-9.
- **D-16, S-8 interop** (ADR 0046, #3422; #4103). Whether S-8 ships a minimal .NET interop, or only the default, with
  the opt-in built after the initiative. The real gap behind it is file ingress (#3194). **Blocks** S-8's scope.
- **D-14, per-arrival join** (ADR 0045 §6, #3389 and #3763). Whether the outcome contract allows a second join mode
  that runs the target once per arrival, or keeps only the default join and parks the second mode as a future ADR
  (#4050). **Blocks** S-3.
- **D-15, .NET bindings** (ADR 0046 V10, #3422; ADR 0083, #3390). Whether input-binding expressions may use the guarded
  .NET opt-in, or only script activities; and whether the opt-in is a set of named profiles or one replaceable engine
  strategy. **Blocks** text only; the default (JSON, proxies off) goes ahead either way.
- **D-18, implementation per spec** (working agreement of 2026-10-26; initiatives guide *Implementing a spec on the
  branch*). Whether a spec may be implemented on the initiative branch as soon as its own decisions are agreed, or every
  spec is still defined before any implementation starts. **Blocks** when the first agreed spec can be built.
- **Small questions** (only if time allows): *transient read*, *date parse*, *host argument*.

**Confirm only (settled in writing):** **D-8, lost updates**; **D-6, effect flag**; **D-7, computed reads**.

**After the meeting:** position 1's approvals at the current heads (none recorded yet), and the open change request on
#3388 (at `d65800a40`), which the later commits answer.

---

## D-17, precision loss

**Decision to make:** refuse or warn when digits would be lost as a changed value is written back.
**Already agreed around it:**
- a five-part diagnostic;
- accepting the loss is explicit and recorded in the conversion contract.

| Position | Summary | Notes |
|---|---|---|
| 1 | Refuse: the engine itself changes the data. | [reply](https://github.com/elsa-workflows/elsa-foundation/pull/3422#issuecomment-6781247798) |
| 2 | Originally a warning; no written position in the final round, and not part of the #3422 approval. | [final round](https://github.com/elsa-workflows/elsa-foundation/issues/3052#issuecomment-6800707535) |
| 3 | Accepted position 1: refuse by default. | |

**Key question:** the loss is caused by the engine, not built by the engineer. Is refusing it a guardrail or a
restriction?
**Changes after the decision:** #3422, S-9 #3805; S-8 can then be defined.

## D-16, S-8 interop

**Decision to make:** does S-8 ship a minimal .NET interop, or only the default?

| Position | Summary | Notes |
|---|---|---|
| 1 | A minimal explicit interop in S-8 (card 23). | [card 23](https://github.com/elsa-workflows/elsa-foundation/pull/3422#issuecomment-6645477643) |
| 2 | Approved the default-only text of #3422 in the final round (only D-15 and D-17 excepted). | [approval](https://github.com/elsa-workflows/elsa-foundation/pull/3422#pullrequestreview-5861087921) |
| 3 | Default only; the opt-in is built after the initiative (#4103), which anyone can pick up. | [#3422](https://github.com/elsa-workflows/elsa-foundation/pull/3422#issuecomment-6793731476) |

**Framing for this item:** *the problem is file ingress, not proxies.* Settle this shared understanding first; the decision
should follow from it.

**Evidence on file** (the 36-definition Elsa 3 corpus, and Jint 4.9.1):
- Only **one binding in one definition** uses .NET members: reading an uploaded file (`IFormFile.openReadStream()` and
  `.name`).
- A host function can return a real `Uint8Array` without proxies, so `getFormFileBytes(activityRef, fileName)` covers
  that scenario.
- Elsa 4 keeps **no file bytes** today: no files output on HttpEndpoint, the body stored as UTF-8, and `FormFile`
  variables refused (#3194). File ingress is the missing piece, and is proposed as its own defect linked from S-8.
- S-1's detector misses this usage, because Elsa 3 used camelCase for .NET members ([finding](https://github.com/elsa-workflows/elsa-foundation/pull/4046#issuecomment-6793834195)).

**Changes after the decision:** #3422, #4103, S-1 #4046; S-8 can then be defined.

## D-14, per-arrival join

**Decision to make:** does the outcome contract allow a per-arrival join mode?
**Already agreed around it:**
- the default join (ADR 0064) runs its target once per activation;
- outcomes are chosen by returning a recognised form.

| Position | Summary | Notes |
|---|---|---|
| 1 | Allow an explicit, visible per-arrival mode; S-3 proves it. | [card 17](https://github.com/elsa-workflows/elsa-foundation/pull/3389#issuecomment-6645085675) |
| 2 | Take it out of the ADR; no second mode without evidence. Kept in the final round. | [review](https://github.com/elsa-workflows/elsa-foundation/pull/3763#issuecomment-6765743497) · [final round](https://github.com/elsa-workflows/elsa-foundation/pull/3389#pullrequestreview-5860972097) |
| 3 | Take it out, keep one sentence, and park it as #4050, with position 1's proofs as the bar it must clear. | [position](https://github.com/elsa-workflows/elsa-foundation/pull/3763#issuecomment-6772904779) |

**Key question:** which real workflow needs per-arrival behaviour today?
**Changes after the decision:** #3389, #3763, S-3 #3810 FR-022.

## D-15, .NET bindings

**Decision to make:**
- whether input-binding expressions may use the guarded .NET opt-in;
- what shape the opt-in takes: named profiles, or one replaceable strategy.

**Already agreed around it:**
- the default is JSON values;
- general .NET access always comes with a publish warning and a Studio marker;
- no live object enters stored state;
- the opt-in is built after the initiative (#4103), so this item changes text only.

| Position | Summary | Notes |
|---|---|---|
| 1 | Bindings may opt in, behind five guardrails. A named profile is selected per expression and pinned by the executable. | [guardrails](https://github.com/elsa-workflows/elsa-foundation/pull/3422#issuecomment-6782921131) |
| 2 | Earlier: script activities only. No written position in the final round; not part of the approvals of #3384, #3390, #3422. | [final round](https://github.com/elsa-workflows/elsa-foundation/issues/3052#issuecomment-6800707535) |
| 3 | Bindings may opt in (position 1's guardrails). The shape is one replaceable engine strategy enabled by a feature; per-expression profiles are seen as chaotic. | [#3422](https://github.com/elsa-workflows/elsa-foundation/pull/3422#issuecomment-6793731476) |

**Key question:** with a replaceable strategy, how does a published executable record that it needs .NET access, so it
never runs silently on a host without it?
**Possible middle ground:** decide the binding question now, and leave the shape to #4103's own design, within the
guardrails.
**Changes after the decision:** #3422, #3390, #3384.

## D-18, implementation per spec

**Decision to make:** does the initiative keep its stricter rule that every spec is defined before any implementation
starts, or does each spec advance to implementation once it is approved and every ADR and decision it applies is
agreed?
**Already agreed around it:**
- the working agreement of the [2026-10-26 meeting](https://github.com/elsa-workflows/elsa-foundation/issues/3052#issuecomment-6419640542):
  all specs are defined before implementation starts, so a poor design shows up on paper, not in code others build on;
- the initiatives guide now applies phases per spec by default (*Implementing a spec on the branch*): an agreed spec is
  implemented on the initiative branch, its implementation PRs run the full checks, Code Review approves them and the
  coordinator merges them; decisions and code land on `main` together. An initiative's own stricter rule holds until
  the approvers change it.

| Position | Summary | Notes |
|---|---|---|
| 1 | Agreed the 26 Oct working agreement; current position to be asked. | |
| 2 | Agreed the 26 Oct working agreement; current position to be asked. | |
| 3 | Drop the stricter rule: a spec whose decisions are agreed is implemented on the initiative branch right away. | guide change on the initiative branch |

**Key question:** can code for an agreed spec be forced to change by a later decision on another spec, and is "every
ADR and decision it applies is agreed" enough to guard against that?
**Possible middle ground:** per spec, but only for specs whose ADRs no open decision touches.
**Changes after the decision:** the board's working agreement and phase line.

## Small questions *(if time allows)*

- **Transient read** (S-4 #4024 OQ-3, #3420 RQ5): a computed read that reaches a transient variable. Written: fault.
  Alternative: `undefined` with a warning. Position 2 left it open for the meeting.
- **Date parse** (S-1 #4046 OQ-3): `new Date(input)` without a source time zone. Written: warn. Stricter: refuse.
  Position 2 approved the warning as written; position 1 still to answer.
- **Host argument** (#3422): does a lossy value passed to a host function count as a write? Proposed: warn.

## Confirm only

- **D-8, lost updates:** ADR 0092 option C: by default the last write wins with a warning, and a node can opt in to
  `Fault` (#3411). Position 2 accepted it in the final round
  ([approval](https://github.com/elsa-workflows/elsa-foundation/pull/3411#pullrequestreview-5861178293)).
- **D-6, effect flag:** a registered function is effect-free unless the host declares it effectful at registration;
  only declared calls get the commit boundary; a call is never refused or skipped (#3384, #3856, S-1 FR-012). Position 2
  accepted it in the final round
  ([approval](https://github.com/elsa-workflows/elsa-foundation/pull/3856#pullrequestreview-5861161903)).
- **D-7, computed reads:** pinning plus a run-time fallback (#3420, #3388, spec 095, S-4). Position 2 confirmed RQ3: the
  reads a computed name performs are part of the step's input snapshot
  ([comment](https://github.com/elsa-workflows/elsa-foundation/pull/3420#issuecomment-6799537621)).

## Parking lot

## Read-back

| Anchor | Outcome |
|---|---|
| D-17, precision loss | |
| D-16, S-8 interop | |
| D-14, per-arrival join | |
| D-15, .NET bindings | |
| D-18, implementation per spec | |
| Small questions | |
| D-8, lost updates (confirm) | |
| D-6, effect flag (confirm) | |
| D-7, computed reads (confirm) | |
