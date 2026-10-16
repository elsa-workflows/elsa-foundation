# Implementation Plan: HTTP Usability

**Date**: 2026-10-16 | **Spec**: [spec.md](spec.md)

## Design

Keep `Async = 0` stable. Change the activity and publication defaults to Sync, but retain Async as the reader's legacy metadata fallback. Stamp the effective response mode in every new binding. Provide dropdown labels/options/default through existing activity input metadata. Add additive, non-sensitive accepted-response diagnostics through the HTTP transport.

Bound synchronous HTTP handling with a configurable `ActivitiesHttp.SyncRequestTimeout` (30 seconds by default). Once the route and endpoint settings are selected, the Sync deadline covers authorization, request-body reading, inline dispatch, and response delivery. A positive endpoint `RequestTimeout` may shorten the host deadline but cannot extend it. Cancellation flows through the request token; cooperating handlers and activities time out as 408. Async endpoints keep their existing authored dispatch timeout, armed immediately before dispatch.

Declare HttpEndpoint activity version `1.1.0` because its input default changes the hashed catalog contract. Preserve older catalog rows and compiled artifacts. Recapture the External replay baseline through the normal publisher with the new default contract, retain the historical fixture/import proof, and keep the strict behavioral comparison intact.

Studio renders the mode, explains the Async/response combination, and exposes an explicit corrective action. A catalog-driven starter uses existing Flowchart/HttpEndpoint/WriteHttpResponse contracts. A small request dialog uses the canonical backend client and displays the actual HTTP response; it never substitutes transient workflow test-run behavior for a live HTTP exchange.

The dialog reads the active shell's effective workflow HTTP mount from the `elsa.http.workflow-endpoints` API capability, preserving the shell route prefix. It fails closed when the capability is unavailable or unusable and permits path edits only beneath that mount. Its own 35-second client timeout is separate from the server's Sync deadline.

## Constitution review

Use existing extension points and preserve Runtime/Design boundaries (§E2.2). HTTP stimulus metadata remains transport-owned. Do not add generic graph members to ActivityNode (§E2.9, provisional; use existing activity-owned flowchart structure). Catalog remains the source of version IDs (§E2.8); no reconciliation-policy change. Follow ADR 0080's pre-GA simplification and plain language direction. Add a project reference from Activities HTTP to API Capabilities and update affected lockfiles; do not change NuGet package versions, central package versions, or persistence schemas.

## Validation

Run affected whole HTTP test projects and Studio workflows frontend suites, with focused evidence for synchronous host deadlines, capability advertisement and lookup, and path containment. Rebuild the Workbench and run HTTP e2e tests with an isolated fresh database. Run architecture guards and generated-map freshness. Review changed generated outputs only if a refresh is necessary. Verify the complete starter/publish/request flow in a real browser, including a host-prefixed workflow mount and the Studio client timeout. Run a bounded self-review loop (maximum ten rounds) and fix concrete findings before opening the PR.

## Delivery

One PR for #1177. Retain source and test evidence on the PR. Independent Code Review and green required checks govern merge; no deployment is included.

## Release note

New HTTP workflow publications default to waiting for the authored response (Sync); already-published bindings with missing mode metadata retain Async behavior. Every Sync endpoint, including existing ones, now has a configurable host deadline of 30 seconds after its route/settings are selected. Cooperating operations return HTTP 408 when that deadline expires before headers start; an expiry after response start aborts the connection. Hosts that need longer inline workflows must raise `ActivitiesHttp.SyncRequestTimeout`; a shorter authored endpoint `RequestTimeout` still takes precedence. Studio's HTTP test request has its own 35-second client limit. See the [HTTP authoring guide](../../docs/reference/http-workflow-authoring.md) for configuration and migration details.
