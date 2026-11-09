# Implementation Plan: Workflow organization controls

**Branch**: `agent/codex/3595-workflow-organization-controls` | **Date**: 2026-11-01 | **Spec**: [spec.md](spec.md)

**Input**: #3595, bounded follow-up under #2559.

## Summary

Expose default-true `WorkflowsDesignApi.WorkflowOrganizationEnabled` through the existing typed backend settings catalog. Keep the library store, persistence observers, query visibility and ordinary paging composed. An immutable internal options snapshot gates organization capabilities/actions and projection without changing the authorization that stored organization currently requires.

Studio adapts per operation using advertised relations. It hides unavailable organization entry points/columns while preserving saved choices, and requires explicit recovery for unavailable organization-constrained URLs. Existing selected-backend settings review/Apply and capability refresh are reused.

## Technical Context

**Language/Version**: Existing C#/.NET 10 and TypeScript/React.
**Primary Dependencies**: Existing CShells feature configuration, manifest settings, NativeEndpoints, Studio SDK capability/endpoint context and Features module.
**Storage**: Existing configuration store and workflow library stores; no schema/provider/migration changes.
**Testing**: Whole affected API/frontend projects, rendered browser regressions, real selected-backend setting Apply/off/restart/on and published-artifact execution; normal repository/package/consumer/architecture gates.
**Target Platform**: Existing composed Workbench/Studio; desktop and 320px keyboard UI, existing styles and modes.
**Project Type**: Configuration and availability behavior in existing modules.
**Performance Goals**: None; no benchmark program.
**Constraints**: Default compatibility; no authorization/tenancy/ownership/authentication changes; no silent filter broadening; no new packages, feature IDs or persistence contracts.
**Scale/Scope**: One backend/shell Boolean controlling folders, tags, views and favorites. Partial third-party operation support remains relation-driven.

## Constitution Check

Loaded framework v6.1.0 and Elsa v6.3.0 plus the contributor review checklist. Gates pass for planning and after design:

- Preserve existing features/contracts and behavior under the default setting (§2.19, §2.21.1). Keep new implementation types internal; a configuration property on the existing public feature is the intended additive surface (§2.23.3).
- Keep the immutable options snapshot singleton and behavior scoped; preserve virtual feature configuration and existing composition (§2.5/§2.5.1).
- Keep store/provider boundaries and persistence invariants unchanged (§2.9, §2.20). No new cross-domain implementation reference or extension point is needed.
- Cover logic-bearing availability/writes/recovery with behavior tests and causal red/green evidence (§2.23.1–§2.23.2). Do not test documentation text.
- Keep Design-only/Runtime-only/combined shapes and published execution intact (Elsa §E2.2.3/§E2.6). No workflow authored-state change is introduced; provisional §E2.9 is not relied on.
- Use existing test projects/tier entries (Elsa §E7), computed package versions and normal exact-head/resulting-main gates.
- Draft framework §2.24 is not used as a ratified gate; no exception is requested.

## Project Structure

- `Essentials/src/Workflows/Design/Api/`: feature setting, internal snapshot, library capability source/access checks, page/legacy-list/add guards and authorization-preserving response projection.
- `Essentials/tests/Workflows/Design/Api/Tests/`: contract tests for defaults, disabled operations/placement, paging/filter refusal, projection and permissions.
- `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/`: per-operation availability, pure browse recovery, request guards, visible columns/metadata and stale-dialog retirement.
- Existing Workflows and Features frontend tests, `Studio/tests/browser/` and `Studio/tests/live/`: mounted logic, responsive keyboard recovery, selected-backend Boolean review and actual on/off/on proof.
- Existing backend feature README and `docs/program-goals/workflow-library-organization.md`: configuration and retained-data/consumer guidance; links to this unit for evidence.
- This spec directory: [research](research.md), [data model](data-model.md), [contract](contracts/organization.md), [validation guide](quickstart.md), and tasks.

## Execution and Review

Specification reviewed before planning; plan/research/contracts reviewed before tasks; tasks reviewed before implementation. Research used two independent source-only lenses, backend and Studio. Final code review uses independent correctness, standards and acceptance lenses on the actual diff; verify each actionable finding and its regression before changing code. Keep the PR draft through the author loop, then qualify the exact reviewed head and actual resulting main. Report cancelled/superseded runs, retries, skips and quarantines distinctly.

Behavioral cadence is risk-based (ADR 0080). New behavior uses focused red/green regressions because it changes advertised and accepted operations; the repository does not impose greenfield TDD universally. Final evidence belongs on #3595/its PR/#2559.

## Complexity Tracking

No new module, project, store interface, refresh bus, UI preference or generalized flag framework. Existing configuration and capability mechanisms own the change.
