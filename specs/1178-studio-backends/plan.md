# Implementation Plan: Studio backend connections

**Branch**: `agent/codex/3531-backend-connections` | **Date**: 2026-10-31 | **Issue**: #3531 | **Spec**: [spec.md](spec.md)

## Summary

Implement the approved UX in the reusable Hosting package as a lazy Backends module. Keep profile persistence and host authentication outside the selected backend. Candidate preparation uses independent credentials, capability validation and a write/navigation guard. Commit uses a full browser navigation to a safe backend-qualified landing route: this deliberately resets browser-cached module globals, clients, queries, subscriptions and editor state instead of pretending a React remount clears them. No API object changes its URL after construction.

## Technical Context

TypeScript/React, existing SDK/UI primitives, .NET 10 reusable Studio Hosting, governed Preferences.Core and optional existing durable provider. Browser-safe configuration is explicitly projected by StudioRuntimeScript. Host profile HTTP routes delegate to IStudioPreferenceService; their scope comes from the original host's verified subject/tenant plus configured HostId. Anonymous demo profiles use bounded browser-local metadata only. Base Hosting uses the existing replaceable in-memory preference store; production durability is an explicit existing provider composition and must be documented and qualified.

## Constitution Check

Framework v6.1.0 and Elsa v6.3.0 gates and review checklist loaded. Preserve Design-only/Runtime-only/combined deployment shapes (§E2.2.3), internal-by-default implementations, existing contribution availability and SDK compatibility, no new global settings semantics or universal URL proxy. Hosting references Preferences.Core contracts/services; no Foundation Identity/Preferences.Api dependency is introduced. No new feature identity or provider implementation is needed. Public options and package dependencies are tracked; consumers are qualified. Draft framework §2.24 and provisional Elsa activity-versioning/model-state rules are not relied upon. Tests are risk based; greenfield cadence is not prescribed, while behavioral regressions require bite evidence. Post-design review found no required constitutional exception.

## Design

- Host config: named public profiles, default, personal-profile policy, and allowlisted authentication profiles; legacy URL becomes the default connection. Keep privileged server management configuration out of browser profile data.
- Personal profiles: strict namespace metadata schema, revision/ETag conditional writes, verified host subject/tenant; source-owned profiles cannot be overridden by personal IDs.
- Runtime: immutable backend lifetime, pending-write counter and abortable reads. Guard before preparation and synchronously before commit. The committed full navigation includes backend identity and opens Home; resource links without an explicit backend use configured default, never a remembered different backend.
- Authentication: stable original host manager for manifests, profile preferences and host administration; independent selected backend manager/session/SignalR factory. Identity defaults retain full shell bases. Candidate sign-in callback restores only the pending candidate, never an arbitrary return URL.
- Capabilities: shared SDK wire model, strict validation, per-context cache, dormant reasons and module-declared requirements. Existing Workflows helpers forward to it. Contribution availability gates routes/nav/widgets/actions/editor integrations; modules declare actual requirements rather than a central list of feature names.
- Features: reuse existing route with the selected backend preselected. Pin actual backend session identity in catalog/review/completion. Preserve typed settings, dependency cascades and full-state revision Apply; newer runtime/operational states remain distinct from configured enablement. Successful Server Apply refreshes shared capability discovery.
- UX: Backends under user Settings, separate list/details/create/edit, Connection/Capabilities tabs, global quick switcher in each supported chrome placement; production identity and failure recovery stay explicit.

## Structure and Validation

`Studio/src/essentials/Elsa.Studio.Hosting/Client/src/app/backends/` owns module UI and session coordination; `Client/src/sdk/` owns shared public contracts/discovery; Hosting owns root profile persistence and browser config. Existing Features and other module registration files declare capability requirements. Existing tests are extended rather than duplicating implementations.

Focused tests -> affected whole frontend suites, typecheck/lint/build -> Studio .NET whole suite, Preferences suites, architecture/maps/locked restore -> real source-host multi-backend journeys -> package-consumer proof -> independent diff review -> draft PR, exact-head CI/full-tier/security/Studio gates -> merge and resulting-main checks. Failures, skipped and quarantined cases are recorded distinctly. If main advances, integrate/requalify. All build/test commands use the shared machine build-slot wrapper.

## Independent design review

Three independent source reviews covered correctness/auth/lifetime, repo/persistence boundaries and acceptance/feature administration. Integrated findings: preserve host auth, complete backend token factory, shell-relative routes, full browser lifetime reset, final write/draft guard, exact feature target and strict capability parsing. Reviews are source-based, not test execution evidence.
