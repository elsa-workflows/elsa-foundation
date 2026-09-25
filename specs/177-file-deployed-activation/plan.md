# Implementation Plan: File-deployed composition activation and recovery

**Branch**: `codex/2036-file-deployed-activation` | **Date**: 2026-09-25 | **Spec**: [spec.md](spec.md)

**Input**: [Issue #2036](https://github.com/elsa-workflows/elsa-foundation/issues/2036), the [apply/recovery boundary](../../docs/reports/runtime-composition/apply-recovery-boundary.md), and the delivered [file bridge](../176-composition-file-bridge/spec.md).

## Summary

Define an honest path from a reviewed local composition candidate to an externally deployed default-shell activation. The first implementation-sized slice adds a safe, opaque handoff at the file bridge's publication boundary and rechecks all included files; an existing deployment owner remains responsible for artifact integrity, complete-bundle switching, and source rollback. A later host-observation slice reads authenticated reload, active generation, and default-shell readiness as separate outcomes. It must report candidate match **unverified** until a host-produced, generation-bound source marker is proven. Do not extend the legacy feature-only editor or claim an atomic in-process bundle write.

[Research](research.md) records the source and security findings. The [data model](data-model.md), [v1 contract](contracts/activation-v1.md), and [quickstart](quickstart.md) define safe states and test fixtures. The first implementation issue may cover candidate handoff only; a focused host-attestation spike gates any implementation that would say the exact candidate became active.

## Technical Context

**Language/Version**: C# / .NET 10 for the existing CLI, Modularity planning library, and Workbench host.

**Primary Dependencies**: Existing `System.Text.Json`, `System.CommandLine`, CShells `0.0.30-preview.159` management/lifecycle APIs, and the file bridge from spec 176. No new runtime package or database dependency is required for the first slice.

**Storage**: The file bridge's fresh local candidate directory and invocation-local source snapshot. V1 does not add a Foundation-managed live store. An external deployment owner supplies immutable artifact and previous-release storage; its receipt is not treated as host-source truth.

**Testing**: Focused Planning/CLI source and handoff tests with changed selected/unselected files and redaction canaries. Existing Modularity readiness/fault tests prove lifecycle boundaries. A disposable rebuilt Workbench host and test deployment owner are required before the host-attestation or recovery implementation claims verified activation. Architecture guard, generated-map check and diff review gate each implementation PR. EF container suites apply only if a later story actually changes EF behavior or claims database/migration readiness.

**Target Platform**: Cross-platform developer CLI and a Workbench-style ASP.NET Core host. The first verified-activation target is the configured default shell in one explicit environment.

**Project Type**: Pure handoff projection plus CLI local-file adapter first; host-owned, authenticated default-shell observation after the #2039 attestation decision. No browser client or generic resource editor.

**Performance Goals**: No new latency target is justified for a specification/file handoff. The operation remains bounded by reading the same supported local bundle as the existing generator. Host reload retains its existing lifecycle timing and is not a performance claim in this work unit.

**Constraints**: No raw source values, physical paths, management credential, unkeyed secret-bearing file digest, or raw blueprint response in shareable output. All copied files rechecked at handoff. No blind retry after uncertain deployment/reload. No candidate-match claim without generation-bound host evidence. Process overrides and loaded packages remain separate from static file inspection.

**Scale/Scope**: One Workbench-style host, configured default shell, base plus at most one selected overlay, all supported sibling files copied; one external deployment owner. Multi-shell transactions, arbitrary hosts, live resource writes, Foundation-owned bundle switching, database movement, and production builder UI remain outside v1.

## Constitution Check

- Framework [§2.1–2.2](../../.specify/memory/constitution-framework.md): keep pure candidate identity/projection with the existing Modularity planning boundary and filesystem/console work in CLI. Host-control observation belongs to the Workbench root, not a reverse dependency from Core or a shell feature.
- Framework [§2.7](../../.specify/memory/constitution-framework.md): the bridge and host observer are adapters around externally owned source/deployment/lifecycle APIs; they do not redefine the authored selection model or infer package activation from files.
- Framework [§2.19](../../.specify/memory/constitution-framework.md): exact feature IDs and dependency findings remain with the existing planner. Handoff identity is not a second selection policy.
- Framework [§2.21, §2.23](../../.specify/memory/constitution-framework.md): stable safe refusal classes, canary redaction tests, and a real host failure/recovery fixture are required before outward success claims.
- Framework [§3 and §4.2](../../.specify/memory/constitution-framework.md): do not assume an implementation package or process override can be hot-reloaded into a changed host baseline; package/host compatibility is a separate evidence gate.
- Elsa [§E2, §E5](../../.specify/memory/constitution.md): Workbench is a development/demo host, not a product host. This contract does not turn it into a general hosted builder API.
- [ADR 0037](../../docs/adr/0037-studio-management-bridge-keeps-host-management-key-server-side.md): root management credential remains server-side; a browser gets only a sanitized outcome after a separately designed bridge.
- Framework §2.12 and Elsa §E4 are deferred. This plan does not ratify a global settings/secret taxonomy or change configuration precedence.

No constitution exception is requested. Phase 1 design rechecks the same gates: an opaque handoff plus private all-file recheck avoids disclosure, the external deployer retains mutation ownership, and the host match remains explicitly unverified until a measured source-to-generation seam exists.

## Project Structure

```text
src/essentials/Modularity/Planning/
  Bridge/                           # existing source/candidate semantics and safe role projection
src/essentials/Cli/
  CompositionGenerateCommand.cs     # existing reviewed candidate flow; first handoff integration point
  CompositionFilePublisher.cs       # existing fresh-directory publication and all-file recheck
  CompositionHandoffFileVerifier.cs # prospective post-publication candidate-byte recheck
tests/essentials/Modularity/Planning/Tests/
  CompositionBridgeSourceTests.cs   # existing source and redaction fixture
  CompositionHandoffTests.cs        # first implementation slice
tests/essentials/Cli/Tests/
  CompositionGenerateCliTests.cs    # existing process fixture, extend for safe handoff and drift
src/apps/Elsa.Workbench/
  Program.cs                        # existing root management key and reload mapping
  Readiness/                         # existing default-shell generation/readiness observation
  Composition/CompositionGenerationMarker.cs # conditional on host-attestation proof
tests/essentials/Modularity/Tests/
  ServerReadinessTests.cs            # existing real-registry failed-reload proof
specs/177-file-deployed-activation/
  spec.md plan.md research.md data-model.md quickstart.md contracts/ tasks.md
```

**Structure decision**: The first code change stays in CLI/Planning and publishes no runtime-ready result. A future Workbench-owned observer needs its own reviewed design and must not proxy the existing management `GET /{name}` blueprint payload, which can contain secrets. Do not create a generic management library or extra EF test project for this boundary.

The accepted authored v1 schema remains the input. The safe handoff is a separate, optional CLI output requested with an explicit host alias; it adds no top-level authored field and carries no source bytes or private change token.

## Phase 0: Research

[research.md](research.md) resolves candidate/deployment authority, why shareable raw file digests are inappropriate for secret-bearing configuration, current CShells reload/readiness evidence, and the generation-bound attestation gap. The gap is a **gate**, not an implicit assumption that the existing APIs already prove candidate equality.

## Phase 1: Design and contracts

[data-model.md](data-model.md) separates handoff, external deployment receipt, reload, readiness, candidate match, and recovery. [contracts/activation-v1.md](contracts/activation-v1.md) defines safe projection, statuses, refusal classes, actor responsibility and the host-attestation gate. [quickstart.md](quickstart.md) distinguishes currently runnable baseline tests from future disposable host acceptance steps. The contract requires only the default shell because Workbench readiness currently observes that shell.

## Delivery and verification boundary

The file-only handoff shipped in #2038. The #2039 **host-attestation spike** found no generation-bound reviewed-bundle marker in the current path. #2041 therefore implements only a secret-safe default-shell reload/readback surface, with `candidateMatch=unverified`. Do not advertise `candidateMatch=verified` until a future marker proof passes; a server bridge and external deployment receipt remain later work.

The #2042 [source-to-generation follow-up](../../docs/reports/runtime-composition/source-generation-attestation.md) proves that a custom immutable CShells blueprint and activation participant are plausible generation seams, but records a no-go for verified matching in today's Workbench. The source, process-override and applied-package cohort must be captured by one host owner and compared privately with a trusted deployment receipt before T015 can start. The existing root observer is intentionally unchanged.

The #2046 [Workbench source-snapshot investigation](../../docs/reports/runtime-composition/workbench-source-snapshot.md) adds rebuilt-host evidence that a shell generation can advance while startup-bound root configuration remains old. Replacing the blueprint provider alone cannot make the current Workbench's live `ConfigureAllShells` callback or root service bindings part of one immutable candidate. Complete file/override matching therefore needs a shared host-start snapshot owner and an explicit restart boundary for changed root inputs before a Workbench-level generation marker can be considered. This remains a no-go for hot-reload verified matching; package integrity and deployer comparison are separate open gates.

The #2060 [restart-scoped source proof](../../docs/reports/runtime-composition/workbench-restart-snapshot.md) shows both sides of that boundary in built Workbench processes: a fresh process reads consistent CORS and shell settings from one retained six-file copy, while a file-at-a-time copy across a source change mixes revisions at startup. The external deployment owner must therefore publish an immutable complete artifact before process start; Workbench still has no private artifact/override receipt or package-cohort correlation. US2/US3 must distinguish restart from eligible shell-only reload, and their remaining tasks need that process-aware contract before implementation. `candidateMatch` stays `unverified`.

The local gate for the first story is focused Planning/CLI tests, architecture guard, generated-map check and diff review; the PR CI selector may skip unrelated EF container suites. #2041 needs rebuilt-host reload/readiness/failure and controlled timeout-readback tests, with no provider, migration, or package claim. After merge, check `main` CI and Maps before closing each issue.
