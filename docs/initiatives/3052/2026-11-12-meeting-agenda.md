# Meeting agenda: Elsa 3 → Elsa 4 conversion (#3052), 2026-11-12

**Led by:** the coordinator. **With:** the approvers. **Recorded and transcribed.** Positions are numbered; the board maps them to people.

**How the meeting runs.** We take the items in the order below and see how far we get. The coordinator opens and closes each item
by saying its **anchor** aloud, for example *"D-6, effect flag"*. If the conversation moves to another item, the coordinator names
that item's anchor; we either decide it there or park it. For each item, each of us says:
- the restrictions and benefits of our view for **(a)** the architecture, **(b)** workflow users and **(c)** .NET Elsa
  developers;
- the evidence behind it.

Then we look for the middle ground everyone can agree on. At the end there is a read-back: every anchor with its outcome.

**Yardstick (the agreed platform principle):** "Elsa runs what the engineer builds. Guardrails and diagnostics, not
restrictions. The simplest path is the default, and advanced options stay possible without making the default complex."

## Briefing: the minimum to resolve

- **D-8, lost updates** (ADR 0092, #3411). *Only if it is still open after the final round.* When two parallel
  branches change the same variable: by default the last write wins with a warning, and a node can opt in to a strict
  `Fault`. One position accepts this; another wants no opt-in. **Blocks** S-10, S-5, S-6, S-7 and S-11.
- **D-6, effect flag** (ADR 0045 §5, #3384; D-2, #3856). Which registered script functions get the commit before and
  after their call. One view: every function, because the engine can't know (no metadata). The other view: only
  functions the host declares effectful. **Blocks** S-1 and S-7.
- **D-14, per-arrival join** (ADR 0045 §6, #3389 and #3763). Whether the outcome contract allows a second join mode
  that runs the target once per arrival, or keeps only the default join and parks the second mode as a future ADR
  (#4050). **Blocks** S-3.
- **D-15, .NET bindings** (ADR 0046 V10, #3422; ADR 0083, #3390). Whether input-binding expressions may use the guarded
  .NET opt-in, or only script activities; and whether the opt-in is a set of named profiles or one replaceable engine
  strategy. **Blocks** text only; the default (JSON, proxies off) goes ahead either way.
- **D-16, S-8 interop** (ADR 0046, #3422; #4103). Whether S-8 ships a minimal .NET interop, or only the default, with
  the opt-in built after the initiative. **Blocks** text only.
- **D-17, precision loss** (ADR 0046 V9, #3422). *Only if it is still open after the final round.* Whether losing
  digits when a changed value is written back is refused by default or only warned about. **Blocks** S-8 and S-9.
- **Small questions** (only if time allows): *host argument*, *date parse*, *transient read*.

**Confirm only (settled in writing):** **D-7, computed reads** (#3420): pinning plus a run-time fallback.

---

## D-8, lost updates *(only if open)*

**Decision to make:** does a node get an opt-in `Fault` next to the default warning?
**Already agreed around it:**
- the last write wins by default (D-8 applies to every write);
- the warning is visible in Studio and the write history, and keeps the overwritten value;
- tracking is light by default;
- the default depends on proving there is no gap for inline steps.

| Position | Summary | Notes |
|---|---|---|
| 1 | Accepts C, with an opt-in `Fault` and four conditions. | [accept](https://github.com/elsa-workflows/elsa-foundation/pull/3411#issuecomment-6781246934) |
| 2 | Warning only, no policy switch. | [position](https://github.com/elsa-workflows/elsa-foundation/pull/3411#issuecomment-6766994149) · final-round note: *to be linked* |
| 3 | Proposed C. | [C](https://github.com/elsa-workflows/elsa-foundation/pull/3411#issuecomment-6772890570) |

**Key question:** does an opt-in strict mode cost anyone anything if they don't use it?
**Possible middle ground:** C as written; or ship `Fault` later as an opt-in, with the hook kept in the ADR.
**Changes after the decision:** #3411, S-10 #4006.

## D-6, effect flag

**Decision to make:** which registered functions get the commit boundary.
**Already agreed around it:**
- a call is never refused, skipped or deduplicated;
- idempotency belongs to the designer;
- the engine commits before and after an external effect;
- the cost is measured as a commit count (#3856).

| Position | Summary | Notes |
|---|---|---|
| 1 | Effect-free unless the host declares a function effectful at registration. | [counter](https://github.com/elsa-workflows/elsa-foundation/pull/3384#issuecomment-6781247439) |
| 2 | No metadata: every function is possibly effectful, and a hint comes only after measurements. | [#3856 review](https://github.com/elsa-workflows/elsa-foundation/pull/3856#pullrequestreview-5842856223) · final-round note: *to be linked* |
| 3 | Accepted position 1. | [#3856 review](https://github.com/elsa-workflows/elsa-foundation/pull/3856#pullrequestreview-5856671197) |

**Key questions:**
- Built-ins such as `newGuid()` and `now()` are the most-called functions. Should they carry their own effect-free
  classification?
- A forgotten "effectful" flag silently loses a guarantee, while a forgotten "effect-free" flag only costs performance.
  Which risk do we prefer?

**Possible middle grounds:**
- **(a)** Built-ins are declared effect-free by Elsa; host functions are possibly effectful unless declared effect-free.
- **(b)** Position 1, plus a publish diagnostic for undeclared host functions reached from inputs.

**Changes after the decision:** #3384, #3856, S-1 #4046 FR-012.

## D-14, per-arrival join

**Decision to make:** does the outcome contract allow a per-arrival join mode?
**Already agreed around it:**
- the default join (ADR 0064) runs its target once per activation;
- outcomes are chosen by returning a recognised form.

| Position | Summary | Notes |
|---|---|---|
| 1 | Allow an explicit, visible per-arrival mode; S-3 proves it. | [card 17](https://github.com/elsa-workflows/elsa-foundation/pull/3389#issuecomment-6645085675) |
| 2 | Take it out of the ADR; no second mode without evidence. | [review](https://github.com/elsa-workflows/elsa-foundation/pull/3763#issuecomment-6765743497) |
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
- no live object enters stored state.

| Position | Summary | Notes |
|---|---|---|
| 1 | Bindings may opt in, behind five guardrails. A named profile is selected per expression and pinned by the executable. | [guardrails](https://github.com/elsa-workflows/elsa-foundation/pull/3422#issuecomment-6782921131) |
| 2 | Script activities only. | final-round note: *to be linked* |
| 3 | Bindings may opt in (position 1's guardrails). The shape is one replaceable engine strategy enabled by a feature; per-expression profiles are seen as chaotic. | [#3422](https://github.com/elsa-workflows/elsa-foundation/pull/3422#issuecomment-6793731476) |

**Key question:** with a replaceable strategy, how does a published executable record that it needs .NET access, so it
never runs silently on a host without it?
**Changes after the decision:** #3422, #3390, #3384.

## D-16, S-8 interop

**Decision to make:** does S-8 ship a minimal .NET interop, or only the default?

| Position | Summary | Notes |
|---|---|---|
| 1 | A minimal explicit interop in S-8 (card 23). | [card 23](https://github.com/elsa-workflows/elsa-foundation/pull/3422#issuecomment-6645477643) |
| 2 | *to be linked* | |
| 3 | Default only; the opt-in is built after the initiative (#4103), which anyone can pick up. | [#3422](https://github.com/elsa-workflows/elsa-foundation/pull/3422#issuecomment-6793731476) |

**Framing for this item:** *the problem is file ingress, not proxies.* Settle this shared understanding first; the decision
should follow from it.

**Evidence on file** (the 36-definition Elsa 3 corpus, and Jint 4.9.1):
- Only **one binding in one definition** uses .NET members: reading an uploaded file (`IFormFile.openReadStream()` and
  `.name`).
- A host function can return a real `Uint8Array` without proxies, so `getFormFileBytes(activityRef, fileName)` covers
  that scenario.
- Elsa 4 keeps **no file bytes** today: no files output on HttpEndpoint, the body stored as UTF-8, and `FormFile`
  variables refused (#3194). File ingress is the missing piece.
- S-1's detector misses this usage, because Elsa 3 used camelCase for .NET members ([finding](https://github.com/elsa-workflows/elsa-foundation/pull/4046#issuecomment-6793834195)).

**Changes after the decision:** #3422, #4103, S-1 #4046.

## D-17, precision loss *(only if open)*

**Decision to make:** refuse or warn when digits would be lost as a changed value is written back.
**Already agreed around it:**
- a five-part diagnostic;
- accepting the loss is explicit and recorded in the conversion contract.

| Position | Summary | Notes |
|---|---|---|
| 1 | Refuse: the engine itself changes the data. | [reply](https://github.com/elsa-workflows/elsa-foundation/pull/3422#issuecomment-6781247798) |
| 2 | Originally a warning. | final-round note: *to be linked* |
| 3 | Accepted position 1: refuse by default. | |

**Key question:** the loss is caused by the engine, not built by the engineer. Is refusing it a guardrail or a
restriction?
**Changes after the decision:** #3422, S-9 #3805.

## Small questions *(if time allows)*

- **Host argument** (#3422): does a lossy value passed to a host function count as a write? Proposed: warn.
- **Date parse** (S-1 #4046 OQ-3): `new Date(input)` without a source time zone. Written: warn. Stricter: refuse.
- **Transient read** (S-4 #4024 OQ-3, #3420 RQ5): a computed read that reaches a transient variable. Written: fault.
  Alternative: `undefined` with a warning.

## Confirm only

- **D-7, computed reads:** pinning plus a run-time fallback (#3420, #3388, spec 095, S-4). To confirm in the final round (RQ3): the reads
  are part of the step's input snapshot.

## Parking lot

## Read-back

| Anchor | Outcome |
|---|---|
| D-8, lost updates | |
| D-6, effect flag | |
| D-14, per-arrival join | |
| D-15, .NET bindings | |
| D-16, S-8 interop | |
| D-17, precision loss | |
| D-7, computed reads | |
| Small questions | |
