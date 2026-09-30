# Implementation Plan: Effective persistence preview

**Branch**: `1307-effective-persistence-preview` | **Date**: 2026-09-30 | **Spec**: [spec.md](spec.md)

**Input**: Issue2175 specification and delivered2172 source evidence. This PR supplies an Approved implementation contract after review, not production behavior.

## Summary

Deliver one explicit composition inspect command over an accepted post-edit in-memory candidate. Reuse shared selection/candidate builders, isolate host execution in the existing EF-free worker, add an independently versioned host capability, and use the real host's shell defaults/dependency closure and shared EF preparation with configured-value verification. Publish one safe configurationResolution projection alongside the unchanged unchecked selection plan. Include explicit removal materialization even for IDs absent from original file selection. No second configuration engine, new suite or temporary candidate publication.

## Technical Context

**Language/Version**: C# / .NET10, current repository pins; no SDK or package upgrade.
**Primary Dependencies**: Existing System.Text.Json, Microsoft.Extensions.Configuration JSON streams, CShells declared composer/descriptor/dependency APIs inside host only, installed Nuplane loader in worker.
**Storage**: Read-only local captured configuration/intent files; private memory/stdin streams; no persistence writes.
**Testing**: Existing xUnit Planning, CLI and EF migrations projects; actual child worker and host-produced serialized response, runtime preparer comparison, adverse/mutation controls and architecture/maps.
**Target Platform**: Existing framework-dependent .NET host outputs on macOS/Linux/Windows; no single-file/self-contained expansion.
**Project Type**: Existing CLI + host-owned inspection boundary.
**Performance Goals**: No latency improvement claim; finite60s default operation, bounded buffers, bounded cleanup.
**Constraints**: [Protocol](contracts/candidate-inspection-v1.md) limits/trust/redaction; legacy v1/v2 unchanged; no package acquisition/DB/activation/save; no new EF reference in frontend/worker.
**Scale/Scope**: One shell/environment/candidate, installed closure, existing enrolled EF consumers, file-only inputs. Broader provenance, external capture and portable unknown export remain #1962.

## Constitution Check

Pre-research and post-design checks:

- Framework §2.1: shared Modularity.Planning stays EF-free; host Persistence owns EF resolution. No unrelated implementation-to-implementation reference or new project.
- Framework §2.11: real descriptor graph reused; inspection refuses mismatched accepted/required closure without changing runtime's Auto-Resolve mode or inventing a second graph.
- Framework §2.12 and Elsa §E4: deferred; no global settings policy ratified.
- Elsa §E2.2: no Runtime→Design dependency added; no deployment-shape change.
- Framework §2.21.1 / Elsa §E1: all existing test objectives retained; no deletions or cadence changes.
- Framework §2.23.2: every new/changed logic-bearing implementation has its own meaningful public-surface unit tests with stubbed dependencies, covering conditional/default/failure branches; actual producer/consumer integration remains separate evidence. Spec Kit tasks template requires included tests to fail before implementation. No new feature class is introduced, so §2.23.1 feature-registration obligations remain with existing features rather than inventing a registration class.
- Framework §4.1/4.2: new public host operation/capability is an additive compatible surface expansion, requiring owning-package compatibility/version classification in the normal release pipeline; no manual package version bump or change to old signatures/protocol semantics is prescribed.
- Framework §2.22: update `src/essentials/Cli/README.md`, `src/essentials/Persistence/EntityFramework/README.md` and `src/essentials/Persistence/EntityFramework/EXTENSION_POINTS.md` for the public operation. Maps regenerated/reviewed; plan/source evidence canonical links.
- Elsa §E6: proposed EfCandidateInspectionContract, CompositionInspectCommand and candidate process/projection types use concrete nouns within five components. Keep public payload shape independent from internal class naming.
- No constitutional exception proposed. Arbitrary composer trust is explicit, not an inferred sandbox.

## Project Structure

### Documentation (this feature)

```text
specs/187-effective-persistence-preview/
  spec.md, plan.md, research.md, data-model.md, quickstart.md, tasks.md
  contracts/cli-inspect-v1.md
  contracts/candidate-inspection-v1.md
  contracts/acceptance-proof-matrix.md
```

### Source Code (repository root)

- `src/essentials/Modularity/Planning/Bridge/CompositionCandidateBuilder.cs`: materialize safe explicit Remove declarations, including file-absent IDs, preserving shape/settings-loss refusals.
- `src/essentials/Cli/CompositionInspectCommand.cs` (new), existing `ElsaCli.cs`, `CompositionFileSource.cs`, `CompositionInputSnapshot.cs`, `CompositionFileReader.cs` and `CompositionInspectionCapture.cs`: command admission, bounded capture, one builder invocation, drift checks, safe plan/projection output. Exact parsing registration follows current ElsaCli owner.
- `src/essentials/Cli/WorkerProcess.cs`: share launch arguments; candidate-specific bounded process scope includes every stage and safe console handling, no legacy stderr changes.
- `src/essentials/Cli/Worker/WorkerContract.cs`, `Program.cs`, `WorkerRunner.cs`, `ToolingEntryPoint.cs`: additive inspect-candidate request/capability dispatch, strict fields and safe failures; installed closure loader reused; Restore forbidden.
- `src/essentials/Persistence/EntityFramework/Tooling/EfCandidateInspectionContract.cs` and `EfCandidateInspectionOperation.cs` (new), `EfToolingHost.cs`, narrow shared composition extraction from `EfToolingConfigurationContext.cs`: streamed candidate factory, actual defaults/graph reconciliation and shared preparation true, closed target projection. Existing v2 false stays unchanged.
- Existing tests: `tests/essentials/Modularity/Planning/Tests`, `tests/essentials/Cli/Tests` and existing built host fixtures, `tests/essentials/Persistence/EntityFrameworkCore/Migrations/Tests`. Reuse real enrolled Runtime/diagnostics assemblies and #2172 probe helpers where appropriate. Fake producer tests only for unsupported/adverse transport, not parity proof.

The capture owner and the worker independently validate selection through the same finite identity predicate. This private transport helper is public because the frontend and worker are separate assemblies (`Elsa.Cli` excludes `Worker/**` and references the nonpackable worker project); no new friend assembly or duplicated grammar is introduced. New parser/capture tests call public surfaces directly.

**Structure Decision**: One coherent implementation leaf/PR for candidate generation adjustment, host operation and CLI consumption. They are one actor journey; separate producer-only and consumer-only issues would leave an unusable partial capability. Bounded code subtasks may run in parallel with root integration; one active delivery leaf and merge lane remain.

## Implementation sequence

1. Refresh/claim eligible implementation issue, load accepted spec and recheck competing work. Define strict request/response vectors and reuse current test fixtures.
2. The capture owner keeps source/input snapshots private, calls the shared builder once and constructs immutable selected-layer payloads without accepting external candidate/capture pairs. Correct shared removal materialization and prove real host merge; establish one immutable captured-candidate object and actual bounded readers and shared fail-closed regular-file preflight (FIFO/device included), not file-size preflight followed by unbounded reads.
3. Implement host version1 operation with actual composer/descriptor graph, compare all sets before success, call shared EF preparation true and emit content-validated scopes/targets. Legacy rows explicitly unprojected. Preserve offline v2 behavior.
4. Add worker capability dispatch, same-package additive private envelope fields, isolated bounded process lifecycle and raw console suppression. Reject forbidden live flags and package acquisition; installed closure observations stay separate from candidate byte identity.
5. Implement command and safe human/JSON consumer; validate every returned field and private correlation before presentation. Recheck complete input set immediately before emission. Never promote Planning.PersistenceEvidence to checked.
6. Exercise the actual serialized host producer through worker/CLI and both actor starting paths; cover [all matrix rows](contracts/acceptance-proof-matrix.md). Root reviews integrated delta and meaningful mutation controls before final gates.

## Validation and delivery

Start with focused new candidate/removal/configuration/protocol cases; build affected projects through normal wrapper. On final head run full affected Planning, CLI and migrations suites once, plus architecture guard and maps all/check. Commands in [quickstart](quickstart.md) are planned, not results. Root checks actual discovered tests/fixture applicability and distinguishes unavailable native/environment legs from executed evidence. No added provider container suite is justified by this configuration-only operation.

Specification PR: source/doc/link/contract/checklist review, maps refresh/check and architecture guard, applicable exact-head CI/reviews and exact-main CI/Maps. No behavioral mutation test is claimed for documentation; implementation matrix reserves them for real changes. Status Approved means contract approved, not implemented. Mark Implemented in the eventual implementation PR only after complete actor/configuration/adverse evidence. External review unavailable fallback follows program lead policy, never approval invention.

## Complexity Tracking

No exception, new project or duplicate resolver required. New private candidate wire shape is distinct from old tooling semantics by necessity; shared composition/resolution/launch helpers avoid duplicate owners. Keep existing dynamic configuration stream lifetime alive until ConfigurationBuilder.Build completes; do not adopt the disproved per-loop early-disposal suggestion.
