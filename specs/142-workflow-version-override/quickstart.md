# Quickstart: validate workflow version override

This guide validates the planned behavior against a rebuilt host with the Workflow Design API, Workflow Design Groundwork persistence, API Capabilities, and a test identity authorized for `workflow-design.manage` and `api-capabilities.read`.

## 1. Verify capability-gated compatibility

1. Request the authenticated shell-relative `GET /capabilities` endpoint.
2. Locate `elsa.api.workflow-design` and confirm it has templated `workflow-draft-promote-version-preflight` and `workflow-draft-promote-exact-version` links pointing to the documented routes.
3. Against a host version without those relations, confirm the client sends only automatic promotion requests and does not send `requestedVersion` or probe preflight.

Expected: the same Studio build can use automatic promotion on older hosts and expose preflight plus exact assignment only on supporting hosts.

## 2. Verify non-mutating authoritative preflight

Create or obtain a valid draft for a definition whose latest immutable version is `2.0.0`. Record the version count, then call:

```http
POST /design/workflows/drafts/{draftId}/promotion-preflight
Content-Type: application/json

{
  "requestedVersion": " 2.1.0 "
}
```

Expected: `200 OK`, `isReady: true`, `assignmentMode: "exact"`, `requestedVersion: "2.1.0"`, `resolvedVersion: "2.1.0"`, and `latestVersion: "2.0.0"`. The version count remains unchanged and no version identity is reserved. Repeat without `requestedVersion` and expect `assignmentMode: "automatic"` with the current automatic next-major candidate.

Preflight malformed, equal/lower, build-metadata-equivalent, and draft-validation cases. Expected: `200 OK`, `isReady: false`, no writes, and a stable issue code. The client must treat this answer as current evidence, not as a promotion reservation.

## 3. Verify exact forward assignment and automatic compatibility

Call the existing mutation route with a stable operation key:

```http
POST /design/workflows/drafts/{draftId}/promote
Content-Type: application/json

{
  "operationKey": "promote-2-1-0",
  "requestedVersion": " 2.1.0 "
}
```

Expected: `201 Created`; the response version and stored immutable version are `2.1.0`. Repeat with `2.2.0-rc.1` and expect `201 Created` when it is forward by SemVer precedence. Promote a new valid draft without `requestedVersion` and expect the same next-major label the host produced before this feature.

## 4. Verify atomic recheck and rejection without a write

For each request below, record the version count before and after promotion:

| Request label | Expected status | Expected result |
|---|---:|---|
| `2.1` or `v2.1.0` | 400 | Invalid SemVer; version count unchanged. |
| `  ` | 400 | Empty after trim; version count unchanged. |
| `02.1.0` | 400 | Leading zeroes are invalid; version count unchanged. |
| Current latest label | 400 | Not forward; version count unchanged. |
| Lower prerelease/release | 400 | Not forward; version count unchanged. |
| Existing label plus only build metadata | 409 | Same semantic identity is occupied; version count unchanged. |
| Two concurrent exact requests for the same forward label | one 201, one 409 | Exactly one immutable version exists. |

Also preflight a ready candidate, then cause another promotion to claim it before submitting the original mutation. Expected: the original promotion returns `409`; it does not rely on stale preflight data. Submit a draft that fails the existing promotion validation gate. Expected: `409` with the existing validation details and no version, regardless of preflight or exact label.

## 5. Verify replay semantics

1. Promote a valid draft with an exact label and a stable operation key; retain the response.
2. Repeat the identical request after simulating a lost response.
3. Repeat the same key with a different exact label, then repeat it with the version omitted.

Expected: the identical retry returns the original immutable version and does not add another one. Both altered requests return `409` because their assignment material differs from the committed operation.

## 6. Run automated checks

Run the focused Workflow Design API and persistence test projects selected by the implementation, then the complete relevant design suite. Finally rebuild `Elsa.Server` and run the REST-driven workflow-design e2e scenario following [e2e-tests/README.md](../../e2e-tests/README.md).

Expected: new tests cover preflight, exact assignment, atomic recheck, and replay; existing automatic-promotion tests remain green; and the e2e journey proves the composed host accepts the advertised contracts.

## Related artifacts

- [Data model](data-model.md)
- [OpenAPI extension](contracts/workflow-version-override.openapi.yaml)
- [Research decisions](research.md)
- [ADR 0050](../../docs/adr/0050-author-requested-forward-workflow-versions.md)

## Recorded implementation evidence

- Workflow Design domain tests: 327 passing.
- Groundwork promotion tests: 93 passing.
- Workflow Design API contract tests: 68 passing.
- Focused management-contract and capability architecture tests: 11 passing.
- Rebuilt `Elsa.Server` against an isolated SQLite database and ran
  `e2e-tests/workflow-version-override/Test-WorkflowVersionOverride.ps1`: 5/5 live HTTP cases passing.
