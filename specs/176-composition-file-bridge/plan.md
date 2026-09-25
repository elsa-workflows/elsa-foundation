# Implementation Plan: Local composition file bridge

**Branch**: `codex/2013-file-bridge-plan` | **Date**: 2026-09-25 | **Spec**: [spec.md](spec.md)

**Input**: [Task #2013](https://github.com/elsa-workflows/elsa-foundation/issues/2013), the [file bridge v1 contract](contracts/file-bridge-v1.md), and the [source-boundary investigation](../../docs/reports/runtime-composition/import-export-boundary.md).

## Summary

Add a file-only path from an existing CShells host to a reviewed, pinned, no-profile authored composition, then from that document and an explicitly selected local host back to a fresh host-file candidate. A reusable bridge service owns parsing, provenance, safe projection, snapshot validation, and candidate patch decisions. The existing `SelectionPlanner` remains the only feature-selection/dependency engine. The CLI owns local file I/O, interactive review, redacted presentation, and atomic publication. Nothing starts a host or worker or reaches EF, a package feed, or a database.

The first implementation story should deliver the complete two-shell fixture flow, with Workbench source files as a separate compatibility sample. It must not silently treat the synthetic fixture as a Workbench example. [Research](research.md) explains source and interface choices; [data model](data-model.md), [CLI contract](contracts/cli-file-bridge-v1.md), and [quickstart](quickstart.md) make the slice reviewable.

## Technical Context

**Language/Version**: C# / .NET 10.

**Primary Dependencies**: Existing `System.Text.Json`, `System.CommandLine`, and `Elsa.Modularity.Planning`; no new runtime package dependency.

**Storage**: Explicit local CShells JSON files; an invocation-local in-memory source snapshot; one fresh authored JSON file or fresh generated directory. No database, durable source sidecar, or management save path.

**Testing**: Focused Planning and CLI test projects, including a real CLI-process fixture, redaction canaries, source-preservation comparisons, and refusal/cancellation cases. Run architecture guard and generated-map check. EF container suites are unrelated to this file-only slice and do not belong in its local or PR-specific matrix.

**Target Platform**: Cross-platform `dotnet elsa` CLI and reusable .NET planning library.

**Project Type**: Pure bridge logic plus CLI filesystem/interaction adapter.

**Performance Goals**: Deterministic preview and candidate generation for a normal CShells bundle; no host, network, package, or database latency. Do not add arbitrary timing gates for this first slice.

**Constraints**: No unreviewed value in portable intent or displayed output; no source mutation or existing-destination overwrite; no partial published output; exact selected-file precedence and source provenance; all copied files frozen and rechecked. Keep process environment, CLI overrides, package inventory, resource reachability, and runtime readiness explicitly unchecked.

**Scale/Scope**: One host directory, one selected shell/environment, base and at most one selected overlay for effective inspection, all supported sibling environment files copied. Two commands and the two-shell contract fixture; no web UI or in-place apply.

## Constitution Check

- Framework [§2.1–2.2](../../.specify/memory/constitution-framework.md): keep bridge parsing/projection in the existing Modularity planning boundary and filesystem/console behavior in CLI. Neither domain logic nor the worker gains a reverse dependency.
- Framework [§2.7](../../.specify/memory/constitution-framework.md): parsing of host-owned JSON is an adapter. The portable document and plan keep their existing meanings; no implicit configuration taxonomy is introduced.
- Framework [§2.19](../../.specify/memory/constitution-framework.md): exact feature IDs pass to the existing selection planner; file presence does not define a new activation or package policy.
- Framework [§2.21, §2.23](../../.specify/memory/constitution-framework.md): stable refusal codes, non-disclosing diagnostics, and tests at the parser, CLI, and file-publication boundaries are required. No exception messages or raw JSON excerpts enter diagnostics.
- Framework [§3 and §4.2](../../.specify/memory/constitution-framework.md): the existing authored v1 schema and planner semantics stay intact. Any new public bridge result is additive and versioned; no runtime change is claimed.
- Elsa [§E2](../../.specify/memory/constitution.md): the bridge remains in Modularity/CLI and does not move persistence or design responsibilities into another bounded context.
- [ADR 0076](../../docs/adr/0076-persistence-tooling-runs-inside-the-host-closure.md): EF tooling still runs in the host closure. The bridge never enters that path.
- Framework §2.12 and Elsa §E4 remain deferred. This plan does not ratify a general setting/resource precedence model.

No constitution exception is sought. The same gates were rechecked against the Phase 1 design: invocation-local source association, strict output boundary, sole planner authority, and fresh-file publication preserve them.

## Project Structure

```text
src/essentials/Modularity/Planning/
  Models/SelectionDocuments.cs       # existing authored v1 model; top level unchanged
  Services/SelectionPlanner.cs        # sole selection/dependency authority
  Bridge/                          # new pure CShells JSON/provenance/review/patch logic
src/essentials/Cli/
  ElsaCli.cs                        # composition import/generate registration
  CompositionPlanCommand.cs         # existing plan command and redacted conventions
  CompositionFileBridge*.cs          # file snapshot, interactive review, atomic publication
tests/essentials/Modularity/Planning/Tests/
  <bridge semantic and redaction tests>.cs
tests/essentials/Cli/Tests/
  <bridge process and filesystem tests>.cs
specs/176-composition-file-bridge/
  spec.md plan.md research.md data-model.md quickstart.md contracts/
```

**Structure decision**: The pure bridge seam is reusable by a later builder; CLI remains the only disk and prompt owner. The bridge must not grow a second selector or copy the management feature-save API. If the pure seam would require host runtime references during implementation, stop and revise the boundary before adding those references.

## Phase 0: Research

[research.md](research.md) records actual Workbench loading behavior, the reviewed-setting mechanism, source snapshot and publication rules, the CLI interaction choice, and alternatives deferred.

## Phase 1: Design and contracts

[data-model.md](data-model.md) defines the invocation-scoped source association, preview, review decision, accepted document, and generated candidate states. [contracts/cli-file-bridge-v1.md](contracts/cli-file-bridge-v1.md) pins invocation, refusal, and output behavior; [contracts/setting-review-v1.md](contracts/setting-review-v1.md) defines the optional, deny-by-default safe-setting input without duplicating the [semantic v1 contract](contracts/file-bridge-v1.md). [quickstart.md](quickstart.md) gives the executable acceptance flow and focused verification.

## Delivery and verification boundary

Split implementation only after this plan is reviewed. The first story must close the full preview/accept/generate fixture; a parser-only or pretty-printer story is insufficient. Gate it with both affected test projects, the two-shell canary and parsed-subtree comparisons, a changed-snapshot refusal, an architecture guard, generated-map check, and diff review. Run Workbench as a source-layout sample. Do not run EF container suites for file-only code unless a dependency or behavioral change crosses into EF; the main-branch broad gate can retain its own schedule. In-place apply and whole-bundle recovery remain [#1964](https://github.com/elsa-workflows/elsa-foundation/issues/1964).
