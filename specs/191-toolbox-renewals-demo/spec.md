# Feature Specification: Toolbox renewals demonstration

**Feature Branch**: `codex/toolbox-renewals-demo`
**Created**: 2026-10-03
**Status**: Implemented and rehearsed locally on 3 October 2026; limits recorded in verification.md
**Input**: Complete Monday 5 October Toolbox Team live demonstration, browser presentation and cockpit.
**Program**: none/free-flow; a bounded demo deliverable, not a new product roadmap bucket.

## User Scenarios & Testing

### User Story 1 - Upgrade a renewal module live (P1)
A presenter runs a renewal workflow, deploys an additive module update, sees activation refuse a pending migration, applies it, reloads, explicitly upgrades the activity node and runs with a proposed premium.
**Independent test**: Actual authenticated Studio browser interactions and persisted rows, alongside package/runtime evidence.
1. Given module 1.0.0, registering POL-1042 persists its policy reference.
2. Given published 1.1.0 and Validate, installed and served versions differ until the migration is applied.
3. Applying the migration and reloading preserves the host PID and binary hash.
4. Explicitly selecting activity contract 1.1.0 exposes optional Proposed premium; executing 1250 persists a number.
5. Existing rows remain, and an unchanged 1.0.0 node still executes.

### User Story 2 - Drive the demo from the cockpit (P1)
The presenter uses fixed browser controls, sees real logs and observable state, and can reset/rehearse.
**Independent test**: Click every operational control; observe output and actual process/feed/database effects.
1. Setup prepares packaged features, authenticated Studio and hosts without terminal command pasting.
2. Failed commands remain failed; waiting explains what must change.
3. Stop/reset affects only owned demo processes and isolated disposable demo data.

### User Story 3 - Explain fleet readiness (P2)
Two hosts share a database; a schema-dependent feature stays dormant until all live hosts can read its schema.
**Independent test**: Actual two-host upgrade with shared persistence and explicit membership; observe dormancy before and availability after the second host upgrades.

### User Story 4 - Present the story clearly (P1)
The presenter uses a polished browser deck matching the inspected Elsa Cloud visual reference, with navigation, fullscreen, overview and optional notes.
**Independent test**: Visual/browser review and timed rehearsal of the approximately ten-minute core, including the two-host readiness transition.

### Edge Cases
- Unavailable ports, stale packages, missing migrations, a failed reload, an old live reader, and an interrupted build produce actionable failure state.
- Null premium remains null, never an invented zero.
- A process not owned by this cockpit must never be stopped.

## Requirements
- FR-001: Foundation.Host serves Studio; Nuplane supplies all feature implementations and demo modules.
- FR-002: Foundation.Host receives no added build-time feature references and its binary remains unchanged during the upgrade.
- FR-003: Renewal release 1.1.0 adds optional numeric Proposed premium and a nullable numeric database column.
- FR-004: Package, activity contract, schema and workflow versions are distinct and explained.
- FR-005: The cockpit performs real publication, migration, reload, setup/reset and rehearsal with live evidence.
- FR-006: Two hosts demonstrate shared schema readiness, not a simulated diagram.
- FR-007: Presentation and cockpit receive browser/visual review; illustrations are labelled.
- FR-008: Deliver cheatsheet, fallback, committed source, usable links and evidence that identifies limitations honestly.

## Key Entities
Renewal: ID, policy reference, creation time, schema stamp, optional proposed premium. Release: package/version, installed state, served state. Host: owned process, health, unchanged binary, schema-reader membership. Activity node: explicitly selected catalog contract.

## Success Criteria
- SC-001: A complete browser-driven baseline-to-upgrade rehearsal passes, including Studio node version selection and both executions.
- SC-002: A repeat run starts from a cockpit reset without terminal pasting.
- SC-003: The core story takes approximately ten minutes and includes the real two-host readiness observation.
- SC-004: Every announced runtime claim has recorded evidence; no simulated success substitutes for runtime proof.

## Assumptions
Use isolated local demo resources and fictional policies. Builds/packing happen before the audience arrives. The two-host readiness observation belongs in the core story. This is a small insurance illustration, not a pricing/underwriting product.
