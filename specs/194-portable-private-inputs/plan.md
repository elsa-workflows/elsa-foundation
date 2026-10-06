# Implementation Plan: Portable compositions with required private inputs

**Branch**: `codex/2457-portable-input-contract` | **Date**: 2026-10-06 | **Spec**: [spec.md](spec.md)

**Status**: Draft — Proposed contract under #2457; no implementation-ready tasks/production changes.

## Summary

Wrap unchanged authored v1 in an opt-in portable envelope with required operator-supplied complete private configuration and private receipts. Reuse capture/import/planner/candidate/intended inspection. Fresh directories and explicit complete-input rebind provide the smallest destination coexistence rule. Missing/mismatched/drifted inputs refuse. Public tokens are independent of private bytes; fingerprints remain private.

## Technical Context

**Language/Version**: Existing C#/.NET tooling and target frameworks from the affected project/Directory.Build files; no SDK policy change.
**Primary Dependencies**: Existing System.CommandLine, System.Text.Json, Elsa.Modularity.Planning; no new external dependency.
**Storage**: Operator-owned local regular JSON and fresh artifact directories; no database/service.
**Testing**: Existing CLI/Planning fixtures, produced CLI terminal actor and selected actual Workbench inspection.
**Target Platform**: Existing CLI platforms including Windows/Unix regular-file behavior.
**Project Type**: CLI IO adapters and EF-free planning/bridge contracts.
**Performance Goals**: No performance claim/gate; finite admission and bounded inspection.
**Constraints**: No automatic secret transport, ambient/origin dependency, implicit merge, in-place activation, generic classifier or v1 semantic change.
**Scale/Scope**: Exactly one complete supported private bundle. Existing 32-file/1-MiB-per-file/8-MiB-per-collection opt-in bounds and inspection request limits.

## Constitution Check

Before research: framework §2.12 / Elsa §E4 remain deferred; no taxonomy ratification. Pure planner has no IO. Existing feature/package boundaries remain; CLI captures, Planning interprets supported bridge semantics. Framework §2.21.1 / Elsa §E1 preserve existing test subjects/objectives. No test removal/provider/cadence change. Intended/deployed evidence stays distinct.

After design: [research](research.md) and [contract](contracts/portable-input-v1.md) retain these gates. No new project, persistence participant, host source layer, generic framework or exception. Receipts are operator-owned integrity evidence, not signed ownership/host attestation. Concrete contract review is still required before implementation readiness.

## Project Structure

```text
specs/194-portable-private-inputs/
  spec.md
  plan.md
  research.md
  data-model.md
  quickstart.md
  contracts/portable-input-v1.md
  checklists/requirements.md
src/essentials/Modularity/Planning/{Bridge,Json,Models}/
src/essentials/Cli/
tests/essentials/{Modularity.Planning,Cli}/
```

Only documentation is changed now. Production paths name existing ownership seams, not committed implementation. Research links exact existing files. Preserve shared capture/public-projection helpers rather than clone command setup or fork selection rules.

## Delivery sequence

1. Review specification, trust boundary, exact wire/CLI/refusal contract and produced-actor acceptance plan as one checkpoint.
2. Resolve technical findings. Use `/speckit-tasks` only after contract review. Refine one native implementation child under #1962 with this prerequisite and preserve one active leaf.
3. Implement in existing owners with narrow fixtures/adverse controls. Complete exact-head review/CI/maps and post-merge evidence before claiming portability.

Authoring-only API split (#1961) remains separately required; read-only supporting audit adds no concurrent implementation lane. Real UX participants, production builder and activation/recovery remain open.

## Complexity Tracking

No constitutional exception requested. Wrapper/private receipts express missing configuration and accidental drift without public private-content fingerprints. Field-level target merge, arbitrary providers and cryptographic ownership infrastructure are excluded.
