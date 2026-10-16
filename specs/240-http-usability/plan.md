# Implementation Plan: HTTP Usability

**Date**: 2026-10-16 | **Spec**: [spec.md](spec.md)

## Design

Keep `Async = 0` stable. Change the activity and publication defaults to Sync, but retain Async as the reader's legacy metadata fallback. Stamp the effective response mode in every new binding. Provide dropdown labels/options/default through existing activity input metadata. Add additive, non-sensitive accepted-response diagnostics through the HTTP transport.

Studio renders the mode, explains the Async/response combination, and exposes an explicit corrective action. A catalog-driven starter uses existing Flowchart/HttpEndpoint/WriteHttpResponse contracts. A small request dialog uses the canonical backend client and displays the actual HTTP response; it never substitutes transient workflow test-run behavior for a live HTTP exchange.

## Constitution review

Use existing extension points and preserve Runtime/Design boundaries (§E2.2). HTTP stimulus metadata remains transport-owned. Do not add generic graph members to ActivityNode (§E2.9, provisional; use existing activity-owned flowchart structure). Catalog remains the source of version IDs (§E2.8); no reconciliation-policy change. Follow ADR 0080's pre-GA simplification and plain language direction. No package/reference or migration change is planned.

## Validation

Run affected whole HTTP test projects and Studio workflows frontend suites, with focused red/green evidence for new behavior. Rebuild the Workbench and run HTTP e2e tests with an isolated fresh database. Run architecture guards and generated-map freshness. Review changed generated outputs only if a refresh is necessary. Verify the complete starter/publish/request flow in a real browser. Run a bounded self-review loop (maximum ten rounds) and fix concrete findings before opening the PR.

## Delivery

One PR for #1177. Retain source and test evidence on the PR. The user requested a PR only; no author merge or deployment.
