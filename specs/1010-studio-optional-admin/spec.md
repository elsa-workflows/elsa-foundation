# Feature Specification: Optional Studio administration

**Feature Branch**: `agent/codex/3359-studio-optional-admin`

**Created**: 2026-10-26

**Status**: Draft

**Input**: [#3359](https://github.com/elsa-workflows/elsa-foundation/issues/3359),
[Program #3340](https://github.com/elsa-workflows/elsa-foundation/issues/3340), following the accepted
[hosting contract](../../docs/reports/studio-hosting-contract.md) and completed infrastructure qualification.
Host developers should add package/feature administration without copying the reference application's classes.

## User Scenarios & Testing

### User Story 1 — Add optional administration to a custom host (Priority: P1)

A host integrator selects Studio package and feature administration, its writable configuration files and its
package drop folder. The base host remains usable when that optional capability is absent. Authorized operators
can inspect packages/features, upload/delete feed packages, reconcile and configure feeds without changing another
host's shell or files.

**Independent Test**: Start custom hosts with/without administration and with a selected non-default shell and
separate configuration/drop-folder locations. Exercise cold requests, permissions, uploads and registration errors.

**Acceptance Scenarios**:

1. **Given** the optional capability absent, **When** the base host starts, **Then** it serves Studio discovery/runtime and does not install administration endpoints or services.
2. **Given** administration configured, **When** an operator requests an endpoint, **Then** existing read/manage policies enforce401/403 and manage-implies-read before any side effect.
3. **Given** explicit shell/file/feed choices, **When** packages or features are changed, **Then** the operation targets only those choices; paths resolve against the host's content root.
4. **Given** multiple directory feeds and no explicit selection, **When** the host starts, **Then** it rejects the ambiguity with a useful diagnostic rather than choosing an arbitrary feed.
5. **Given** malformed uploads or traversal attempts, **When** a package is submitted/deleted, **Then** no file outside the selected feed is read, replaced or deleted.

### User Story 2 — Apply feature configuration without leaks or lost updates (Priority: P1)

An operator reads a feature catalog, edits a complete valid state and saves using the revision they observed.
Secrets stay masked in projections; unchanged masks preserve their stored values. The same mutation coordination
protects all Studio configuration changes. An external source that overrides the writable file remains authoritative.

**Independent Test**: Read/save through public administration endpoints against actual temporary configuration
files, then inspect the serving configuration and persisted bytes. Race two saves and exercise precedence changes.

**Acceptance Scenarios**:

1. **Given** a current revision and valid full state, **When** an authorized save completes, **Then** only the selected shell changes and the next configuration read observes the saved state deterministically.
2. **Given** concurrent saves using the same revision, **When** both run, **Then** one succeeds and the other receives a conflict without silently overwriting the winning state.
3. **Given** secret-valued settings, **When** the catalog/revision/error is returned, **Then** secret values and defaults are absent; unchanged masks retain the original secret and explicit valid edits use the existing secret-aware rules.
4. **Given** missing mandatory features, invalid settings or dependencies, **When** a full state is submitted, **Then** validation rejects it before replacing configuration.
5. **Given** host-owned configuration or higher-priority providers controlling the selected state, **When** a save is attempted, **Then** it reports read-only/unsupported and does not falsely report a different file as applied.
6. **Given** a write fails or is canceled, **When** another reader opens the file, **Then** it sees a complete original or replacement document, never partial JSON.

### User Story 3 — Inspect and safely prune packages while readers remain active (Priority: P1)

An operator previews package retention and explicitly confirms a supported prune plan. Only a declared,
enrolled store with known protection and complete inventory can execute it. Active, recovery and retained-reader
packages stay protected. Browser package assets remain available through supported reads without racing deletion.

**Independent Test**: Use a real enrolled store, real installed packages and a retained serving-generation reader.
Exercise preview/confirmation, physical deletion and actual HTTP asset reads/ranges overlapping reconciliation.

**Acceptance Scenarios**:

1. **Given** an ordinary or incompletely enrolled host, **When** prune is previewed/executed, **Then** diagnostics explain why execution is unavailable and no package deletion occurs.
2. **Given** a supported inactive-version retention choice, **When** a preview completes, **Then** it identifies retained/deletable candidates without physical mutation and supplies the exact confirmation binding required for execution.
3. **Given** explicit matching confirmation and complete protection knowledge, **When** execution completes, **Then** obsolete unprotected content is physically removed while active/recovery/live-use packages remain usable.
4. **Given** stale, altered or missing root/epoch/retention confirmation, **When** execution is requested, **Then** it refuses rather than broadening the confirmed operation.
5. **Given** a package asset read, **When** reconciliation/pruning overlaps it, **Then** protection remains in force until all package bytes are detached or the supported stream finishes; traversal/link escapes are rejected and responses revalidate with ranges preserved.
6. **Given** asset size/concurrency limits, **When** a request exceeds the supported bound, **Then** it fails predictably without allocating an unbounded payload or releasing protection before reading.
7. **Given** legacy count/age settings, **When** retention is shown or pruning requested, **Then** their incompatibility is explicit; execution requires deliberate selection of the supported inactive-version policy instead of silently reinterpreting legacy values.

### Edge Cases

- A package disappears or changes between catalog lookup and admission: return a bounded retryable/not-found outcome, never an unprotected physical-path fallback.
- Cancellation releases mutation/admission ownership and owned temporary upload/write files.
- Feed configuration changes require restart and must not redirect installed drop-folder identity in the running host.
- Missing shell files, invalid JSON and invalid selected feed names are actionable startup/operation errors.
- Root-owned administration/catalog services survive shell replacement; request services are not retained by singletons.
- Confirmation binds the observed root identity, enrollment epoch and retention policy. Execution replans under ownership; it does not promise an unchanged candidate list when same-epoch package state changes. Newly protected packages must remain protected.

## Requirements

- **FR-001**: Host developers MUST select administration through public package composition without copied reference-host implementation or mandatory backend identity/cluster services.
- **FR-002**: Administration MUST remain optional to the base host and enforce existing Studio read/manage authentication/permission behavior.
- **FR-003**: Shell, configuration and drop-folder identity MUST be explicit, content-root based and consistent across reads/writes/reload.
- **FR-004**: Feature edits MUST reuse secret-aware full-state validation and revision semantics, preserve mandatory Studio functionality and refuse unsupported writes.
- **FR-005**: All Studio configuration writers MUST coordinate complete read/revision/modify/atomic-replace/reload cycles with the shared process-lifetime mutation gate.
- **FR-006**: Package pruning MUST use the qualified maintenance protocol with exact explicit confirmation and complete protection knowledge; no local deletion fallback is permitted.
- **FR-007**: Retention configuration and operator controls MUST describe supported inactive-version retention, expose legacy incompatibility and require deliberate migration.
- **FR-008**: Package asset access MUST preserve protection through actual reads, reject traversal/link escapes and bound memory/concurrency while retaining MIME/range/revalidation behavior.
- **FR-009**: Root/shell lifetimes MUST use supported infrastructure sharing and scoped access; no duplicated loader, catalog refresher or reload observer is introduced.
- **FR-010**: Reference Web MUST adopt the public optional composition and remove superseded administration/store/asset wiring while keeping its selected in-box modules.

## Key Entities

- Installed administration choices: selected shell, configuration sources and directory-feed identity fixed for the running host.
- Feature catalog/edit state: existing enabled/configured feature projections, secret masks and revision token.
- Package-store preview/confirmation: existing root identity, enrollment epoch, retention choice, protection/inventory diagnostics and candidate outcomes.
- Asset read: selected package/path, admitted ownership and a bounded detached payload or supported protected stream.

## Success Criteria

- **SC-001**: Custom hosts add/remove the capability through package references and public composition, with zero copied Web administration classes.
- **SC-002**: Every unauthorized, invalid, stale or unsupported operation produces its expected refusal with zero unintended writes/deletions or secret disclosure.
- **SC-003**: Concurrent feature saves preserve exactly one winning full document and deterministically expose its applied state.
- **SC-004**: A confirmed supported prune physically removes an obsolete package while retained serving readers and concurrent protected HTTP asset reads remain valid.
- **SC-005**: The reference host and focused optional/base consumer cases pass without activating publication or deployment.

## Assumptions and boundaries

- The owner authorized this named program end to end; this is its existing #3359 leaf, not a new program or issue.
- #3358 supplies Hosting's common authorization/bridge contract; its reviewed implementation may await CI/merge while this leaf is developed locally.
- Protected bounded package reads support stock extracted, immutable published installs under complete native admission; archive/unenrolled/custom mutable profiles refuse. Arbitrary out-of-protocol writers are unsupported.
- The accepted hosting/package-store reports settle package direction and maintenance policy. Trusted roster/enrollment remains on its owning infrastructure's configuration surface.
- Configuration mutation is coordinated within the process. Arbitrary external writers and cross-process transactions are unsupported.
- Full isolated NuGet/browser/custom-module/large-asset qualification remains on #3363–#3365; this leaf must nevertheless implement and test its production bounds and actual protected HTTP behavior.
- Theme extraction, prebuilt browser ownership and final distribution thinning remain #3360–#3362.
- No release, deployment, publisher activation, general configuration framework or new identity implementation is included.
