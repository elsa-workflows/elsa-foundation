# Feature Specification: Secrets Module

**Feature Branch**: `079-secrets-module`

**Created**: 2026-06-24

**Status**: Draft

**Input**: User description: "Implement a Secrets module for elsa-foundation and elsa-foundation-studio based on the intended elsa-core and elsa-studio Secrets functionality."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Manage Named Secrets (Priority: P1)

An operator can manage named secrets from the Studio security area without exposing current secret values. The operator can create a secret, inspect safe metadata, update metadata, rotate the value, test whether the secret resolves, revoke it, and delete it.

**Why this priority**: This is the core user value. Without a trusted management surface, workflow authors and operators keep putting passwords, API keys, and connection material directly into workflow definitions, settings, or application configuration.

**Independent Test**: Enable the Secrets feature, open Studio, create a text secret, verify it appears in the secrets list with metadata only, rotate it, revoke it, and verify runtime resolution no longer returns a usable value.

**Acceptance Scenarios**:

1. **Given** an operator opens the secrets area, **When** they create a text secret with a technical name, display metadata, store, and value, **Then** the secret appears in the list without showing the submitted value.
2. **Given** an active secret exists, **When** the operator edits its display name or description, **Then** the metadata is updated without changing the secret's technical name or current value.
3. **Given** an active secret exists, **When** the operator rotates the secret with a replacement value, **Then** the previous active version is retired and the new version becomes the only active latest version.
4. **Given** a revoked or expired secret exists, **When** a consumer attempts to resolve it, **Then** resolution fails with a safe error that does not reveal the value.
5. **Given** a secret exists, **When** the operator tests it, **Then** Studio reports whether the secret resolves without displaying the resolved value.

---

### User Story 2 - Use Secrets From Workflows And Modules (Priority: P1)

A workflow author or module operator can select a named secret reference for sensitive workflow inputs or module settings. The saved artifact stores only the immutable reference, and runtime consumers resolve the latest active value only when needed.

**Why this priority**: The module exists to keep sensitive values out of workflow and module configuration payloads while letting consuming features stay independent from the chosen secret store.

**Independent Test**: Configure a sensitive workflow input with a secret reference, save the workflow definition, inspect the saved payload to verify no raw secret value exists, then execute or resolve the input and verify the consumer receives the active secret value.

**Acceptance Scenarios**:

1. **Given** an activity input is marked as eligible for secret references, **When** a workflow author edits the input in Studio, **Then** Studio offers a secret picker instead of requiring a literal secret value.
2. **Given** a workflow definition contains a secret reference, **When** the workflow is saved, **Then** the stored definition contains the reference identity and does not contain the resolved value.
3. **Given** a workflow using a secret reference runs after the secret is rotated, **When** the input is resolved, **Then** the latest active version is used without editing the workflow.
4. **Given** a referenced secret is missing, inactive, expired, revoked, type-incompatible, scope-incompatible, or unavailable, **When** a consumer resolves it, **Then** resolution fails deterministically with a non-secret error.

---

### User Story 3 - Choose Secret Types And Stores (Priority: P2)

An operator can choose what kind of secret is being created and where it is stored. The authoring experience and compatibility rules adapt to the selected type and store while preserving one common resolution surface for consumers.

**Why this priority**: The feature must support more than one kind of sensitive material and more than one ownership model, including Elsa-managed values and deployment-managed configuration references.

**Independent Test**: Register the built-in secret types and stores, create one Elsa-managed text secret and one configuration-backed reference, then resolve both through the same runtime resolution surface.

**Acceptance Scenarios**:

1. **Given** multiple secret stores are available, **When** an operator creates a secret, **Then** Studio requires a compatible store choice or applies the configured default.
2. **Given** a text secret type is selected, **When** the operator creates or rotates it in an Elsa-managed store, **Then** a replacement value is required.
3. **Given** a configuration-backed store is selected, **When** the operator creates a secret reference, **Then** Studio asks for the configuration lookup metadata and never asks Elsa to store or reveal the configured value.
4. **Given** a secret type does not support a selected store, **When** an operator attempts to create or rotate it, **Then** the operation is rejected before any value is persisted.

---

### User Story 4 - Govern And Audit Secret Operations (Priority: P3)

Administrators can distinguish between metadata read, secret management, value update, deletion, runtime use, testing, import, and export permissions. Security-relevant operations are auditable without recording raw secret values.

**Why this priority**: Secrets are a security boundary. The system must not grant broad access to secret values merely because a user can manage the workflow or module that consumes them.

**Independent Test**: Configure users with different secret permissions, verify each can perform only the allowed operations, and verify audit records are emitted for privileged operations without raw values.

**Acceptance Scenarios**:

1. **Given** a user has secret metadata read permission only, **When** they view secrets, **Then** they can see safe metadata but cannot rotate, revoke, delete, test, import, or export values.
2. **Given** a user can select a secret for a workflow input, **When** they use the picker, **Then** they do not gain permission to update, test, reveal, or export that secret.
3. **Given** a privileged operation succeeds or fails, **When** audit records are inspected, **Then** the record identifies the operation, actor, secret identity, and outcome without containing raw secret values.

### Edge Cases

- A submitted technical name differs only by case or surrounding whitespace from an existing secret.
- A user attempts to change a secret's technical name after creation.
- A secret has no active version because of corruption or concurrent edits.
- More than one version is incorrectly marked active.
- A secret value is too large or invalid for the selected type or store.
- A configuration-backed reference points to a missing configuration key.
- A workflow references a secret that was deleted, revoked, expired, moved, or made unavailable after the workflow was saved.
- A store supports reading but not writing, deleting, testing, versioning, or inline creation.
- A Studio module is installed without the matching backend Secrets feature enabled.
- Error messages, logs, audit records, API responses, exports, and Studio notifications accidentally include raw secret values.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST support named logical secrets that are referenced by immutable technical name.
- **FR-002**: The system MUST normalize technical names for uniqueness checks and reject case-insensitive or trimmed duplicates.
- **FR-003**: The system MUST treat the technical name as immutable after creation.
- **FR-004**: The system MUST separate safe secret metadata from secret value material.
- **FR-005**: The system MUST provide safe metadata list and detail views that contain no raw value, encrypted payload, external lookup secret, or provider-private material.
- **FR-006**: The system MUST support creating, updating metadata, rotating, revoking, deleting, testing, listing, and inspecting secrets.
- **FR-007**: The system MUST support secret versioning with exactly one latest active version for a healthy active secret.
- **FR-008**: Rotating a secret MUST create a new active version and retire previous active versions.
- **FR-009**: Revoking a secret MUST prevent all future resolution of that secret until a permitted replacement action creates an active state again.
- **FR-010**: Deleting a secret MUST remove it from ordinary list and picker results.
- **FR-011**: The system MUST resolve references to the latest active version of the named secret.
- **FR-012**: The system MUST reject resolution for missing, inactive, expired, revoked, deleted, type-incompatible, scope-incompatible, unauthorized, or unavailable secrets.
- **FR-013**: Resolution failures MUST use safe messages that do not reveal secret values or provider-private details.
- **FR-014**: The system MUST support pluggable secret stores behind a common authoring and resolution model.
- **FR-015**: The first release MUST include an Elsa-managed encrypted store for values owned by Elsa.
- **FR-016**: The first release MUST include a configuration-backed store where Elsa stores only lookup metadata for deployment-managed values.
- **FR-017**: The configuration-backed store MUST NOT allow Studio or API users to write, reveal, or export the underlying configured value.
- **FR-018**: The system MUST support extensible secret types with compatibility metadata and validation.
- **FR-019**: The first release MUST include text value, RSA key material, and X.509 certificate reference types.
- **FR-020**: Secret types MUST declare which stores they support.
- **FR-021**: Secret stores MUST declare their capabilities so Studio and APIs can prevent unsupported operations.
- **FR-022**: The system MUST expose a Studio management area under security or settings for authorized users.
- **FR-023**: The Studio management area MUST support searching, filtering by status/type/store/scope, creating, editing metadata, rotating, revoking, deleting, testing, and opening secret details.
- **FR-024**: The system MUST expose a reusable Studio secret picker for workflow inputs and module settings.
- **FR-025**: The picker MUST support filtering by compatible type, store, scope, status, and consuming context.
- **FR-026**: The picker MUST support inline creation when the selected store/type combination and the current user allow it.
- **FR-027**: Sensitive workflow inputs MUST be eligible to store secret references instead of literal values.
- **FR-028**: Workflow definitions and module settings MUST persist references rather than resolved secret values when a secret is selected.
- **FR-029**: Runtime consumers MUST resolve secret references only at the point of use.
- **FR-030**: The system MUST define permissions for metadata read, metadata write, value update, deletion, runtime use, testing, import, and encrypted export.
- **FR-031**: The first implementation MAY permit anonymous/local development access where the host has not enabled authorization, but the permission model MUST be represented in contracts and ready for enforcement.
- **FR-032**: Security-relevant secret operations MUST emit audit-ready events or records without raw values.
- **FR-033**: The system MUST NOT provide a cleartext reveal endpoint or UI for current secret values after creation.
- **FR-034**: The system MUST avoid raw secret values in logs, validation errors, audit records, diagnostics, and export/import messages.
- **FR-035**: The system MUST provide import/export contracts that export references safely and reserve value export for explicit encrypted export.
- **FR-036**: Same-name import conflicts MUST be errors unless the import operation explicitly chooses create-new, update/rotate, or skip.
- **FR-037**: The first implementation SHOULD keep external vault providers out of scope while preserving extension points for later providers.
- **FR-038**: The Studio module MUST detect or report when the backend Secrets feature is unavailable.
- **FR-039**: The system MUST provide automated tests proving metadata responses, picker responses, saved workflow definitions, logs, and audit records do not contain submitted raw secret values.
- **FR-040**: The system MUST provide automated tests proving lifecycle, resolution, store/type compatibility, and Studio picker behavior.

### Key Entities *(include if feature involves data)*

- **Secret**: A logical named item that users and modules reference. It has an immutable technical name, display metadata, type, store, scope, status, timestamps, and versions.
- **Secret Version**: A specific value or external-reference version for a secret. It has a version number, status, creation time, optional expiration, and store-owned payload metadata.
- **Secret Reference**: The serializable value stored by workflow definitions or module settings. It identifies the secret by technical name and may constrain type or scope.
- **Secret Store**: A backend or lookup mechanism that can read, write, test, delete, version, or export secret payloads according to declared capabilities.
- **Secret Type**: A classification that defines authoring validation and compatibility rules for a secret, such as text, RSA key, or X.509 certificate reference.
- **Secret Picker Context**: The consuming UI context that filters compatible secrets and inline-creation options.
- **Secret Operation Audit Record**: A security event describing a privileged operation without containing secret value material.
- **Secret Export Item**: A safe export representation containing references and metadata, plus encrypted value material only when explicitly requested and permitted.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can create a text secret, select it for a sensitive workflow input, save the workflow, and verify the saved definition contains the secret reference but not the submitted raw value.
- **SC-002**: Automated tests prove list, detail, picker, and test responses contain zero raw secret values or encrypted store payloads.
- **SC-003**: Automated tests prove rotation produces exactly one active latest version and at least one retired previous version.
- **SC-004**: Automated tests prove missing, revoked, expired, deleted, type-incompatible, scope-incompatible, and unavailable secrets fail resolution with safe errors.
- **SC-005**: A configuration-backed secret can be created from lookup metadata and resolved from application configuration without persisting the configured value as secret material.
- **SC-006**: Studio renders a secrets management route and a reusable picker that can create, select, rotate, revoke, delete, and test secrets without revealing current values.
- **SC-007**: At least three secret types and two store kinds are discoverable through descriptors and enforce compatibility rules.
- **SC-008**: A scan of saved workflow definitions, API responses, logs captured during tests, and audit records finds no submitted raw secret value.
- **SC-009**: Feature registration tests prove backend and Studio modules can be enabled through the shell feature model.
- **SC-010**: The quickstart validation demonstrates end-to-end creation, selection, resolution, rotation, and revocation in a local development shell.

## Assumptions

- The first implementation prioritizes local Foundation development and simple production deployments over cloud-vault integrations.
- Existing host authentication and authorization features provide the enforcement layer when a secured host is composed.
- Secrets are a new Foundation domain and are not the same as non-retrievable identity credential hashes.
- Import/export value encryption is specified as a contract boundary in this work unit, but full cross-environment encrypted value movement can be delivered after the management, picker, and resolution path is stable.
- Studio implementation happens in the paired Foundation Studio repository while backend contracts and APIs live in Foundation.
- Existing sensitive-input metadata is the primary signal for offering a secret reference editor in workflow authoring.
