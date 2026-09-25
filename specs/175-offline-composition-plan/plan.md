# Implementation Plan: Offline composition plan command

**Branch**: `codex/2001-offline-composition-plan` | **Date**: 2026-09-25 | **Spec**: [spec.md](spec.md)

**Input**: [Story #2001](https://github.com/elsa-workflows/elsa-foundation/issues/2001), [command contract](../../docs/reports/runtime-composition/developer-plan-command-contract.md), and [selection-planner contract](../174-profile-selection-planner/contracts/selection-planner-v1.md).

## Summary

Expose the already-implemented pure selection planner through `dotnet elsa composition plan`. Keep the command in the CLI front end and read only explicit versioned files. Extend the shared plan result with source-tagged dependency evidence, add strict supplied-inventory and resource-hint readers, then project one safe deterministic result as text or JSON. Do not invoke the persistence worker, activate a host, or perform database work.

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: Existing `System.CommandLine`, `System.Text.Json`, and `Elsa.Modularity.Planning`. No new package dependency.

**Storage**: Read-only JSON files supplied by the caller; no state store or write path.

**Testing**: `Elsa.Modularity.Planning.Tests` and `Elsa.Cli.Tests` as whole projects; existing CLI real-process harness; architecture guard and generated maps check.

**Target Platform**: Cross-platform .NET tool (`dotnet elsa`).

**Project Type**: CLI adapter plus additive pure planning-library result.

**Performance Goals**: Deterministic in-memory planning for normal composition documents; no network, package loading, worker launch, or database latency.

**Constraints**: Preserve existing `persistence plan` behavior and ADR 0076 front-end/worker boundary. Treat file snapshots as supplied evidence only. No secret-bearing output, partial JSON, or implicit acceptance of candidate state.

**Scale/Scope**: One command, two existing projects, two focused test projects. No released profiles or export/import.

## Constitution Check

- Framework [§2.1 and §2.2](../../.specify/memory/constitution-framework.md): the CLI references the existing pure planning implementation; no new domain/module or prohibited reverse dependency.
- Framework [§2.23](../../.specify/memory/constitution-framework.md): planner behavior and CLI boundary are verified in their owning test projects; meaningful negative and no-side-effect cases accompany the behavior.
- Framework [§3](../../.specify/memory/constitution-framework.md): no Nuplane activation or runtime-hot-reload behavior is changed by an offline planner command.
- Framework [§4.2](../../.specify/memory/constitution-framework.md): adding dependency evidence to a public result is additive; retain existing selection and digest semantics. Review downstream callers/tests for source compatibility.
- Elsa [§E2](../../.specify/memory/constitution.md): planning remains in Modularity, not a runtime/design bounded context or EF provider project.
- [ADR 0076](../../docs/adr/0076-persistence-tooling-runs-inside-the-host-closure.md): existing persistence operations keep their worker and host-owned dependency closure. The new file-only command does not reinterpret EF tooling rules.

No constitution exception is sought. Recheck after implementation and source review.

## Project Structure

```text
src/essentials/Modularity/Planning/
  Models/SelectionPlan.cs                # additive dependency-evidence result
  Services/SelectionPlanner.cs           # reviewed explanations and shared result
  Services/HostAssessment.cs             # observed descriptor/manifest evidence
  Json/SelectionJsonReader.cs            # current catalog/authored/workspace readers
  Json/<new input reader>.cs              # strict inventory/resource-hint adapters
src/essentials/Cli/
  Elsa.Cli.csproj                         # direct planning reference
  ElsaCli.cs                              # register composition command only
  CompositionPlan*.cs                    # parse, redacted projection, rendering
  Program.cs                             # generic root help for parser errors
tests/essentials/Modularity/Planning/Tests/
  <focused dependency/input tests>.cs
tests/essentials/Cli/Tests/
  <focused command tests>.cs
specs/175-offline-composition-plan/
  spec.md plan.md research.md data-model.md quickstart.md tasks.md contracts/
```

**Structure decision**: Keep selection and evidence semantics in the shared planner, while the CLI owns file I/O and presentation. Neither the CLI nor the worker acquires EF packages.

## Phase 0: Research

[research.md](research.md) records CLI ownership, source/evidence boundaries, deterministic output and safety decisions, plus rejected alternatives.

## Phase 1: Design and contracts

[data-model.md](data-model.md) defines the additive evidence row and file adapters. [contracts/cli-v1.md](contracts/cli-v1.md) pins invocation, exit, and output behavior. [quickstart.md](quickstart.md) gives runnable positive and negative validation cases. The [command decision](../../docs/reports/runtime-composition/developer-plan-command-contract.md) remains the canonical rationale.

## Verification and review

Run the two affected test projects as whole suites, architecture guard using `tools/architecture/restore-ci-project-graph.sh`, `dotnet run --project tools/maps/Elsa.Maps.Generator -- all` and `-- check`, solution-filter freshness, and `git diff --check`. Exercise sentinel redaction and a dependency mutation that makes the affected test fail before restoration. Review the exact diff before PR, then use the program merge gate and verify main after merge. The existing CLI worker/persistence tests in CI guard unintended changes to `persistence plan`.
