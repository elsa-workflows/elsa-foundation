# Studio Expression Developer Experience

- **Status:** Active; approved for end-to-end implementation on 2026-10-02.
- **Area:** Foundation Studio activity-input code authoring and Foundation expression tooling.
- **Stewards:** Sipke as product owner; the incoming program-lead session as delivery owner.
- **Program issue:** [Foundation #2310](https://github.com/elsa-workflows/elsa-foundation/issues/2310).
- **Project:** [Studio expression developer experience, project 53](https://github.com/orgs/elsa-workflows/projects/53).

## Purpose

Deliver syntax coloring and useful IntelliSense whenever an author selects an installed supported text syntax for an activity input. Make compact and expanded expression editing smooth, readable and helpful, with correct workflow scope and runtime-compatible assistance.

## Scope and ownership

Studio owns module/editor discovery, the shared CodeMirror presentation/session substrate, syntax-specific adapters, completion/help interaction and accessibility. Foundation owns language/runtime metadata, authoring scope, permission boundaries and authoritative validation. Existing Studio spec 094 and Foundation spec 143 remain the baseline to reconcile. Keep their module boundaries; do not create a second editor system.

Reference and structured expression types retain their appropriate editors. No runtime-value evaluation for completion, unsolicited language addition, unrelated editor replacement, or new performance-measurement program belongs in this bucket.

## Active objectives

1. Verify and protect normally composed Studio/backend expression editing, adopting Studio #546's existing host-discovery fix.
2. Define installed-syntax conformance and align JavaScript editor grammar/help with runtime semantics.
3. Deepen JavaScript language/member/type assistance and Liquid interpolation/filter/tag assistance, with useful runtime-owned diagnostics.
4. Polish themed colors, compact previews, help panels, signatures, formatting, keyboard behavior and editing continuity; demonstrate the complete real workflow journey.

The incoming lead progressively elaborates these milestones into one cross-repository issue hierarchy and project queue after the next milestone's readiness check. No implementation leaf is assigned by this bootstrap. Keep one integration lane and bounded isolated workers; the program issue is the public record of state changes and evidence.

## Canonical surfaces

- [Assessment and staged delivery plan](../reports/studio-expression-editing/assessment.md).
- [Independent source-audit findings](../reports/studio-expression-editing/source-audit.md).
- [Fresh-session implementation handoff](../reports/studio-expression-editing/implementation-handoff.md).
- [Studio spec 094](https://github.com/elsa-workflows/elsa-foundation-studio/tree/main/specs/094-expression-code-intelligence).
- [Foundation spec 143](../../specs/143-expression-code-intelligence/spec.md).
- [Existing host-discovery fix, Studio #546](https://github.com/elsa-workflows/elsa-foundation-studio/pull/546).

## Readiness and completion

The user approved the complete assessment plan and requested a fresh implementation session. Retain CodeMirror and existing ownership boundaries. The choice of a deeper JavaScript language service is a bounded technical spike owned by the lead before milestone 3; it does not block baseline proof. Formatting must preserve behavior, including Liquid whitespace. Unsupported unknown shapes are reported honestly rather than treated as known errors.

The initial browser evidence is fixture evidence only. Full persisted-workflow integration remains unverified; the local backend stopped during the assessment. Revalidate current refs, composition and service availability before implementation and before claiming a gate.

Complete only after real Studio/backend user journeys demonstrate every scoped milestone, applicable review/build/test/architecture/map gates pass, and the issue/project records contain the actual evidence. Do not substitute closed issue counts, synthetic fixtures or historical results for this outcome. Route any justified scope change through the program issue and owner decision where material.
