# Specialist validation guide

**Status**: M1 design/qualification recipe. Only the M0 commands and wireframe exist today;
production commands/routes below are acceptance targets to deliver, not a claim they already work.
The [support contract](contracts/support.md) owns the finite matrix and actual status of each cell.

## Existing research evidence

Use .NET 10 and Python 3.12+, a new absolute directory outside the checkout and a unique local version:

```sh
bash tools/agents/research/run.sh
bash tools/agents/research/run.sh --foundation-compatibility
python3 tools/agents/publication/qualify.py \
  --output /absolute/new-specialist-research \
  --version 4.0.0-specialist.local1
```

The first command proves bounded SDK behavior (17 checks plus a separate-process session check).
The second proves source-project assistant/MAF composition, without provider requests.
The third produces/consumes actual packages and executes reviewed publication plus native/BPMN
fixtures and the isolated lifecycle sketch. Its retained status is PassedWithRecordedGap while
the old BPMN package brings two Design.Core contracts. Its expected-failure children stay Failed.
None of these commands establishes the full product, normal host or live model matrix.

Serve/open [the wireframe](design/studio-wireframes.html) as a local static document. It makes no
backend/model requests. Navigate all seven views and the scenario selector; use keyboard arrows on
the tabs and test 320px page overflow. This is design verification, not production acceptance.

## Product prerequisites to implement and qualify

- Install the named normal host and specialist runtime/Design/API/Studio/provider packages, with
  durable generic stores, specialist journal, Connections metadata and Secrets/key-ring configuration.
- Use private qualification databases and normal tenant/user/service identities with explicit grants.
  No existing developer database, credentials or running user host is silently modified.
- Select a supported connection/model capability profile. Deterministic/protocol fixtures require no
  paid model. Live examples use only authorized existing access or separately completed provisioning;
  disclose provider calls and bounded budgets before consuming access.
- Keep exact produced package/asset hashes, locks, deployed readers/schema versions and host readiness
  evidence. A listening port is insufficient; activate the normal shell/public lifecycle.

## Author and publish Copywriter/Proofreader

1. Sign in to normal Studio, open Agents and create Copywriter. Give it required text Brief input,
   required article result, instructions and a supported model connection. Save/reload; record exact revision.
2. Verify the unpublished row appears with correct server total/cursor. Exercise a second author's
   conflicting save and confirm both winning state and retained local changes.
3. Open the playground, save/identify the candidate, run a fresh bounded test, and inspect exact
   candidate/model/schema/result/usage. Malformed result must fault without an accepted article.
4. Save an evaluation case. Preflight/publish Copywriter1.0.0 through its exact review/idempotency path.
   Verify ordinary picker discovery, usable ports and immutable history.
5. Create Proofreader with an article input and structured verdict/comments result. Its verdict enum
   constraint must be part of the generated/published JSON schema. Publish an exact version.
6. Change Copywriter instructions only. Verify the behavior hash changes while old workflow bindings
   remain on1.0.0. A source-owned or historical record is read-only; permitted fork creates new authority.

## Run equivalent native and BPMN editorial processes

Native: Copywriter → Proofreader → structured-verdict branch → bounded rewrite or human review.
Use [the editorial contract](contracts/editorial.md): feed the rejected article/comments into the
next writer call, maximum three total rounds; exhaustion takes an explicit process branch. Restart
at the exact human wait, approve/deny in separate runs, and replay/alter/late-deliver decisions to
prove one continuation and no approval after denial. BPMN executes these same cases.
Place the same exact specialist versions in BPMN service-task bound children and corresponding routing.
Save/reload both, then run with the same brief. Prove the committed article reaches Proofreader, the
validated verdict controls routing, and parent continuation is accepted once under duplicate/late delivery.

Exercise supported BPMN export/import and exact bindings; unsupported content must refuse without
inventing a version. Repeat from an independent Design-free runtime package consumer after the
BPMN Core/Design split. A directly constructed executable is useful component proof but cannot
replace actual authoring/publishing/interchange acceptance.

## Prove continuity, recovery and cancellation causally

Run fresh twice and confirm no prior history. Enable explicit continuity and use one authorized
conversation/expected revision; restart between accepted turns and confirm permitted prior history.
Foreign tenant/owner/version, changed operation fingerprint and overlapping stale revision must fail.

Force process exits at provider-dispatch, journal acceptance, background receipt/native checkpoint
and settlement acknowledgement boundaries. Accepted work must replay without another model request.
Dispatched-without-acceptance must expose uncertainty and block automatic fresh work.

Force journal acceptance/cancellation in both orders, cancellation before admission and delayed native
cancellation handoff. Observe the native process and conversation independently. Completed/not-applied
must not be mislabeled cancelled; no cancelled process continues. Late/stale worker fences, duplicate
receipts and out-of-order settlement cannot overwrite accepted history or cancel a newer operation.

Include sensitivity/redaction input/result flow, explicit native encryption/secret-input refusals,
encrypted journal key rotation/readers, disabled new admission, retention/purge and
unknown usage. Never infer restart safety from a persistence-policy setting or session serialization alone.

## Tools, knowledge, teams and evaluation

Deliver the #2912 integration, then qualify scoped style-guide retrieval, one workflow tool and one
MCP tool. Denial/revocation/changed approval produces zero protected effect. Restart around exact
approval and effect-before-ack; report the connector's real idempotency/reconciliation guarantee.

Run sequential, complete-cohort concurrent and bounded-refinement teams inside a parent. Force a
delayed participant, duplicate/conflicting messages, exhausted budgets and cancellation. Verify cohort
membership/barrier, no further prohibited dispatch, preserved accepted decisions and one parent result.

Compare exact candidate versions on a saved evaluation-set revision. Contract assertions and model
quality rubrics remain distinct; failed/skipped/cancelled/unknown cases retain their actual status.
Test the real Studio permission/read-only/conflict/save/undo/keyboard/narrow journeys and occurrence links.

## Final release evidence

Run all24 deterministic/protocol provider/store/host cells, the independent deployment/Studio shapes,
and four authorized live native/BPMN provider journeys. Retain exact versions/hashes, assertions,
process identities, original failure statuses and cleanup/closure proof. Ship docs/examples/configuration/
upgrade/rollback/recovery guidance. Complete A1–A15 only from observed product receipts, independent
review, required exact-head CI and resulting descendant-main qualification.
