# ADR 0063: BPMN moves to a host-agnostic library

**Status**: Proposed
**Date**: 2026-08-09
**Deciders**: Sipke Schoorstra, Frans Nieuwenhuizen

## Context

BPMN Phases 1 through 3 are complete: sixteen specs merged, roughly 9,100 lines of source and 11,900
lines of tests under `src/Elsa/Activities/Bpmn/`. No ADR records why any of it is shaped the way it
is. The rationale lives in `specs/108-bpmn-container-activity/spec.md`, in
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

Elsa 4 becomes its first consumer. Elsa 3 is an intended second consumer, deferred.

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

## Consequences

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

## Verification

The pre-extraction baseline is committed at `docs/reports/evidence/bpmn-extraction-baseline/`: 247 and
107 sorted test names, both suites green, plus the frozen `BpmnProcess` and `BpmnDecision` contract
fingerprint, which must not change at any point during the program.

The gate on the neutrality claim is a throwaway spike implementing the host port against Elsa 3's
`ActivityExecutionContext` and running several BPMN processes green. Never merged, never released. If
the spike cannot express two or more commands, this decision is wrong and the in-place seam is the
outcome to keep.
