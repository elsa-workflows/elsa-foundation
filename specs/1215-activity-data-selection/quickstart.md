# Activity data selection: production verification

**Status:** Reviewed M2 verification instructions, effective on M2 approval/merge. These are future
production acceptance instructions; no feature or journey is claimed passed. M1's failed product
criteria remain historical failures until explicitly retested.

Use [spec.md](spec.md), [authoring-contract.md](contracts/authoring-contract.md) and
[binding-contract.md](contracts/binding-contract.md). The [program plan](../../docs/plans/activity-data-selection.md)
owns milestone sequencing. Execute the feature-specific journeys after the reviewed production tasks,
against the exact committed candidate in isolated worktrees and disposable owned hosts/stores.

## Setup and existing commands

Read [Studio UX acceptance](../../docs/contributing/studio-ux-acceptance.md) and the
[backend e2e lifecycle instructions](../../e2e-tests/README.md). Preserve the machine's build-slot wrapper;
target affected projects and inspect `uptime` before attributing timing failures to product behavior.

The existing composed runner exposes only `--output` (plus help). From the repository root:

```bash
python3 tools/ux-acceptance/run.py --help
pnpm --dir Studio install --frozen-lockfile
pnpm --dir Studio exec playwright install chromium
python3 tools/ux-acceptance/run.py --output /tmp/studio-activity-data-<unique-run>
```

Replace the output placeholder with a new task-owned directory. The runner builds Workbench and Studio
from this checkout, copies host outputs, allocates local ports, uses disposable SQLite stores and stops
only its process groups. It refuses a dirty checkout. It does not attach to a personal/shared Studio or
reuse a developer's store/provider secrets. No external customer service or model is required.

Add the production feature journey to `Studio/tests/acceptance/journeys.spec.ts` and its required capture
names to `tools/ux-acceptance/run.py`'s `CAPTURES` inventory. Keep exact expected project/journey matching,
one successful execution, zero retries and required artifact checks. Passing the existing unrelated
journeys does not qualify data selection. Do not invent `--journey`, `--minimal` or `--restart` flags for
this runner: normal/minimal/restart variants need reviewed owned-host integration in the affected proof
infrastructure, with their actual entrypoints recorded in the final receipt.

The existing four profiles are desktop-light/desktop-dark at 1440×1000 and narrow-light/narrow-dark at
360×900. Run changed UI journeys in these profiles and capture a focused 320px picker/reference state
without treating 360px as 320px proof. Backend-only type/availability controls can run once per relevant
composition rather than duplicating them for every visual profile.

## Deterministic customer fixture

Use a real cataloged typed producer or an owned test-only activity/provider. It returns one Customer
object containing `customerId`, an explicitly serialized member such as `display_name`, a nullable member,
an optional missing member and a collection/map where relevant. The basic nested-ID proof may not replace
that output with a separately declared scalar ID. Use different A/B customers for iteration cases.

Record the producer/output/member declarations and serialization profile. Fixture-only activities and
proof drivers stay out of the shipped catalog. The consumer exposes actual committed input evidence.
Each ordinary customer graph starts with no workflow variables or output captures; saved evidence must
show that selecting data did not synthesize them.

## Nine finite journey families

Use the smallest cases that cover each independent behavior; reconcile every eligible row in the spec's
surface/source inventory. A non-applicable target needs its actual contract reason.

| Journey | Actions and required observable result | Evidence |
|---|---|---|
| 1. Basic source selection and shape | In native Sequence and linear Flowchart, open an untouched input before any run, browse Customer and select its ID; also select a whole compatible object/collection/map. Save, reopen, Code Apply, publish/run and observe the expected consumer value. Whole selection retains collection/set semantics. Repeat basic mapping in the minimal host. | Source facts, canonical JSON before/after, zero synthetic variables/captures, executable binding/type/conversion and committed input receipts; readable reference/nested-fields captures. |
| 2. Explicit expression composition | In compact and expanded supported installed syntaxes, insert a selected source, transform/combine it and exercise an alias collision and escaped serialized name. Undo/Redo once changes text plus binding together. Save/reopen/run. Missing modules preserve existing source/bindings. | Editor/document regressions, syntax-specific insertion source, portable binding identity, transformed committed input and compact/expanded captures. |
| 3. Graph edits and repair | Rename a same-label producer, move/delete/change its contract, copy a producer/consumer pair and a consumer alone, then Undo/Redo and reopen. Internal copies remap; external/unavailable references stay repairable. Use the repair action, not only its label. | Canonical identity/remap checks, composed edit/reopen trace and repair/provenance captures. Statically invalid direct and portable references reject before artifact/publication/activation writes. |
| 4. Causal paths and selected members | Run A/B iterations including different completion order, a conditional/join, and an explicit return/collection across a scope boundary. Check optional producer absence, member absence and explicit null separately; include known incompatible and deferred unknown literal member paths. No latest-result or implicit first-item fallback occurs. | Focused authority/resolver/conversion regressions plus real loop/conditional runs; static rejection receipts or consumer-materialization failure receipts as appropriate. Producer completion remains committed. |
| 5. Durable continuation and retry | Complete producer once, suspend before consumer, stop/restart the owned process on the same disposable store, resume to consumer completion and exercise defined retry-input behavior. The same causal result/selected value survives; producer count remains one. | Owned lifecycle/store identity and logs, execution/invocation/checkpoint IDs, pre/post-restart snapshots, producer count and completed consumer input evidence. |
| 6. Equivalent and restricted targets | Exercise reusable activity graphs and a public output consumed by another workflow, an ordinary activity hosted in BPMN, intrinsic value input and Switch condition at its actual location. Check secret/Connection/options/custom constraints and masked/denied/source-owned/read-only/captured inputs. Unwrapped repeater elements retain their literal contract. | Source/provider/template and document-location regressions; composed eligible/restricted journeys with actual mutation/visibility assertions. Explain model-owned closed BPMN boundaries. |
| 7. Current draft and partial information | Add/delete/move a producer without saving, switch target while loading and change metadata/permissions. Verify enabled normal autosave acknowledgment; repeat with manual/held edits through authorized non-persisting analysis or explicit save-and-load. Cover empty/no matches, unknown/truncated, denial/revocation, stale response and failed save/Retry/cancel. | Exact document/base/context identities, permission-scoped responses, request/write counts proving no hidden manual save, preserved input JSON and failure/recovery captures. |
| 8. Host and reader composition | Compose normal and minimal hosts; remove optional backend JS/Liquid and matching optional Studio expression-editor modules. Basic whole/member mapping still saves/publishes/runs. Preserve existing unsupported expressions and exercise binding capability/version rejection under the binding contract. | Host manifests and loaded-module evidence, minimal-host context/runtime receipts, saved unsupported source and fail-closed reader tests. Do not claim removal of internal shared code-editor packages. |
| 9. Rendered accessible experience | Keyboard-open the chooser, browse/select/replace/clear, cancel and restore focus. Inspect populated/empty/loading/error/repair/restricted states in supported light/dark appearances and narrow widths; selected references remain readable. | Four-profile feature journey, focused 320px PNG/ARIA, interaction trace and independent exact-head screenshot review. |

For typed target controls include scalar, typed object, JSON/Any, Array/List, HashSet and Dictionary.
Use authoritative collectionKind plus element alias. Include a non-primitive alias distinction, an ignored
member and custom/provider shape limit. Assert no render-time coercion or silent ignored member path.
Include nullable and optional intermediate parents with required non-null leaves, required-parent controls
and unknown suffixes; assert the composed effective contract, not only the leaf's declaration facts.
Nested source reads cover activity results, workflow inputs, scoped/shadowed variables and a structure's
exposed current item; write-target selectors do not gain member-write semantics.

Publication controls cover direct result/member inputs and existing portable expression parameters on
every affected executable-producing/test-run path. A known invalid required source/member fails before
artifact/publication/activation writes. Unknown literal-path presence/null/type checks can be deferred;
assert failure before consumer activity construction/invocation or intrinsic assignment, preserving the producer, rather than
requiring a static validator to predict runtime data.

## Normal, minimal and restarted owned hosts

Normal composition uses the production Workbench/Studio module path. Minimal composition is a copied,
owned composition with optional JavaScript/Liquid runtimes and optional Studio expression editor features
removed. Verify actual advertised capabilities, syntax choices and loaded optional scripts. Changing a
manifest without observing the resulting host is insufficient. A language-specific context returning
Unavailable is not proof of an independent basic data-selection route.

For restart reuse the ownership pattern in [durability tests](../../e2e-tests/durability/README.md) and
`e2e-tests/_ServerLifecycle.ps1`: same owned store, terminate/relaunch only the process started by the
journey, then actively resume. Record that the old process exited, the new process uses the same store,
and the consumer completed. A successful in-process reload or completed pre-restart run is insufficient.

The existing `Test-RestartRecovery.ps1` can exit green through `KNOWN ISSUE #1761` when resumedCount is
zero. That tracker outcome cannot pass journey 5. Use a reviewed feature-specific actual completion proof;
if it fails, retain the logs and mark the journey failed/incomplete. Do not turn a missing resumption into
a skipped or expected-failure success.

## Supporting regression checks

Run the owning affected suites after implementation: wire roundtrip and reference admission, shared
property/collection/custom-editor behavior, graph remapping/history, compact/expanded expression insertion,
source context/permissions/revisions, publishing gate, conversion/member plan/hash and causal runtime
resolver. Reuse existing tests for unchanged rules; add meaningful cross-seam regressions for newly fixed
behavior rather than tests mirroring document text. Backend design/publishing/runtime changes also require
the relevant real e2e suite and current source server, as the task-path rules require.

Run applicable architecture/maps, build/test and CI checks through the existing review loop. Focused
suite results support the real journeys; a green aggregate does not replace omitted feature criteria.

## Evidence and independent acceptance receipt

Retain one finite criteria ledger with Passed, Failed, Not run, Skipped, Canceled or Incomplete outcomes.
Each passing criterion names its actual host/profile/run and proof. Preserve failed M1 research results
separately from production retests.

Required downloadable evidence includes:

- `report.json` with clean tested source/head/merge identities, composition, stage and exact executed inventory.
- Canonical authored JSON before/after save/reopen/Code Apply, source/context revisions and permitted member
  metadata, compiled selection/type/conversion/hash and publication success/rejection/no-write receipts.
- Runtime execution/invocation/checkpoint identities, selected committed input values under existing
  authorized diagnostic access, causal A/B identities and the restart/producer-once receipts.
- Playwright JSON and HTML report, trace, PNG and ARIA captures for the changed states, minimal host and
  focused 320px state. Failed runs retain their artifacts; screenshots alone are not runtime proof.
- Independent receipt identifying the exact head, Actions run/artifact links, actual opened screenshots,
  tested functional/keyboard/recovery states, findings/retest and anything not verified. A reviewer's
  receipt must distinguish machine assertions from inspected experience.

Apply [Studio UX acceptance](../../docs/contributing/studio-ux-acceptance.md) to download/open current-commit
captures; local absolute paths alone are not shared evidence. Refresh expired/stale artifacts and rerun
affected whole suites/journeys after fixes. Verify the resulting `main` and reconcile the program/spec/issue
records only when the complete scoped evidence passes. Neither this quickstart, an approved contract,
the M1 supporting controls nor closed child counts establishes delivery.
