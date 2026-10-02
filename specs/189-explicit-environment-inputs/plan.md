# Implementation Plan: Explicit Private Environment Inputs

**Branch**: `codex/2292-explicit-environment-inputs`

**Date**: 2026-10-01

**Spec**: [spec.md](./spec.md)

**Owning program bucket**: runtime composition, #1959 / #1962; authoring #2277; implementation #2292

**Planning status**: Plan and 46 unchecked implementation tasks passed root and independent review on 2026-10-01. Authoring delivery gates passed after causal correction #2282; implementation is authorized under #2292. This plan contains no implementation claim and no executed-test claim.

## Summary

The feature adds an opt-in `composition inspect --environment-input <path>` lane for one explicitly supplied private environment overlay. The CLI captures the regular file once, binds it to the accepted candidate and selected host context, and sends it through a new private `inspect-candidate-environment` capability. The selected host runs the existing composition, selection reconciliation, and EF preparation policy inside its trusted owned worker. Existing candidate-v1/file-only requests retain their closed shape and behavior.

The first lane is enrolled only by an explicit declaration on the reviewed Workbench host assembly. It applies entries at the supported Workbench environment stage, refuses ambient, custom, and command-line sources, refuses all eleven standard service-connection prefixes, and publishes only safe logical results and evidence states. The implementation must not start a host, activate services, access a database, apply migrations, publish, or save application state.

## Technical Context

**Language/Version**: C# on the repository's pinned .NET 10 toolchain.

**Primary Dependencies**: Existing Elsa CLI worker/host closure, `Microsoft.Extensions.Configuration` JSON streams, CShells composition and selection resolver, `EfPersistencePreparation`, Nuplane installed-closure loader, and the existing Workbench host.

**Storage**: No new storage. The operator-owned private JSON file is read and rechecked in place; no additional private copy or generated artifact is persisted.

**Testing**: Existing xUnit projects and fixtures only. The authoritative cross-process path uses the public `Elsa.Cli` wrapper against an installed host closure. The actual Workbench build output is required for the Workbench-enrollment proof; a fixture host alone is insufficient. Real database/provider acceptance remains a separate persistence proof.

**Target Platform**: Existing supported .NET CLI/host platforms. The worker's current process-group/job cleanup and bounded stream behavior remain the lifecycle boundary.

**Project Type**: Additive CLI/worker/host-library capability with a reviewed Workbench enrollment declaration.

**Performance Goals**: Preserve existing bounded transport: 8 MiB serialized request, 4 MiB response, 1 MiB raw environment document, 1,024 entries, 1,024 UTF-8 bytes per raw or normalized key, 65,536 UTF-8 bytes per value, JSON depth 64, a combined participant-plus-finding projection limit of 1,024, and existing 1 MiB-per-file/4 MiB selected-file limits. Existing timeout range remains 1–300 seconds with default 60 seconds.

**Constraints**: One private capture; immutable owned bytes and drift checks; no values, raw configuration, private-input paths, or private-input fingerprints in child arguments, logs, diagnostics, public output, or generated artifacts. Existing user-typed CLI locations and actual assembly-loader/deps/package-root paths may remain as finite metadata for regular-file checks and host loading. No ambient or command-line import; candidate-v1 compatibility; explicit host enrollment; no general sandbox promise; no runtime activation or database effects.

**Scale/Scope**: One invocation, one accepted candidate, one selected host/shell/environment binding, and one explicit overlay. No new provider, project, cadence, general configuration taxonomy, service-prefix expansion, or portable export.

## Constitution Check

*GATE: reviewed before Phase 1 and rechecked after the design below.*

| Gate | Result and design consequence |
|---|---|
| Framework §2.1, Core/Helper/Feature boundaries | Pass. The new public contract and host method are additive; EF/provider behavior remains inside the selected host closure. The worker does not gain an EF reference or duplicate the resolver. |
| Framework §2.11, dependency resolution | Pass. The host reuses the existing composer, CShells selection reconciliation, and `EfPersistencePreparation`; divergence and required-edge conflicts refuse before preparation. |
| Framework §2.12 and Elsa §E4, configuration classification | Pass with boundary. Both remain deferred; this plan defines only a supplied inspection overlay and does not ratify a general configuration taxonomy. |
| Framework §2.19, stable feature identity | Pass. Existing accepted/removed IDs and safe identity rules remain the candidate-v1 compatibility boundary. |
| Framework §2.21, testing | Pass. Existing tests and proof subjects remain; the matrix adds cases to existing projects and does not establish a new cadence or delete a subject. |
| Framework §4, package/module compatibility | Pass. The new capability is additive and independently versioned; package placement and final SemVer release action remain implementation/release review. |
| ADR 0076 D1/D4/D7 | Pass. Execution remains in the host closure, explicit source policy refuses unsupported provider disagreement/fallback, and private values stay outside arguments and evidence. |
| Provisional sections | No §2.24 or Elsa §E2.9 ratification is invoked. Any later reliance on those provisional sections requires the constitution-readiness route. |

**Post-design result**: Pass for planning. The contracts preserve old candidate-v1 and outer WorkerContract v2, make host enrollment explicit, use one host-owned resolver/preparer, and keep all unresolved implementation work within the documented bounded proof matrix.

## Ownership and implementation shape

The public CLI wrapper owns argument validation, source capture, regular-file checks, private byte ownership, candidate construction, and final source recheck. It does not interpret EF configuration or resolve targets.

The EF-free worker client owns the private process lifecycle, outer WorkerContract v2 validation, exact capability binding, bounded stdin/stdout exchange, cancellation, timeout, and cleanup. It carries no EF reference and never places private values in process arguments.

The selected host owns the version-1 inner envelope, enrollment declaration check, explicit overlay admission, Workbench source ordering, its existing configuration builder and `EfToolingConfigurationContext.ComposeShell`, descriptor/selection reconciliation, and the existing `EfPersistencePreparation` call. The frontend `CompositionCandidateBuilder` remains a CLI/Planning responsibility and is not duplicated in the host. It returns one lane-specific safe response. It does not start application services, create a DbContext, connect to a database, run migrations, or publish.

The new public attribute is declared on the reviewed Workbench host assembly:

```text
[assembly: EfCandidateEnvironmentInputs(1, "workbench-json-explicit-environment-v1")]
```

The shared EF assembly may carry the capability method, but method presence does not enroll Foundation Host or another host. The worker loads the actual host assembly through the inspection-only helper, resolves the selected persistence assembly from the installed closure, binds the new capability first, and then calls the metadata-only enrollment validator; absent, duplicate, or wrong declarations refuse.

Admission order is outer request/layout closure, actual host loading by `HostClosure.LoadHostAssemblyForInspection(string, string): Assembly` (name/location validation only), selected persistence-assembly resolution, exact capability binding, then actual host enrollment. Missing/partial/wrong capability returns `candidate-capability-unavailable` exit 3 with no legacy fallback; only a successfully bound new capability proceeds to enrollment, where `ToolingEntryPoint.ValidateCandidateEnvironmentEnrollment(Assembly hostAssembly, Assembly persistenceAssembly):void` performs metadata-only exact attribute identity, constructor, version, policy, named-argument, and cardinality checks. Missing/duplicate/wrong declaration returns `candidate-environment-host-unenrolled` exit 3. The existing public `void LoadHostAssembly(...)` signature remains unchanged for binary compatibility; the inspection helper may share its private loading implementation. The host repeats capability and enrollment metadata validation with its actual host and selected persistence assemblies immediately before configuration construction/composer creation.

## Design phases

1. Add the independent EF capability/version, the additive enrollment attribute, and lane-specific host request/response types while leaving candidate-v1 and the old response validator closed. Add the inspection-only host assembly helper without changing the existing public `LoadHostAssembly` signature.
2. Add explicit-input file capture and admission with the exact D11 grammar, collision rules, limits, capture binding, and drift checks. Reuse `CompositionInputSnapshot`/`CompositionInspectionCapture` ownership seams.
3. Add the private worker command and worker client branch. Audit every old command reader for an absent `environmentInput` field, including an explicit `null`; reuse the existing `CandidateWorkerProcess` bounded streams, trusted child lifecycle, process cleanup, and fixed local refusal mapping.
4. Add the host operation. Verify enrollment before configuration construction, apply the explicit overlay at the reviewed Workbench environment stage, reuse the host configuration builder/`EfToolingConfigurationContext.ComposeShell`, descriptor reconciler, and `EfPersistencePreparation`, and produce the lane-aware redacted projection. The new lane validator must admit a correlated unenrolled-host refusal with its fixed exit-3 code; it must not reuse an old `WriteError` helper whose invalid-request behavior is always exit 2.
5. Extend the existing public `composition inspect` command with the one file option. Preserve the old no-option file-only path and render the same validated safe projection in human and JSON modes.
6. Execute the proof matrix in existing CLI, planning, EF migration, architecture, and CLI acceptance projects. Include the built `src/apps/Elsa.Workbench/Elsa.Workbench.csproj` closure and the public CLI wrapper; retain existing fixture coverage as a companion and negative control.

## Project Structure

### Documentation

```text
specs/189-explicit-environment-inputs/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── environment-input-v1.md
│   ├── cli-inspect-environment-v1.md
│   ├── acceptance-proof-matrix.md
│   └── adr0076-extension.md
└── tasks.md                         # created only by speckit-tasks
```

### Planned source seams

```text
src/essentials/Cli/
├── CompositionInspectCommand.cs
├── CompositionInspectionCapture.cs
├── CompositionInputSnapshot.cs
├── CandidateWorkerProcess.cs
└── Worker/
    ├── HostClosure.cs
    ├── WorkerContract.cs
    ├── WorkerRunner.cs
    ├── ToolingEntryPoint.cs
    └── CandidateWorkerOperation.cs

src/essentials/Persistence/EntityFramework/Tooling/
├── EfCandidateInspectionContract.cs                 # candidate v1 remains closed
├── EfCandidateEnvironmentInputsAttribute.cs          # additive host enrollment declaration
├── EfCandidateEnvironmentInspectionContract.cs       # additive v1
├── EfToolingContract.cs                              # existing host contract remains closed
├── EfCandidateInspectionOperation.cs                 # old operation remains compatible
├── EfCandidateEnvironmentInspectionOperation.cs      # additive operation
└── EfToolingHost.cs                                  # additive public wrapper

src/apps/Elsa.Workbench/
├── WorkbenchEfToolingShellDefaults.cs                # additive assembly enrollment declaration
└── Program.cs                                      # existing reviewed source order
```

### Authoritative existing proof surfaces

```text
tests/essentials/Cli/Tests/
├── Elsa.Cli.Tests.csproj
├── DotnetElsa.cs                         # public cross-process wrapper
├── CandidateInspectionFixture.cs         # source/canary fixture
├── CandidateInspectionLifecycleTests.cs
├── CandidateInspectionOutputTests.cs
├── CandidateProcessOwnerTests.cs
├── CandidateProcessTests.cs
├── CandidateWorkerOperationTests.cs
├── CompositionAcceptCliTests.cs
├── CompositionInspectionCaptureTests.cs
├── ToolingEntryPointTests.cs
└── WorkerProtocolTests.cs

tests/essentials/Persistence/EntityFrameworkCore/Migrations/Tests/
├── Elsa.Persistence.EntityFrameworkCore.Migrations.Tests.csproj
├── EfCandidateInspectionTests.cs
└── EfToolingHostTests.cs

tests/essentials/Modularity/Planning/Tests/
├── Elsa.Modularity.Planning.Tests.csproj
├── CompositionCandidateTests.cs
└── SelectionExpansionTests.cs

tests/essentials/Architecture/
└── Elsa.Architecture.Tests.csproj

tests/essentials/Persistence/EntityFrameworkCore/CliAcceptance/ProviderTests/
└── Elsa.Persistence.EntityFrameworkCore.CliAcceptance.ProviderTests.csproj
```

`src/apps/Elsa.Workbench/Elsa.Workbench.csproj` and its built output are the installed actual Workbench closure required by the enrollment and public-wrapper proof. Fixture hosts remain useful for controlled refusal cases, but passing a fixture alone cannot establish Workbench enrollment or public CLI behavior.

## Validation handoff

The commands and expected observations are recorded in [quickstart.md](./quickstart.md). The complete requirement-to-case assignment is [contracts/acceptance-proof-matrix.md](./contracts/acceptance-proof-matrix.md). All rows are planned evidence until root reviews the implementation and the actual gates run. The configuration-only inspection proof must remain separate from the real database/provider persistence proof in `CliAcceptanceLegTests`.

## Complexity Tracking

No constitution violation is claimed and no complexity exception is requested. The separate worker command, capability, validator, and enrollment declaration are required to preserve the closed candidate-v1 and outer WorkerContract v2 contracts; a widened old payload or inferred host enrollment was explicitly rejected.
