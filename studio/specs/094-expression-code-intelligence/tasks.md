# Tasks: Expression Code Intelligence

All implementation tasks are complete. File names below reflect the landed design rather than provisional planning names.

## Contracts and transport

- [x] T001 Add editor-neutral documents, capabilities, authoring contexts, symbols, value shapes, tooling outcomes, diagnostics, and clients to the public Studio SDK and declaration mirrors.
- [x] T002 Add canonical Foundation capability-link clients and the coordinated OpenAPI contract.
- [x] T003 Implement explicit outcome/version mapping, cancellation, stale-result rejection, memory-only caches, permission/policy invalidation, and authorization purge.
- [x] T004 Keep expression source out of metadata caches and dispose all workflow/editor session state at authorization-session end.

## Shared rich editor

- [x] T005 Add CodeMirror 6 completion, lint, view, JavaScript, and Liquid dependencies.
- [x] T006 Implement compact and expanded profiles on one bounded workflow-lifetime editor session.
- [x] T007 Preserve exact source, selection, undo, diagnostics, and tooling state across compact/expanded transitions.
- [x] T008 Implement completion-aware Enter, Tab/Shift+Tab indentation, Escape-then-Tab exit, multiline expansion, read-only reconfiguration, and workflow-undo isolation.
- [x] T009 Add lightweight unfocused previews, accessible keyboard guidance/status, sanitized documentation, degraded fallback, and `--studio-*`-only styling.

## Per-language intelligence

- [x] T010 Register JavaScript and Liquid as independent Expression Editor Contributions with lazy language loading.
- [x] T011 Project JavaScript context through runtime-accurate `args`, `variables`, `getVariable`, and named getter symbols.
- [x] T012 Project Liquid context, tags, filters, direct variables, and nested member shapes without inventing generic Elsa globals.
- [x] T013 Consume authoritative completion/hover first and provide permission-filtered local catalog/value-shape fallback.
- [x] T014 Add automatic/explicit completion, hover, signatures, local syntax diagnostics, semantic diagnostic marks, and nested member completion.

## Workflow integration and consequential actions

- [x] T015 Derive stable draft/activity/property/type document identity and carry exact values between text Expression Types.
- [x] T016 Orchestrate authoring-context and semantic-validation requests with debounce, cancellation, source/context versions, blur validation, and stale response rejection.
- [x] T017 Present compact highest-priority and expanded full diagnostics while leaving editing/autosave available.
- [x] T018 Integrate full-draft validation with known-error Test Run blocking, unavailable-warning acknowledgement, and publication fail-closed behavior.
- [x] T019 Preserve source through unavailable/unauthorized/incompatible/module-missing states and provide retry/generic editing.

## Verification

- [x] T020 Cover shared editor, JavaScript, Liquid, transport, cache, authorization, diagnostics, workflow integration, and value-shape behavior with focused unit/component tests.
- [x] T021 Cover the real inspector in Chromium for compact activation, completion, Tab/Escape, expanded continuity, exact JavaScript-to-Liquid carryover, 50-field lazy activation, and narrow touch interaction.
- [x] T022 Validate Workflows bundle budgets with the expression tooling client deferred from landing routes.
- [x] T023 Reconcile the OpenAPI fixture with Foundation work unit 143 and record verification evidence.
- [x] T024 Run repository lint/typecheck/build, focused regression suites, browser tests, and Foundation coordinated gates.
- [x] T025 Complete iterative cross-repository self-review and required manual assistive-technology acceptance with no actionable release blockers.
