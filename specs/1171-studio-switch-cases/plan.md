# Implementation Plan: Visual Switch cases

**Branch**: `agent/codex/3493-switch-cases` | **Date**: 2026-10-31 | **Spec**: [spec.md](spec.md)

## Summary

Add a shared collection-slot editor to the existing Inspector Slots panel. Derive editable collections from catalog descriptors, even when no items exist. Add a canvas action that selects the activity and opens that editor, using the same document mutation path for both graph hosts. Preserve existing choose/open controls and Default. Use exact string comparisons; keep invalid local input out of authored state.

## Technical Context

**Language/Version**: Existing TypeScript/React and .NET 10 guards.
**Primary Dependencies**: Existing Studio SDK/UI, React Flow, Vitest and Playwright.
**Storage**: Existing authored activity structure and host-controlled persistence only.
**Testing**: Model/mounted behavior, browser journeys across both editor hosts, affected whole Workflows suite, Studio lint/typecheck/build, architecture/maps and hosted checks.
**Target Platform**: Studio browser, desktop and narrow keyboard use.
**Constraints**: Preserve unknown payload members, nested activity identity, external-update invalidation, permissions, read-only views, navigation and undo/redo. No backend/public contract or new dependency.
**Scale/Scope**: Single-label, single-child collection slots (Switch cases and Parallel branches); leave multi-child collections such as Compensation Scope to their existing designer.

## Constitution Check

Loaded framework v6.1.0 and Elsa v6.3.0 Gate indexes and the review checklist. Framework §2.21.1 preserves valid behavior; §2.23.2 requires risk-based behavior tests with red/green bite proof. Existing modules, references, API visibility, features, schemas and provider boundaries remain unchanged. Architecture and maps checks apply. Studio ADR 0001 retains existing contribution ownership; ADR 0015 keeps persistence in the host. No constitutional exception or new reliance on provisional §E2.9 is introduced. Post-design check: unchanged, no violations identified.

## Project Structure

- `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/collectionSlots.ts`: descriptor admission and immutable case/branch edits.
- `.../workflow-editor/CollectionSlotEditor.tsx`: exact label editing, validation and populated removal confirmation.
- Existing `InspectorPanel`, node/Sequence canvas controls and both graph hosts: shared control and host mutation wiring.
- Existing Workflows tests and browser fixtures: lossless edits, stale/read-only boundaries, real rendered controls and host persistence/undo.

## Review and Delivery

Review spec, then plan, then tasks before implementation. Use local focused red/green proofs before full gates. Open one draft PR; run independent correctness, standards and acceptance reviews without shared findings. Requalify on current-main integration, request application review after local convergence, and verify exact-head checks plus resulting main. Record execution evidence on #3493 and its PR. Standing org authorization governs any self-merge after clean review and checks.
