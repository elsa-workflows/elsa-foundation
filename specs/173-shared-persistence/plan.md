# Implementation Plan: Shared persistence resources

**Branch**: `1305-shared-persistence` | **Date**: 2026-09-23 | **Spec**: [spec.md](spec.md)

**Input**: specification #1967 / `specs/173-shared-persistence/spec.md`.

**Status**: Phase 0 research in progress. Not approved for Elsa implementation. The owner approved bounded strict target verification on 2026-09-23. Phase 1 contracts and the task breakdown remain to be designed and reviewed; upstream publication is pending.

## Summary

Define named relational resources and shell/feature selection without changing stable feature identities or module-owned stores. Resolve one effective target before registration and tooling agreement. First prove the reviewed shared PostgreSQL composition, then the diagnostics split; retain legacy mode throughout.

Use a generic final-settings preparation hook in CShells so Elsa sees global and dependency-enabled participants before binding. [CShells #134](https://github.com/valence-works/cshells/issues/134) is a reviewed, independently deliverable prerequisite of #1968. Keep one active delivery item in project 51.

## Technical Context

**Language/Version**: C# 14 / .NET 10 in Elsa; upstream CShells retains its existing target-framework matrix.

**Primary Dependencies**: Existing Microsoft configuration/JSON/DI libraries, CShells abstractions, EF Core/Relational policy and module metadata. No provider engines in the pure resolver or modularity adapter; CLI and worker remain EF-free.

**Storage**: Authored host/shell JSON and existing configuration providers. Live proof uses PostgreSQL databases and module-owned migrations. No schema/data relocation is introduced.

**Testing**: xUnit component/architecture suites, real rebuilt-host backend journeys, database inspection and packaged CLI worker execution. Existing 63 management/store/mask and 38 legacy persistence checks are baseline evidence, not new-mode acceptance.

**Target Platform**: Supported Workbench host plus its out-of-process EF tooling. Preserve Foundation Host legacy behavior; wider resource-mode host enrollment requires explicit integration evidence.

**Project Type**: Existing libraries, host integration and CLI protocol; no web builder implementation in this slice.

**Performance Goals**: None; performance measurement was retired by owner decision/ADR 0073.

**Constraints**: No secret values in exported plans or diagnostics; preserve source/presence semantics; module/context and operation-specific transaction boundaries remain intact. Keep framework §2.12 and Elsa §E4 deferred. No generic settings language or internal CShells service replacement.

**Scale/Scope**: The 13 stable feature IDs across six participant sets in the spec, with only the first four sets in the initial shared layout and both diagnostics sets in the second layout. Dynamic/unknown participants do not become ready by resemblance of options.

**Approved direction**: Use bounded strict live target verification; see [decision record](decisions/tooling-target-verification.md). The owner approved one expected-value lookup and strict comparison, while retaining env/stdin as the only actual database connection input. Concrete host-context transport and negotiated protocol remain design gates.

## Constitution Check

Pre-design research check:

- Framework §2.1 / §2.7: provider-neutral contracts stay separate from EF adapters; no concrete engine reaches generic feature or CLI code.
- Framework §2.6.2: context preparation uses one explicit replacement seam; no uncontrolled collection of configuration-mutating guards.
- Framework §2.9 and Elsa §E2.5: preserve module-owned contexts and opt-in base context; no universal context or transaction abstraction.
- Framework §2.19: stable feature names remain the binding keys.
- Framework §2.21: preserve existing test subjects/objectives; no test removal to make a refactor pass.
- Framework §2.22: document supported configuration, refusal, lifecycle and extension behavior with evidence.
- Elsa §E2.2: Runtime/Design dependency boundaries and deployment shapes remain unchanged.
- Framework §2.12 / Elsa §E4: the bounded persistence contract does not ratify deferred generic settings classification.
- ADR 0066/0073/0076: retain ordered publication, first-party EF ownership and host-closure tooling. The bounded D7 verification extension has owner approval; its concrete contract and ADR amendment still require the normal design review.

No justified constitutional violation is proposed. Post-design check remains open until the concrete target-verification contracts are complete. The CShells prerequisite has its own package/dependency and regression gates.

## Project Structure

### Documentation

```text
specs/173-shared-persistence/
  spec.md
  research.md
  plan.md
  decisions/tooling-target-verification.md
  checklists/requirements.md
  # data-model.md, contracts/, quickstart.md and tasks.md follow research closure
```

### Source ownership

- `src/essentials/Persistence/EntityFramework/`: pure resource resolution, EF participant/constraint adapter and host-side tooling integration.
- `src/essentials/Modularity/Core/`: provider-neutral activation context-preparation contract only.
- `src/essentials/Modularity/Nuplane/`: legacy context preparation and mandatory pre-guard invocation.
- `src/essentials/Modularity/EntityFramework/`: root preparation adapter and safe resource-mode management preflight.
- `src/essentials/Cli/` and `Worker/`: explicit protocol negotiation and context transport; no competing EF resolver.
- `src/apps/Elsa.Workbench/`: opt-in root wiring and supported composition fixtures, preserving host-owned identity persistence.
- Existing test directories mirror these owners. Acceptance fixtures cover actual host/database/tooling behavior.
- Upstream `valence-works/cshells`: generic pre-binding preparation contract, builder invocation/invariants and lifecycle tests; no Elsa dependency.

**Structure decision**: Extend existing packages at their owning boundaries. Do not add a speculative umbrella project. Final type names and wire fields are assigned only in the reviewed contracts.

## Research decisions and remaining gate

[R1-R5](research.md) record ownership, lifecycle, management, tooling and evidence. R2 is resolved into an upstream prerequisite. R3 selects explicit pre-mutation refusal in the legacy feature editor for resource-mode compositions while retaining file/shell reload; resource-aware operations remain required under #1964.

R4 now has the owner decision. Finish and review the explicit host-context input and negotiated tooling protocol before publishing #1968 as implementable. CShells #134 remains the single active delivery leaf through review and publication; resume this specification as the next delivery item afterward.

## Delivery and validation gates

1. Deliver and publish CShells #134 with focused ordering, refusal, invariant and reload evidence. Pin it in Elsa when integrating #1968.
2. Finish the authored configuration, input-context, redacted plan and tooling contracts after R4. Review against every FR/SC and both existing stories; produce bounded tasks using the normal Spec Kit flow.
3. Deliver #1968 as the shared runtime/database/tooling path, including legacy checks and preflight refusals. Follow with #1969's actual two-target proof and negative layouts.
4. Run focused component/architecture tests and applicable backend end-to-end journeys. Refresh/check generated maps only when authoritative inputs change; required CI and review must pass before merge. Do not infer a full host result from a pure resolver test.
5. Publish evidence and keep issue dependencies, readiness, status and labels aligned. The program's other four epics remain required outcomes.

## Complexity Tracking

No constitutional exception is requested. The upstream hook is required to avoid duplicating dependency/default composition in Elsa. The owner-approved CLI verification extension is limited to one expected-value lookup and an existing strict comparison. Do not expand it into database identity discovery, generic secret management, or automatic connection selection.
