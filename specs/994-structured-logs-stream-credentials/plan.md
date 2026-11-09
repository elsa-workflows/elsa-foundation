# Implementation Plan: Authenticated Structured Logs Streaming

**Branch**: `agent/codex/3132-stream-credentials` | **Date**: 2026-10-20 | **Spec**: [spec.md](spec.md)

## Summary

For the existing Structured Logs page, construct its existing `EventSource` with `{ withCredentials: true }`. This lets the browser-managed session accompany the same stream URL across the normal separate Studio and Workbench origins. Preserve the existing filters, UI states, retry and cleanup lifecycle, backend authorization, and empty-inventory behavior. No endpoint, package, storage, or authentication-model change is planned.

## Technical Context

**Language/Version**: TypeScript, React 19 (existing Studio client)

**Primary Dependencies**: Existing browser `EventSource`, Studio SDK, Vitest; no dependency changes

**Storage**: N/A; no new persisted state

**Testing**: Existing module Vitest suite, focused backend authorization/replay suites as regression evidence, and the required normal-host browser qualification

**Target Platform**: Existing Studio browser app against Workbench/API, including the configured separate-origin development composition

**Project Type**: Existing Studio module client

**Performance Goals**: N/A; no throughput or latency change is intended

**Constraints**: Keep the stream URL and filter serialization unchanged; use only browser-managed credentials; do not expose secrets in URLs or evidence; backend permissions and configured credentialed CORS remain authoritative

**Scale/Scope**: One existing EventSource construction site and its module tests; no new entity, endpoint, service, or public API

## Constitution Check

**Pre-design and post-design: Pass.** Apply both Gate indexes and [the review checklist](../../docs/contributing/review-checklist.md). The change preserves valid behavior (§2.21.1), adds behavior-focused coverage for the risky client path without text assertions (§2.23.2), and adds no public contract or package surface (§E5). Registration and feature composition are unchanged. Draft/provisional Elsa §E2.8 and §E2.9 do not govern this UI-only change. No exception or added complexity needs justification.

## Project Structure

```text
specs/994-structured-logs-stream-credentials/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
└── contracts/README.md

Extensions/Diagnostics.StructuredLogs/studio/Elsa.Studio.Diagnostics.StructuredLogs/Client/
├── src/module.tsx
└── src/__tests__/module.test.ts

Essentials/tests/Diagnostics/StructuredLogs/Tests/
├── StructuredLogsApiAuthorizationTests.cs
└── StreamEndpointTests.cs
```

**Structure Decision**: Keep the fix and rendered-constructor regression test in the existing Structured Logs Studio client. Existing backend tests remain independent security and stream-behavior evidence; no backend source changes are in scope.

## Complexity Tracking

None. The design uses the existing browser API and current client lifecycle.


## Local qualification and delivery order

The final independently reviewed PR #3133 candidate may be stacked locally to run combined product-host checks while parent CI is pending. Delivery order remains parent-first: integrate its verified main result before opening this issue’s PR, compare source changes, and repeat affected proof. Parent revisions invalidate the relevant earlier qualification. Exact-head review/CI and resulting-main verification still gate delivery. This sequencing refinement changes no product behavior, scope or acceptance criterion.
