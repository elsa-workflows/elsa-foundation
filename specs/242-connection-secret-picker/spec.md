# Feature Specification: Connection credential Secret selection

**Feature Branch**: `agent/codex/2959-connection-secret-picker`
**Created**: 2026-10-18
**Status**: Implemented
**Input**: Owner requests a proper Connections UI Secret picker, then complete demo-v3 design and rehearsal. Tracking: #2959.

## User Scenarios & Testing

### P1 — Reuse an existing Secret
An operator creates an HTTP connection and selects a named existing Secret for each sensitive authentication field. Display names and technical names identify choices; only the chosen reference is submitted. A workflow then selects that connection and authenticates to the protected API.

Acceptance: metadata-only discovery is scoped by the existing backend; loading, search, refresh, empty, denied/failure/retry and unavailable states are explicit. No value-reveal request is made. Required/unresolved selections block submission. Server-side validation remains authoritative if metadata changes before creation.

### P1 — Keep entered credentials working
An operator can choose to enter a new value instead. Only the active source is serialized. Switching source, authentication scheme, connection type or endpoint cannot carry hidden stale values into a later request. While saving, repeat submits and dismissal are protected.

### P2 — Rotate the referenced Secret
After an authenticated run, rotate the named Secret and execute the same workflow publication again. Runtime resolves the current value; the workflow retains its Connection reference. Confirm with safe endpoint receipts and no token values in recorded evidence.

## Functional requirements

- Use the existing connection credential source contract and metadata-only Secret picker API. Preserve backend tenant, permission, status and compatibility validation. Do not fetch persisted values to the browser.
- Reuse Studio modal focus, controls and design tokens; expose clear labels, keyboard operation, usable narrow layouts and errors. No private imports between frontend feature modules.
- Support compatible current Secret types and reject unavailable/revoked/expired references through the authoritative server. Preserve new-value behavior.
- Track connection-owned versus externally referenced Secrets explicitly. Deleting/replacing a connection must not delete a selected shared Secret, including names resembling generated owned names or references retained by another field. Endpoint retargeting must preserve bind-secret permission checks. Null credential collections on create and null field entries on create/update return client errors. A null collection on update preserves the existing omitted-patch semantics. Shared-reference cleanup checks all stored tenant connections, but is not an atomic concurrent bind/delete guarantee.
- Isolate asynchronous results by request and endpoint. Clear incompatible state when scheme/type changes. Serialize one credential source per field.
- Cover real failure modes with red/green regressions, relevant full test projects and authenticated UI-to-runtime integration.

## Boundaries

No new credential store, runtime secret-resolution mechanism, connection persistence backend, OAuth flow or permission-policy redesign. #2854/#2855 retain their unrelated deferred scope. Rehearsal artifacts live separately and preserve prior demo versions. Current source evidence is not a rehearsal receipt.

## Success criteria

An operator can complete the existing-Secret journey through Studio without a seeding/API workaround; new-value creation still works; the protected local API receives authenticated requests before and after Secret rotation on the same workflow publication; the complete v3 demo is rehearsed and its outcomes recorded.
