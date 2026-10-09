# Implementation Plan: Shared Startup Runner Adoption

**Branch**: `2341-adopt-startup-runner` | **Date**: 2026-10-09 | **Spec**: [spec.md](spec.md)

**Status**: Reviewed plan; implementation in progress. Root and independent review cover the requirements, research and Phase1 artifacts, including explicit fatal/cancellation/external-completion clarifications and the no-notification projection correction. Task sequencing and implementation verification follow this checkpoint. Returned-generation prerequisite [CShells #167](https://github.com/valence-works/cshells/issues/167) is qualified from public `.173`.

## Summary

Use the existing opt-in root `IShellActivationRunner` inside the three existing host adapters. Keep Foundation's ordered pre-listen first pass and independent retries, Workbench's ordered one-shot eager pass, and its separate post-ApplicationStarted warmup. Elsa owns refusal policy, phase telemetry and sanitized projections; the runner owns attempts, timers and cancellation/join lifetime. See [research](research.md) for remaining design verification.

## Technical Context

**Language/Version**: Existing C#/.NET10 host applications; upstream abstractions support .NET8/9/10.
**Primary Dependencies**: CShells hosting/lifecycle abstractions, standard DI/logging/options, existing host TimeProvider and Elsa diagnostic types. No new third-party package or feature closure.
**Prepared-package baseline**: This descendant currently carries `.171`/`.99` preview pins from `af83b06`; source qualification must coherently update its reached CShells family to qualified public `.173` for ReturnedGeneration and refresh reached locks/maps. Canonical main remains `.159`/`.94`. Final adoption still replaces previews with the complete verified stable families.
**Storage**: No new store; only owned activation handles and bounded host policy metadata.
**Testing**: Existing Modularity startup/readiness tests, actual Cluster/SQLite recovery/boot/refusal processes, affected Workbench composition tests and architecture/maps. Preserve all existing test objectives.
**Target Platform**: Supported host platforms and current CI matrix.
**Project Type**: Refactoring of two existing deployable hosts; no new shared host library.
**Performance Goals**: No performance measurement or benchmark gate; owner retired that program.
**Constraints**: No settled-readiness choice by inference; no adoption-source merge before final stable families and actual host proof. Heavy builds remain serialized through the machine wrapper.
**Scale/Scope**: Three startup adapters and local diagnostic projection/DI wiring. #2354/#2164/#2362, #2128, pruning and readiness repair remain separate.

## Constitution Check

- Framework §2.5.1: the root singleton runner has a specific lifetime reason: it owns independently tracked activation runs that begin before any shell exists and survive a caller-bounded shutdown wait. No scoped feature logic is promoted to singleton.
- Framework §2.7/§2.16: generic execution stays upstream; Elsa refusal/Attention/telemetry policy remains local. No new project or feature dependency is justified.
- Framework §2.21.1 and Elsa §E1: preserve existing subjects/objectives/assertions; rewire fixtures through the actual public runner. Do not replace host-policy tests with mocked-runner-only tests.
- Framework §2.23: verify each changed logic-bearing adapter and root registration through real DI and deterministic branches, plus real process recovery.
- Test cadence is undeclared by the derived application constitution; record the gap without inventing mandatory test-first ordering. Existing refactor assertions remain mandatory.
- Draft framework §2.24 and Elsa §E2.9 are not used as ratified new gates.

**Pre-design**: scope and ownership are consistent. **Post-design**: passed root and independent source/design review. Research explicitly selects configured-order concurrent fatal propagation, startup-token detachment, terminal external boot completion and the failure-base projection. Existing test subjects/assertions remain mandatory; no new project, constitutional exception or readiness policy is introduced. This is design acceptance, not adapter execution proof.

## Project Structure

### Documentation

`specs/200-adopt-startup-runner/`: specification, plan, [research](research.md), [data model](data-model.md), [startup contracts](contracts/startup-profiles.md) and [validation guide](quickstart.md). Tasks follow the reviewed design. The official setup-plan script resolved the spec through `.specify/feature.json`; its empty branch output is a script metadata limitation, while actual Git branch is `2341-adopt-startup-runner`.

### Source Code

- `src/apps/Elsa.Foundation.Host/Shells/EagerShellActivationHostedService.cs`, `ShellActivationTracker.cs`, existing retry options and `Program.cs`.
- `src/apps/Elsa.Workbench/Boot/EagerShellActivationHostedService.cs`, `Readiness/DefaultShellWarmup.cs`, existing readiness state and `Program.cs`.
- `tests/essentials/Modularity/Tests/FoundationHostEagerActivationTests.cs`, `EagerShellActivationTests.cs`, `ShellReadinessTests.cs`, `HostOwnedServicesAreSharedWithShellsTests.cs` and shared fixtures where useful.
- `tests/essentials/Cluster/EntityFrameworkCore/Tests/FoundationHostEagerActivationRetryTests.cs`, relevant boot/refusal/Workbench process tests; affected architecture/maps.
- Stable adoption updates `Directory.Packages.props` and reached lockfiles with complete observer/startup source and maps.

**Structure Decision**: retain three adapters because their phases and policies differ. Extract helpers only for real repeated setup or safe outcome projection; do not add an Elsa scheduler.

## Validation Sequence

1. Preserve the independently reviewed requirements/research and qualified returned-generation `.173` evidence. Review completed design artifacts before consumers implement them.
2. Create reviewed contracts, validation guide and dependency-ordered tasks. Preserve clean observer candidate `af83b06bb`; this worktree is a separate descendant.
3. Implement only reviewed adapter scope with one runner execution path, keeping readiness compatibility pending its separate owner choice.
4. Run deterministic phase/retry/refusal/fatal/custom-identity/overlap/shutdown regressions and compiled behavioral mutations followed by exact restoration.
5. Rebuild actual hosts and run relevant process recovery/boot/refusal, architecture/maps serially. Record exact preview inputs/package identities, separately from stable acceptance.
6. After stable publication, combine reviewed source with coherent released pins/locks/maps, clean-cache locked restore, actual host/backend E2E and resulting-main gates before closure.

## Complexity Tracking

No constitutional exception is approved. Fatal-error retention remains exceptional propagation state, not duplicate attempt/scheduling state. Explicit compatibility clarifications in FR016–FR020 must be documented and tested; existing assertions remain effective. Root and independent design reviews are complete; tasks and executable acceptance remain outstanding.
