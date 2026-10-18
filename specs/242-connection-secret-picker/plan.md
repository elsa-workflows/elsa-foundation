# Implementation Plan: Connection credential Secret selection

**Branch**: `agent/codex/2959-connection-secret-picker` | **Date**: 2026-10-18 | **Spec**: [spec.md](spec.md)

## Approach

Keep credential-source selection in the Connections module. Use the existing `/secrets/picker` metadata API, narrow allowlisted DTOs and a focused credential-field component. Keep backend ownership of tenant/permission and reference eligibility checks. Retain entering new values as an explicit alternative. Share field serialization/state helpers rather than duplicating branches per authentication scheme. Use the SDK's modal focus behavior for the creation dialog.

Before implementing, audit current API validation and eligibility behavior; make only server changes required for safely exposing existing references. Do not implement unrelated repository or policy follow-ups. Review reproduced a directly relevant shared-reference ownership hazard: replace name-prefix ownership inference with authoritative stored provenance and retain any Secret still referenced by a remaining field. This closes the shared-reference cleanup edge from #2855 without taking on its other manager concurrency or permission-policy work.

## Constitution review

Framework6.1.0 / Elsa6.0.2 and ADR0080 apply. No domain migration, provider bypass, private cross-domain frontend import or demo-specific product behavior. Types remain internal by default. Test reference-only payloads, stale async work, source isolation, failed creation and server rejection, rather than mirroring implementation. Generated-map and architecture gates remain required. Re-check after integration.

## Verification

Run whole Connections frontend suite, affected Secrets or backend Connections/Secrets suites if changed, production module build, typecheck/scoped lint, browser keyboard and responsive checks, architecture and generated maps. Independently review correctness, standards/security and acceptance. On an isolated host, create the Secret and connection through UI and verify authenticated execution plus rotation without republishing.

## Demo design and rehearsal

Preserve v2. Complete v3 story, speaker notes, preparation/reset instructions and exact workflow/contract wiring. Rehearse authoring, standalone test, publication, composition, HTTP request, fault/diagnosis/repair, rotation and final run inspection. Save receipts/screenshots without credential values; label unverified steps honestly. Recording/upload is not part of this request.
