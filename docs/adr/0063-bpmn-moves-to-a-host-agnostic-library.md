# ADR 0063: BPMN moves to a host-agnostic library

**Status**: Accepted
**Date**: 2026-08-09
**Accepted**: 2026-10-28
**Deciders**: Sipke Schoorstra, Frans Nieuwenhuizen

## Context at proposal (9 August 2026)

This section records the original extraction rationale and repository layout. The delivered
integration and current evidence are recorded below.

BPMN Phases 1 through 3 are complete: sixteen specs merged, roughly 9,100 lines of source and 11,900
lines of tests under `src/essentials/Activities/Bpmn/`. No ADR records why any of it is shaped the way it
is. The rationale lives in `specs/225-bpmn-container-activity/spec.md`, in
`docs/program-goals/bpmn-engine.md`, and in doc comments that cite spec numbers. That is a gap on its
own terms, and it is the reason this ADR exists at all.

Two things then became clear.

**The semantics are already separable.** Roughly 2,550 lines — the token coordinator, state mutator,
element families, all five behavior files, the contracts, and almost all of `BpmnGraph` — import
nothing but `Bpmn.Models`. `BpmnGraph` touches Elsa on exactly three lines. The coupling is
concentrated in `BpmnScheduler` (64 lines) and `BpmnStatePersister` (89 lines), with
`BpmnExecutionEngine` threading runtime types through its own signatures.

**Elsa 3 has no BPMN at all, and the demand is long-running.** `grep -ri bpmn` across elsa-core's
`src/` returns nothing. [elsa-core#39](https://github.com/elsa-workflows/elsa-core/issues/39) has been
open since 2019 with 27 comments, last bumped July 2026, and the request recurs across at least four
issues and five discussions.

That second point is what makes this a repo decision rather than a packaging one. Elsa 3 and Elsa 4
have genuinely different execution models and independent release trains. A shared BPMN
implementation living inside the Elsa 4 monorepo would couple Elsa 3's releases to Elsa 4's
permanently.

It is worth being precise about what does *not* justify the move. Architectural hygiene alone does
not: `DesignPersistenceBoundaryTests` already proves this repository can enforce a negative dependency
in CI, at both the csproj graph and the restored assets level, with the code sitting exactly where it
is. A clean host port and a neutral core are achievable in-repo. Serving two majors from one codebase
is not.

## Decision

Extract the BPMN semantics core and the XML interchange layer into a standalone, host-agnostic,
MIT-licensed library at **`github.com/valence-works/bpmn`**, published as four packages with zero
external NuGet dependencies: `Bpmn.Model`, `Bpmn.Interchange`, `Bpmn.Semantics`,
`Bpmn.Runtime.InMemory`.

The original sequence named Elsa 4 as the first consumer and deferred Elsa 3. By the October
[#2735 adoption decision](https://github.com/elsa-workflows/elsa-foundation/issues/2735), Elsa 3 had
already adopted the shared library. This acceptance records Elsa 4 adoption; it does not qualify
Elsa 3 against the Foundation host evidence.

The library's own decision records live in that repository. This ADR records the decision *for this
repository*: what leaves, what stays, and what Elsa 4 takes on as a result.

### What stays here

`ActivitiesBpmnFeature`, `ActivitiesBpmnInterchangeFeature`, `BpmnInterchangeEndpoints`,
`BpmnProcess`, `BpmnDecision`, `BpmnStructureHandler`, the trigger and recurring-schedule providers,
and the authored-structure envelope.

`BpmnScheduler` becomes a command applier translating the library's three commands onto
`ScheduleChildActivity`, `RequestChildSubtreeCancellation`, and `RequestParentNotification`, and
reading the fault disposition to resolve incidents. `BpmnStatePersister` keeps `LoadState` and
`StageState`; only `PruneForPersistence` moves, because deciding which tokens can never influence a
future decision is BPMN knowledge rather than persistence knowledge.

A new binder converts the library's `BpmnWorkBinding` declarations into `ActivityNode`s bound to
`Elsa.Delay`, `Elsa.Event`, `Elsa.PublishEvent`, and `Elsa.DispatchWorkflow`.

This split also keeps the library genuinely standalone: `CShells.Abstractions` and
`Elsa.Platform.PackageManifest.Generator` are used only by the feature-registration files, which stay
here. If the neutral packages ever need either, the extraction is not finished.

### The host port

The library returns commands rather than calling back. Elsa 4 stages teardown and flushes it on a
non-fault continuation; Elsa 3 cancels eagerly and recursively by walking its context tree. Both are
legitimate, and a callback interface would have baked Elsa 4's timing into the library. This is the
clearest evidence that the neutrality is real rather than asserted, and it is recorded in the
library's ADR 0002.

## Consequences recorded at proposal

**Two release trains, and a third repository in the coordination set.** `elsa-foundation-studio`
carries a hand-maintained TypeScript mirror of the BPMN payload at
`src/Elsa.Studio.Workflows/Client/src/bpmn/bpmnTypes.ts`. A payload change breaks BPMN authoring
unless Studio ships in step. The library publishes a JSON Schema so Studio can generate rather than
mirror, but the lockstep remains real and should be planned for, not discovered.

**Cross-repo PRs for BPMN behavior changes,** and loss of atomic refactoring across the seam. A
sibling clone with a gitignored MSBuild property swapping package references for project references
covers local debugging; committed state always consumes a published preview.

**Roughly 65 percent of the BPMN test mass stays here.** Everything running through
`WorkflowExecutionHarness` is testing the integration, not the semantics. That is the right split, and
it is also a useful signal about where this code's complexity actually lives.

**NuGet identity changes.** `Elsa.Activities.Bpmn` and `Elsa.Activities.Bpmn.Interchange` are
published only as `4.0.0-preview.N` with one consumer, so framework §2.16's preserve-identity guidance
is satisfied at negligible cost now and would be expensive later. This is the window.

**Constitution amendments required**: §E2.1's domain table and §E2.4's foundation-repo composition.
Note also that framework §2.15 marks the multi-repo preference as an explicit *"strong preference, not
yet ratified"* — this decision executes ahead of that ratification, deliberately, and says so.

## Alternatives considered

**Keep BPMN here and add an architecture guard.** Genuinely strong, and it delivers most of the
architectural benefit at none of the coordination cost. Rejected only because it cannot serve Elsa 3.
If the Elsa 3 consumer never materializes, this was the better option, which is why the sequencing
places every in-place refactor before anything irreversible.

**Split the packages but not the repository.** Same objection: a package built from this repository
still belongs to this repository's release train.

**Move to `elsa-workflows` rather than `valence-works`.** Rejected. The org name appears in every clone
URL, badge, and package project URL, which would make the most prominent reference in a host-agnostic
project a reference to a specific host. Valence Works already hosts CShells, Groundwork, and Nuplane
under exactly this arrangement.

## Original verification plan

This was the proposal's extraction gate. The October direction in
[#2735](https://github.com/elsa-workflows/elsa-foundation/issues/2735) records the existing Elsa 3
consumer and orders Foundation parity, adoption and ADR acceptance. The delivered verification below
qualifies Foundation; it does not claim a newly executed Elsa 3 spike or full Elsa 3 qualification.

The pre-extraction baseline is committed at `docs/reports/evidence/bpmn-extraction-baseline/`: 247 and
107 sorted test names, both suites green, plus the frozen `BpmnProcess` and `BpmnDecision` contract
fingerprint, which must not change at any point during the program.

The gate on the neutrality claim is a throwaway spike implementing the host port against Elsa 3's
`ActivityExecutionContext` and running several BPMN processes green. Never merged, never released. If
the spike cannot express two or more commands, this decision is wrong and the in-place seam is the
outcome to keep.

## Delivered Foundation adoption

Acceptance follows parity [PR #3335](https://github.com/elsa-workflows/elsa-foundation/pull/3335),
adoption [PR #3338](https://github.com/elsa-workflows/elsa-foundation/pull/3338), and resulting-main
verification at **438001406d980051584234e5d656eb8e6a67f47f** ([delivery receipt](https://github.com/elsa-workflows/elsa-foundation/pull/3338#issuecomment-6460348332)).
The original extraction counts, paths and constitution references above describe the proposal;
this acceptance makes no new constitution amendment or ratification claim.

Foundation consumes released `Bpmn.Model`, `Bpmn.Semantics` and `Bpmn.Interchange` **0.5.0**.
The [parity report and cut ledger](../reports/evidence/bpmn-adoption-0.5.0/cut-ledger.md)
distinguish the selected neutral profile from actual Foundation binding support.

The delivered adapter uses `BpmnProcessRuntime`, `BpmnStateCodec` and `BpmnWorkLedger` in
[`Elsa.Activities.Bpmn`](../../Essentials/src/Activities/Bpmn/Elsa.Activities.Bpmn/README.md).
They replace the scheduler/persister implementation names anticipated above: the runtime applies
library commands through normal Foundation scheduling, cancellation, notifications and checkpoint
authority; the codec and ledger retain supported stored state and exact invocation identities.
The library owns semantic evaluation and pruning. Foundation's duplicate engine, behavior registry,
semantic state DTOs and pruning policy are deleted, with collision and retired-surface guards.
The registered [interchange binder](../../Essentials/src/Activities/Bpmn/Interchange/README.md)
provides supported Delay, Event, PublishEvent, waited DispatchWorkflow and embedded-process bindings.

The frozen `BpmnProcess`/`BpmnDecision` authoring fingerprint remains
`41aa0bb979ea46fc2533b057e3677f408ab54c3411fecfafaa238a2adae38c92`.
The authored envelope remains `elsa.bpmn.structure` 1.0.0. Qualified complete old version-1
checkpoints continue with their version and original wire names; new executions use version 2.
Ambiguous or unsupported persisted layouts refuse dispatch without replacing the stored document.
The [compatibility contract](../../specs/1006-bpmn-library-adoption/contracts/compatibility.md)
records the exact qualified profiles and limits, including rollback limits after pruning.

Verification includes actual checkpoints captured before deletion of the old writer, upgraded-host
continuation, native-composite integration, called error/escalation recovery, retry-safe broadcast,
and normal Studio import/edit/publish/run/reload/export. The
[implementation task ledger](../../specs/1006-bpmn-library-adoption/tasks.md) links each source-bound
receipt; the [latest pre-merge host/Studio qualification](../../specs/1006-bpmn-library-adoption/evidence/round3-fixed-host-qualification.md)
and [review round 5](https://github.com/elsa-workflows/elsa-foundation/pull/3338#issuecomment-6459660974)
keep their own revision scopes. Resulting-main verification is linked above separately.

This accepts the shared-library integration delivered by #2735. It does not close
[#2898](https://github.com/elsa-workflows/elsa-foundation/issues/2898),
[#2907](https://github.com/elsa-workflows/elsa-foundation/issues/2907) or the
[#2892 program](https://github.com/elsa-workflows/elsa-foundation/issues/2892).
Their direct-hosting and production journeys need their own evidence. Arbitrary live migration,
Orleans qualification and the task-runner follow-up remain deferred.
