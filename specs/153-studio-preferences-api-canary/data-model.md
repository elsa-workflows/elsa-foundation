# Data Model: Studio Preferences API Canary

## Studio preference route scope

- `Subject`: authenticated normalized session subject; required.
- `TenantId`: authenticated normalized session tenant; required.
- `StudioHostId`: `X-Elsa-Studio-Host-Id`; 1-128 characters matching the existing allowed character set.
- `Namespace`: route segment from `/_elsa/studio/preferences/{namespace}`; resolved through the registered namespace catalog.

These four values form the existing `StudioPreferenceKey`. Route/header/session sources are authoritative; JSON input cannot override them.

## Write payload

- `SchemaVersion`: integer schema version.
- `Value`: arbitrary JSON element validated by the selected preference namespace.

The HTTP body does not own `Namespace`, subject, tenant, or host selection even though the legacy FastEndpoints request type combined route and body binding.

## Conditional write

- `If-None-Match: *`: create only when no document exists.
- `If-Match: "<revision>"`: update only when the quoted revision matches.
- Any missing, malformed, or ambiguous combination is invalid.

The parsed value remains `StudioPreferenceWriteCondition` with `MustNotExist` or `Matches(revision)` semantics.

## Studio preference document

- Existing scope/document identity fields from `StudioPreferenceDocument`.
- `SchemaVersion` and canonical JSON `Value`.
- `Revision`: returned in the document and as a quoted `ETag` response header.
- Existing creation/update timestamps and persistence semantics remain unchanged.

## Endpoint contract record

- `Route`: exact source template `/_elsa/studio/preferences/{namespace}`.
- `Method`: `GET` or `PUT`.
- `Owner`: `Elsa.Studio.Preferences.Api`.
- `AuthoringModel`: `Minimal API` after migration.
- `PermissionPolicy`: canonical `Any` policy containing `*` plus the action permission.
- `ShellMetadata`: shell id/generation/prefix supplied by CShells at publication time.

Exactly one GET and one PUT record may be active per shell generation.

## Compatibility evidence set

- `Manifest`: canonical endpoint identity, owner, authoring model, security disposition, request/response metadata.
- `Http`: named observations for authorization, success, missing data, invalid host, invalid preconditions, validation, quota, and conflicts.
- `OpenApi`: canonical projection of the two consumed operations.
- `ApprovedDifferences`: exact, reviewed differences; expected to remain empty for this migration.

Ten unchanged captures must serialize identically.

## Authorization cases

- Anonymous or untrusted normalized identity: `401`.
- Authenticated identity without the action permission: `403`.
- Exact read/write grant: allowed on its action.
- Write grant: allowed on GET through the existing `write -> read` catalog implication.
- Explicit `*` grant: allowed through the retained wildcard branch.
- Resource-handler denial: remains authoritative even when claim evaluation would otherwise allow.

## Unload evidence

- `Cycle`: repeated isolated load/map/release attempt.
- `Stage`: route, services, serializer, harness, or clean.
- Weak references: load context, assembly, and endpoint type only.
- `Collected`: bounded result after all owned route/provider references are released.
- `Diagnostic`: owner classification when a reference remains.

Route and service stages must collect after release. Serializer retention, if present because of runtime caches, is reported separately and cannot mask failures in the required stages.
