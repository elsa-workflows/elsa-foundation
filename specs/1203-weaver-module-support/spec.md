# Feature Specification: Module-owned Weaver support for the current Studio inventory

**Current naming and upgrade baseline (12 November 2026)**: the owner-authorized [#4027 rename](../../docs/reference/package-renames.md) moves the Studio assistant to `Elsa.Weaver.*` and `api.weaver`; user-defined agents keep `Elsa.Agent.*`. This is an identifier/input reconciliation, not a scope or acceptance waiver. The rename starts a fresh Weaver store. The additive upgrade in this work unit is post-rename, pre-effects Weaver1.0 → Weaver1.1, with genuine actual-writer evidence. Pre-rename Agent-store captures remain unchanged historical evidence and cannot qualify that upgrade. All legacy digest, protected-content, counter-lifetime, hold/finalization and no-replay requirements apply within the Weaver family. Contracts remain Preview until the required composed and independent-package journeys pass.

**Feature Branch**: `agent/claude/3443-weaver-module-support`

**Created**: 2026-11-03

**Status**: In progress

**Input**: [Feature #3443](https://github.com/elsa-workflows/elsa-foundation/issues/3443), which is M4 ([#3433](https://github.com/elsa-workflows/elsa-foundation/issues/3433)) of [Program #3428](https://github.com/elsa-workflows/elsa-foundation/issues/3428). The feature is [in progress](https://github.com/elsa-workflows/elsa-foundation/issues/3443#issuecomment-6612633571).

Delivery is authorized by the Program owner's [standing authorization](https://github.com/elsa-workflows/elsa-foundation/issues/3428#issuecomment-6615643669), within #3443's boundaries. This lane reads the owner decisions under Assumptions as amending those boundaries.
- **#3443's Boundaries** exclude blanket administration permission, arbitrary workspace editing, and package promotion or activation.
- **The authorization record** itself excludes releases, deployments, provider purchases, credential extraction and publisher activation.

The scope comprises:
- the M0 inventory in [docs/reports/weaver-baseline.md](../../docs/reports/weaver-baseline.md);
- #3443's planning comments of 30 and 31 October;
- the `/user-tasks` and `/agents` rows that the [claim](https://github.com/elsa-workflows/elsa-foundation/issues/3443#issuecomment-6605270638) adds;
- the screens merged since: `/settings/backends` (PR #3600) and the BPMN pages (PR #3603).
- the 12 November source refresh adds the composed Decisions surfaces under FR-001/later leaf22; source discovery is not live-host acceptance.

The prerequisites #3440, #3441 and #3442 are complete.

**Review**: The PR records the cumulative independent correctness and standards review on 3 and 4 November 2026. Round 22's interrupted correctness review was resumed with fresh reviewers in round 23. Every finding is verified against `origin/main`; the approval basis is the standing authorization and the owner decisions under Assumptions.

This spec contains:
- the M4 delivery matrix (FR-001);
- the cross-cutting requirements every leaf must meet;
- leaves 0–3, as stories 1–4.

Each later leaf gets its own spec (FR-025). The plan PR, which adds `plan.md`, also adds `contracts/privilege-policy.md` and `contracts/outcomes.md`.

**Terms**: use [Studio/CONTEXT.md](../../Studio/CONTEXT.md), the [Elsa glossary](../../docs/glossary/elsa.md) and [spec 182](../182-dormant-features-until-finalization/spec.md). Leaf 0 adds the new terms and links in FR-021. *Performed* means an approved action ran without a persisted change (FR-016); privilege classes and module codes follow FR-015/016. A connection-owned secret is identified by the Connections convention (FR-006); secret consumers are distinct from workflow Secret References.

New contributor interfaces use `I<X>Extension` under ADR 0080 D5. Existing domain names, including Attention, stay unchanged until their own rename batch.

**Hosts**:
- **Normal host**: the Workbench backend (`Elsa.Workbench`) with Studio (`Elsa.Studio.Web`), with Design and Runtime combined, in the Development configuration, with the in-memory Weaver store. Leaves 1 and 2 add their Weaver packages to it (FR-002). It runs only shipped code, and does not compose OAuth2 (`IntegrationsOAuth2`).
- **Durable host**: the normal host plus the Weaver EF store, which the overlay of the Workbench's durable-store test adds. Secrets EF is already composed. From leaf 2, it also includes the Connections EF store.
- **Probe host**: the conformance probe's packaged consumer. It carries each leaf's test fixtures, such as fault stores, a test connection type and the OAuth2 composition, so every scenario that needs test code runs there. Its restart configuration enables the Weaver EF store. Leaves 1, 2 and 3 change it (FR-002).

## User Scenarios & Testing

Each story's test also covers the common controls (FR-009), the common outcomes of each effect (FR-014) and the canary search (FR-023). Every tool and effect has a conformance-probe case on both provider adapters (FR-022). Each step runs in one host.

### User Story 1 - Explicit answers, the privilege policy and *performed* (leaf 0) (Priority: P1)

A user asks about a module that is not installed or is disabled, or about a page this feature does not support. Weaver answers from two live sources: the Studio module registry, which module load status shows, and the Weaver contributions each host registers. It never answers from a route, or from M0's historical list.

The user also sees:
- which privilege classes the integrator has enabled;
- which classes each effect needs, in its review;
- *performed*, for an approved effect that persists nothing.

**Why this priority**: Every later leaf depends on this one.

**Independent Test**:
1. In the normal host, run leaf 0's inventory refresh. ExtensionBuilder is disabled by default.
2. Deselect structured logs: the backend feature, its EF store (its only dependant) and its Studio module. Ask Weaver about structured logs and about the extension builder, open `/diagnostics/structured-logs`, and ask again.
3. Enable ExtensionBuilder in the backend configuration and in Studio's shells. Open it, and ask again.
4. In the probe host, extend the fixture module with:
   - an administrative effect;
   - an administrative and irrecoverable effect;
   - an `Admin` effect that declares only the irrecoverable class;
   - an effect whose module raises its risk to `Admin` at preparation;
   - an effect that persists nothing;
   - a tool and a context provider that each declare a module-code set and emit a code outside it, one of them echoing a canary;
   - an authorization violation, a preparation error and an execution error that each carry such a code, one of them owner-unavailable, and one whose message echoes a canary.
5. Propose each effect under each of these six conditions:
   - the classes unconfigured;
   - only the administrative class enabled;
   - all three classes enabled, as in the Workbench's base configuration;
   - a class switched off between review and approval;
   - a class switched off between approval and execution;
   - a class switched off before recovery.
6. In the probe host, deselect the fixture module while a page nests its widget.
7. In the probe host's restart configuration:
   1. Leave an effect pending, restart, remove the fixture module, approve, and execute.
   2. Place an operator hold on the Weaver schema family.
   3. Simulate the leaf 0 upgrade, with pre-upgrade rows of every kind (FR-018), one server effect pending approval, one server effect approved before dispatch, and one approved client invocation waiting.
   4. While the hold keeps 1.1.0 unfinalized, check the store and prepare the fixture's tools.
   5. Release the hold, which finalizes 1.1.0, and check again.

**Acceptance Scenarios**:

1. **Given** a module that the registry does not list, **When** the user asks about it or opens its route, **Then** Weaver answers that it is not installed, and the route is the only context it discloses.
2. **Given** a disabled module, or an enabled module without Weaver contributions, **When** the user asks for help on its page, **Then** Weaver says so and offers no tool.
3. **Given** a deselected module, **When** a page that would nest its widget is open, **Then** nothing of that module is offered, and other modules' support is unchanged.
4. **Given** unconfigured classes, **When** any classed effect is proposed, **Then** it is refused before review.
5. **Given** only the administrative class enabled, **When** an administrative and irrecoverable effect is proposed, **Then** it is refused before review. **When** an administrative-only effect is proposed, **Then** it is reviewed.
6. **Given** all three classes enabled, **When** each effect is proposed, **Then** it is reviewed, and its review shows the classes it needs.
7. **Given** an `Admin` effect that declares classes without the administrative class, **When** it is prepared, **Then** Weaver core adds the administrative class, and the review shows it.
8. **Given** an effect whose module raises its risk to `Admin` at preparation, **When** it is prepared, **Then** its effective risk is the higher of the two, and it needs the administrative class.
9. **Given** a bootstrap request, which carries no session, **When** the host answers, **Then** the answer reports every class's state, and Studio shows the states read-only.
10. **Given** an effect whose class is off at approval, execution or recovery, **When** that step is reached, **Then** the proposal is refused and ends there. Nothing is dispatched, and a new proposal is needed once the class is enabled.
11. **Given** an approved effect whose owning module persisted nothing, **When** it is recorded, **Then** it is *performed*, with the no-persisted-effect scope, and the review shell shows it.
12. **Given** a code outside the declared set, **When** it is recorded in a receipt, refusal, preparation or execution error, authorization violation or context diagnostic, **Then** it is replaced by a generic code. The generic code keeps the original classification, owner-unavailable included, and no canary is disclosed, in the code or in the message.
13. **Given** a pending effect whose module is removed, before or after a restart, **When** it is approved, **Then** approval succeeds on Weaver core's own checks. **When** it is then executed or recovered, **Then** it is unavailable, with zero effect.
14. **Given** a server effect pending approval across the leaf 0 upgrade, **When** it is approved and executed, **Then** its empty class set leaves its digest unchanged, and it executes subject to the current privilege policy. A legacy `Admin` preparation still requires the administrative class; switching it off refuses approval/execution with zero dispatch. A classed proposal cannot predate leaf 0.
15. **Given** a server effect approved before dispatch across the upgrade, **When** it is executed, **Then** its original digest is preserved and execution still applies the current privilege policy, including the administrative requirement for a legacy `Admin` preparation.
16. **Given** either of those effects, where the upgrade outlasted its prepared deadline, **When** it is executed, **Then** it is refused with `weaver.proposal.expired`.
17. **Given** an approved client invocation waiting across the upgrade, **When** it is resumed, **Then** spec 1189's restart protocol applies, with the current privilege-policy veto still enforced.
18. **Given** pre-upgrade rows of every kind, **When** the upgraded store reads them, **Then** they read with default values.
19. **Given** an Weaver EF store whose 1.1.0 is not finalized, **When** an operation of the new leaf 0 feature is prepared, **Then** it is refused before review with `schema-version-not-finalized`. The feature catalog, Attention and `/capabilities` report that feature as dormant; existing Weaver features remain available. **When** the hold is released, **Then** 1.1.0 finalizes, and the new feature is available.
20. **Given** a host with the in-memory Weaver store, **When** such a tool is prepared, **Then** it is available, because that store carries no dormancy requirement.

---

### User Story 2 - Inspect, describe and test Secrets without exposing a value (leaf 1) (Priority: P2)

A user on the Secrets page (`/security/secrets`, or its alias `/secrets`) asks which secrets exist, why one is expired, or whether one is usable, or asks Weaver to improve a description.

Weaver reads each secret's metadata:
- name, display name and description;
- type, store, scope, tags and status;
- the effective condition and the latest version's expiry, both computed by the owning module;
- current version, revision, and creation and update times.

It never discloses a value. Weaver can propose a metadata update, or the owning module's value test.

**Why this priority**: Secrets is the smallest row with the highest exclusion risk, and it sets the pattern for the other rows.

**Independent Test**:
- **Normal host**, with shipped code: seed an active secret, one whose latest version has expired, and a revoked secret, each holding a canary value; a configuration-store secret whose entry is missing; and a secret in another tenant. Read the list on both routes, improve a description and test the secrets. Compare each receipt with the owning module's readback.
- **Probe host**, with leaf 1's fixtures: a test secret store whose test message and test code both echo the value.

Negative cases:
- no `secrets:read`, no `secrets:write`, or no `secrets:test`;
- a target in another tenant;
- an edit or rotation in another tab after review;
- a test of a missing or inactive secret, and of the configuration-store secret whose entry is missing;
- the Secrets EF store deselected;
- the administrative class off;
- in the durable host, a restart with an approved effect pending;
- requests to create, rename, reveal, rotate, import, export, revoke or delete.

**Acceptance Scenarios**:

1. **Given** the list with a filter and a selection, **When** Weaver reads context, **Then** it receives the filter, the selected identities and their metadata, bounded by `secrets:read` and the tenant. An expired secret shows as expired.
2. **Given** an approved description update, **When** it is executed, **Then** it is saved at the reviewed revision, and the receipt carries the readback. **Given** a repository that is not revision-aware, **When** the update is proposed, **Then** it is unsupported.
3. **Given** an approved test, **When** it runs at the reviewed version, **Then** it is *performed*. The receipt carries only success or failure and a declared module code. **When** the secret is missing or inactive, or its configuration entry is missing, **Then** the test is refused, with a declared code for each case.
4. **Given** a request to create, rename, reveal, rotate, import or export, **When** Weaver receives it, **Then** it refuses on the grounds in FR-001. **Given** a request to revoke or delete in leaf 1, **When** Weaver receives it, **Then** Weaver says the operation is not yet supported.

---

### User Story 3 - Understand, prepare, update, rebind, test, revoke and delete a Service connection (leaf 2) (Priority: P3)

A user on the Connections page, in its creation form with its secret picker, or at a connection picker in a workflow property form asks what a connection is or why it fails. The user may also ask Weaver to prepare, update, rebind, test, revoke or delete one.

Through a new read in the owning module (FR-006), Weaver sees:
- name, display name, type, authentication scheme and status;
- settings field names and secret-bound field names;
- managing party and Owner field;
- tags, last test time and version.

Bound non-owned secret names and named-credential names are disclosed only under `secrets:read`. Settings values are never disclosed.

**Why this priority**: "Why does my integration fail" is the commonest Connections question.

**Independent Test**:
- **Normal host**, with shipped code: for a connection of a shipped type, explain it, prefill the creation form, update it, rebind it, revoke it and delete it. Open a workflow property form with a connection picker while an Operation Batch is pending. Seed active, expired and inactive text secrets, a non-text secret, another connection's owned inline secret, and a connection in another tenant.
- **Probe host**, with leaf 2's fixtures, which compose OAuth2: use a test connection type that records invocations, carries canary token fields, and returns a test message that echoes a resolved value. Seed:
  - a connection with canary settings, secret and token values;
  - a connection with an owned inline secret, an OAuth grant secret and PKCE secrets;
  - an OAuth2 connection with PKCE secrets but no grant;
  - another connection's grant and PKCE secrets;
  - a hand-made secret whose name lies under a connection's `connection:<name>:`;
  - a connection with more owned secrets than the enumeration's safety bound.

  Explain, test and delete.

In both, compare each receipt with the owning module's readback.

Negative cases:
- an edit, or a user-run test that ran, after review;
- no `workflows/design:read` and no `connections:manage`, for reads;
- no `secrets:read`, for bound names, the picker and a rebind;
- no `connections:manage`, for the prefill, update, rebind, revoke or delete;
- no `connections:bind-secret`;
- no `connections:test`;
- a target in another tenant;
- a rebind to an expired, inactive or non-text secret, or to any connection-owned secret, hand-made ones included;
- a rebind away from the owned inline secret, where cleanup fails after commit;
- a test that cannot run, because the connection or its type is not found; one that fails; and one whose write fails after the external call;
- a test message or code that echoes a resolved value;
- a dropped test response;
- a delete that removes owned secrets and then fails to remove the connection;
- a delete of a connection with an OAuth grant or PKCE secrets where the cleanup is not available;
- a delete whose enumeration page cannot be read, or whose owned secrets exceed the safety bound;
- a connection selected in the nested picker while an Operation Batch is pending;
- the creation form closed before the prefill completes;
- the administrative or irrecoverable class off;
- in the durable host, a restart with an approved effect pending;
- requests to create a connection, authorize OAuth, change the redirect URI, bind a named credential, replace an owned inline secret's value, or set a settings value.

**Acceptance Scenarios**:

1. **Given** the creation form, **When** Weaver reads it, **Then** it sees the type, scheme, field names and validation errors, and no entered value. The form's secret picker is a Connections contribution that lists candidates under `secrets:read`.
2. **Given** a workflow property form with a connection picker and a pending Operation Batch, **When** Weaver reads context, **Then** Connections contributes the picker's own kind. Selecting a connection in the picker never retargets the batch (FR-010).
3. **Given** an approved prefill under `connections:manage`, **When** it is applied, **Then** only the form's name, type and scheme change, and the prefill is applied locally. Creating the connection stays the user's action.
4. **Given** an approved update or rebind, **When** it is executed, **Then** it applies at the reviewed version, under `connections:manage`. A rebind also needs `connections:bind-secret`, `secrets:read`, and an active text secret in the tenant that is not connection-owned. Ownership is decided by name, so no query of other connections is needed, and a hand-made secret whose name lies under `connection:<name>:` is refused, which is the safe direction. Execution rechecks the permissions and the secret. The candidates shown to the client grant nothing. The native rebind is unchanged.
5. **Given** a rebind, **When** it is reviewed, **Then** the review states that the connection sends the new secret's value to the endpoint it is configured for, that Weaver does not disclose that endpoint, and that the user should check it on the connection page.
6. **Given** a rebind away from an owned inline secret, **When** it is reviewed, **Then** it needs the administrative and irrecoverable classes. The review states that:
   - the secret is deleted after commit unless another connection still uses it;
   - only other connections are checked, so workflow Secret References and Nuplane feed credentials are not detected;
   - the deletion cannot be undone.

   If cleanup fails after commit, the effect is persisted with a warning.
7. **Given** an approved test, **When** it runs, **Then** the reviewed version is checked before the external call. When the type's test runs, the owning module records the test time and advances the version, and other pending proposals for that connection go stale. The receipt is persisted with the result and a declared module code, and the message is redacted. **When** the test cannot run, **Then** it is refused, with its declared code. **When** its write fails after the external call, **Then** it is *uncertain*.
8. **Given** a revoke or delete, **When** it is reviewed, **Then** it needs the administrative and irrecoverable classes. The review states that the action cannot be undone, and that consumers which resolve the connection by name are not detected: workflows, ETL bindings and specialist model profiles. For a delete, the review also lists the secrets that will be deleted: the owned inline secret fields handled today, the deterministic OAuth grant name, and names matching the OAuth PKCE nonce pattern. Hand-made names that merely share the inline-field prefix are kept. It states that only other connections are checked for those secrets, so workflow Secret References and Nuplane feed credentials are not detected. A secret that another connection still uses is kept.
9. **Given** a connection with an OAuth grant or PKCE secrets, **When** a delete is proposed and the composed Connections cannot delete them (FR-006), **Then** Weaver refuses it before review.
10. **Given** a delete whose enumeration of owned secrets cannot read a page, or finds more than the safety bound, **When** it is proposed or executed, **Then** it is refused before any side effect, and no secret is left behind.
11. **Given** an approved revoke or delete, **When** it is executed, **Then** the reviewed version is checked before any side effect. A delete's receipt reads back the connection's absence. A delete that removes owned secrets but then fails to remove the connection throws, so it is *uncertain*.

---

### User Story 4 - Revoke and delete a secret, knowing its connection consumers (leaf 3) (Priority: P4)

On the Secrets page, a user asks Weaver to revoke or delete a secret. The review shows the consumers that Connections reports: the connections whose fields bind the secret, and whether the secret is connection-owned. Weaver refuses connection-owned secrets, and states what it cannot detect.

**Why this priority**: Irrecoverable secret effects are safe only once their consumers are known, as far as they can be known.

**Independent Test**:
- **Normal host**, with shipped code: seed a secret bound by two connections, an unused secret and an owned inline secret. Revoke and delete through Weaver.
- **Probe host**, with leaf 3's fixtures, which compose OAuth2 and use a test secret store that really removes values plus repository save and revision-conflict faults. Seed an OAuth grant secret, a PKCE secret, a hand-made secret under `connection:<name>:`, and secrets whose delete removes the value and then fails to save the metadata or meets a revision conflict at save.

In both, compare each receipt with the owning module's readback.

Negative cases:
- a binding added or removed after review, before execution;
- a binding created after the last consumer comparison but before the secret mutation, coordinated by a barrier (the non-atomic window below);
- a truncated answer whose page or count changes;
- no consumer source answering;
- a repository that is not revision-aware;
- an actor who cannot read connections;
- no `secrets:delete`;
- the partial deletes;
- the administrative or irrecoverable class off;
- in the durable host, a restart with an approved effect pending.

**Acceptance Scenarios**:

1. **Given** a revoke, **When** it is reviewed, **Then** the review states that the revoked version's value cannot be restored, and that rotating reactivates the secret with a new value. **Given** a delete, **When** it is reviewed, **Then** the review states that it cannot be undone. Both need `secrets:delete` and the administrative and irrecoverable classes.
2. **Given** a review, **When** consumers are listed, **Then** the list is advisory. It shows the binding connections by name when the actor meets the disclosure rule the source declares, and as a count otherwise. A truncated answer says so. The review states that workflow references and Nuplane feed credentials are not detected.
3. **Given** a secret that the source reports as connection-owned, hand-made ones included, **When** a revoke or delete is proposed or executed, **Then** Weaver refuses it. This is stricter than the native page, and the user can act there instead.
4. **Given** no consumer source answered, **When** a revoke or delete is proposed, **Then** it is refused before review. The refusal states that connection bindings could not be checked. This covers five cases: Connections is not composed, Connections is an older version without the source, Connections runs in another shell, the event pipeline is not composed, or the query is unsupported. This limitation is recorded: a host without Connections cannot revoke or delete secrets through Weaver.
5. **Given** a repository that is not revision-aware, **When** a revoke or delete is proposed, **Then** it is unsupported.
6. **Given** an approved revoke or delete, **When** it is executed, **Then** the consumer answer is recomputed and compared, by full identity, with the answer the review showed. A truncated answer is compared by its page and count. A change observed by that comparison makes the proposal stale. The reviewed revision is checked before any side effect. Approval itself checks only what Weaver core holds (FR-014).
7. **Given** a binding created after the final consumer comparison but before the secret mutation, **When** that interval is exercised, **Then** the test proves the plan's declared ordering. An owner-supported atomic fence may refuse the stale operation with zero effect. Without such a fence, the remaining cross-module window is explicitly recorded under PD3: the review warns that concurrent bindings may be missed, the consumer answer is attributed to its observation rather than claimed current at deletion, and neither receipt nor UI claims the secret was unused at deletion. The consumer list is advisory; the secret's own revision guard still applies.
8. **Given** a delete, **When** it runs, **Then** its receipt reads back the secret's absence. If the value is removed but the metadata save then fails or meets a revision conflict, the owning module throws, and the delete is *uncertain*.

### Edge Cases

- **A module unloads mid-review:** approval succeeds on Weaver core's own checks. Execution finds the module gone, and the effect is unavailable, with zero effect. Context is re-admitted without the module (FR-009).
- **A shell reloads during an effect:** an effect whose response is lost is *uncertain*, and is never replayed (FR-014).
- **A secret is rotated after review:** execution finds the revision changed, so the effect is stale.
- **A connection's display name is changed, or the connection is deleted, under a pending proposal:** execution's version or identity check makes the proposal stale. Connections cannot be renamed.
- **A dense page overflows the attachment budget:** the collection is refused as the budget defines, and FR-011's tests prevent this at default sizes.
- **Connections is composed in another shell:** no consumer source answers, so secret revoke and delete fail closed (story 4).
- **A hand-made secret lies under `connection:<name>:`:** it is treated as connection-owned (FR-006).
- **The Weaver EF store has not finalized 1.1.0:** M4 tools use the new leaf 0 feature for classed, module-code and performed operations. Its static dormancy requirement is checked at preparation (FR-018); existing Weaver features remain unaffected.
- **An upgrade outlasts the prepared deadline:** an effect prepared before it is refused with `weaver.proposal.expired`.

## Requirements

### Functional Requirements

#### Delivery matrix

- **FR-001**: #3428 is authoritative for Program status. #3428 owns the acceptance matrix; this table supplies its linked M4 detail. A PR changing that detail reconciles the corresponding Program rows without replacing their acceptance authority.
  - **Inventory refresh**: leaf 0 MUST refresh the normal host's live, composed module inventory, and record the result on #3443.
  - **Changed and new rows**: leaf 0's PR updates any row the refresh finds absent or changed. It adds any screen the refresh finds that the table lacks, and assigns it under FR-025.
  - **Outcomes and grounds**: every row MUST end at its outcome and evidence. Every narrowing, and every existing owner operation that Weaver does not offer, MUST state its grounds.

  Outcome words:
  - **Read**: read and explain.
  - **Reviewed**: a reviewed server effect.
  - **Local**: a reviewed edit of visible, unsaved state.
  - **Delivered by #3442**: M3 delivered it.
  - **None in M4**: not delivered in M4.

  Grounds:
  - **A**: absent or disabled in the refreshed inventory.
  - **B**: the Program boundary: secret values (#3433), package promotion and activation (#3443), and publisher activation (#3428).
  - **C**: a credential, file or value would pass through Weaver.
  - **D**: the owning module does not hold it durably or cluster-wide.
  - **H**: a human's decision.
  - **L**: a lane decision: this spec's Program-authority choice, open to owner review.
  - **N**: no owner operation exists.
  - **O**: owned by another issue, spec or open decision.
  - **S**: the screen does not show it.
  - **T**: outside a post-M0 row's target, which is an L choice.
  - **V**: the write is reachable only from the browser, so the server cannot verify its result.
  - **W**: needs a module-owned working-state adapter.
  - **X**: needs its own exclusion design first.

  Targets are quoted from the baseline. Feature selection apply is delivered through the activation class (owner decision 2).

  | Row (owning module) | Target | Outcome; narrowings (grounds) |
  | --- | --- | --- |
  | Secrets (Secrets) | "Reference/metadata/availability inspection; domain-authorized effects, with no secret values in context" | Read; Reviewed metadata update and value test (story 2), revoke and delete (story 4). The connection form's Secret picker is a Connections surface (story 3). Value-bearing create and rotate (B); reveal (C); configuration-key create and rotate (L); rename, import and export (N); the workflow property's Secret editor (O: #3711) |
  | Connections (Connections) | "Metadata, bindings and diagnostics; supported changes/test actions without credentials in context" | Read, including the form's Secret picker and the workflow property form's connection picker; Local prefill; Reviewed update, rebind, test, revoke and delete (story 3). Settings values (X: no field declares its sensitivity); replacing an owned inline secret's value (B); create and OAuth authorize (C); redirect URI (N: a read-only public URL); named-credential binding (L) |
  | Optional sample: Weather (Samples, Studio-only) | "Qualify only when installed; no current composed-support claim" | Read of a client-asserted page context when installed (its leaf spec); None in M4 when absent (A) |
  | Optional ExtensionBuilder (ExtensionBuilder) | "Optional inspection/guidance; no source-control push, package promotion or publisher activation" | Read: installed-module guidance when enabled (its leaf spec). Workspace source inspection (X); None in M4 while disabled (A) |
  | Structured logs (StructuredLogs) | "Current filters/time range/selected records; bounded redacted correlation" | Read (its leaf spec). Time range and retention (S) |
  | Traces (OpenTelemetry) | "Exact trace/span/resource/query with authorized detail and availability" | Read (its leaf spec). Metrics and OpenTelemetry logs (S) |
  | Diagnostics (Studio Hosting and diagnostic modules) | "Accepted/rejected/disabled module state and bounded visible diagnostic results" | Read (its leaf spec). Module administration (L) |
  | Console (`Elsa.Studio.ConsoleStream`, console log streaming) | "Current source/filter/record/truncation/freshness and authorized bounded inspection" | Read (its leaf spec). Filter (S: none exists); server reads of records (X); lines the user attaches are the user's own client-asserted disclosure (FR-009) |
  | Home (Studio Dashboard, Studio Preferences, Attention, Workflows.Dashboard) | "Explain visible widget snapshots, filters, freshness and unavailable data; module-owned supported preferences" | Read; Reviewed personal layout change and Attention Item snooze (its leaf spec). Default layout (L); new widgets and saved queries (O: #3604) |
  | Runtime evidence (Workflows Runtime) | "Explain capture policy and exact proposed configuration differences" | Read; Reviewed Workflow Runtime Diagnostics Setting change within today's capture rules (its leaf spec). Full payloads (O: Studio ADR 0009); a durable, cluster-wide setting (D) |
  | Activity availability (Activities Design) | "Effective availability/diagnostics and supported settings changes" | Read; Reviewed change (its leaf spec) |
  | Features (FeatureManagement client, Modularity server) | "Selected host, revision, filters, settings draft and reviewed differences through existing authorization" | Read; Local draft; Reviewed Server-host apply in the activation class (its leaf spec). Studio-host apply (V); configuration values and edits (C) |
  | Appearance/themes (Studio Hosting, Hosting.Themes) | "Supported preference/theme edits, exact persistence semantics and built-in/read-only constraints" | Read; Local appearance and builder edits (its leaf spec). Theme Store writes (V) |
  | Modules/feeds (Hosting.Nuplane; client Studio Hosting) | "Explain exact host/package/feed/reconcile state; package promotion/activation remains outside authorization" | Read (its leaf spec). Package operations (B); feed operations (L); feed credentials never (#3735) |
  | Backends (Studio Backends) | post-M0: read the profiles | Read (its leaf spec). Profile edits (V) |
  | Settings sections (Studio Hosting) | post-M0: the resolved section and host (#3443, 31 October, item 4) | Read (its leaf spec) |
  | ETL definitions (ETL) | "Exact selected definition/draft, filters, validation and supported authoring" | Read; Reviewed draft update (its leaf spec). Create (W); delete (N); publish (H) |
  | ETL connectors (ETL) | "Exact connector definition, authorized bindings/metadata and validated proposals" | Read; Reviewed draft update (its leaf spec). Create (W); delete (N); publish (H) |
  | ETL runs (ETL) | "Exact run/partition and bounded evidence; supported explicit cancellation" | Read; Reviewed cancellation (its leaf spec). Start (N); completing a run (L: the target names only cancellation) |
  | Activity definitions (Activities Design; Workflows.Publishing; Elsa3) | "Exact catalog/version/dependency inspection and supported draft/import/upgrade proposals" | Read; Reviewed draft add, update and discard, and upgrade plans (its leaf spec). Import (C); publish (H); other definition and version operations, and test runs (L); contract proposals (W) |
  | User tasks (`Elsa.Studio.UserTasks`, Workflows Runtime) | post-M0: read; claim, release, reassign | Read; Reviewed claim, release and reassign (its leaf spec; reassign may be escalated, FR-025). Decisions (H); prefill values and responses (C); form authoring (W) |
  | Agents (user-defined; `Elsa.Agent`) | post-M0: read | Read (its leaf spec). Review decisions (H); the decider's identity (C); authoring (O: #3519) |
  | Decisions (`Elsa.Studio.Decisions`, Decisions) | added after the earlier inventory: the composed `/decisions` library, exact decision/draft/version, table/model editors, saved cases, evaluation and rule-explanation surfaces; workflow decision-version/input editors | Module-owned authorized reads and bounded guidance through later leaf22. Carry exact selection, revisions and unsaved-state provenance; never execute/evaluate user data or mutate/publish merely to answer. The leaf spec must inventory supported owner operations and explicit limitations before implementation; all common authority, redaction, recovery and both-adapter evidence gates apply. |
  | BPMN (Activities BPMN) | post-M0: read | Read (its leaf spec). Run and work operations, artifact registration, interchange management (T); direct publish (H); process authoring (W) |
  | Expressions (Expression Modules) | "Language-owned guidance and exact expression edits without runtime evaluation or live-value capture" | Read; Local expression edits in the draft's working state, as spec 1193's Operation Batch (its leaf spec, with #3711) |
  | Workflow rows (Workflows): definitions list, new workflow, workflow draft, Executables, Runs; Reusable Workflow Templates and library tags, favorites and views | "Preserve search/folder/status/page/selection; read exact resources and propose supported lifecycle changes"; "Carry caller attachment; return typed metadata suggestions or reviewed creation"; "Grounded explain/troubleshoot; atomic visible working-state edits, validation, dirty state and whole-batch undo"; "Inspect immutable compiled structure and selected source reference; dispatch remains a distinct authorized effect"; "Carry exact run/activity-execution/incident/scope; authorized evidence and supported wait/stop/recovery operations"; post-M0: templates and library tags, favorites and views (#3443, 31 October, items 1–2) | Delivered by #3442. Reviewed run wait/stop/recovery controls, supported library deletes and Executables lifecycle including dispatch (later M4 leaf 21, FR-025); edits to published or historical versions and Executables (O: spec 1193) |

#### Packaging and ownership

- **FR-002**: **Server packages.** This spec's Program-authority decision is one Weaver package per owning module.
  - **Name and path**: each package is named `Elsa.Weaver.<Domain>`, at the path the architecture guard derives.
  - **Why the Weaver bucket**: #3428 allows optional integrations to be "separately packaged with their owning module", but the ExtensionBoundary guard forbids core projects from referencing an extension. Framework §2.2's secondary-domain sub-rule does not apply, because these packages define their own Context Kind, tool and result models.
  - **References**: a package references its owning module's `.Core` where one exists, and the Weaver packages in its own bucket. It also references `Elsa.Foundation.Identity.Core` where it evaluates a permission rule itself.
  - **Modules without a `.Core`**: the package admits only client-asserted kinds, and exists only where SC-004 needs it.
  - **Cross-bucket references**: a reference to another extension bucket, such as Etl, is declared in the ExtensionBoundary guard's edge list, with a justification.
  - **Workbench composition**: a leaf that adds an Weaver package makes the Workbench reference it, adds it to the explicit feature-assembly catalog in the Workbench's `Program.cs` beside the existing Weaver packages, and enables it in `shells.json`, `shells.baseline.json` and `docker/compose/elsa-workbench.shells.json`.
  - **Leaf 1**:
    - adds `Elsa.Weaver.Secrets`, which references Secrets `.Core`, and composes it in the Workbench;
    - packages the Secrets API, Secrets EF and `Elsa.Weaver.Secrets` for the probe host, and composes them in its baseline, with Secrets EF on SQLite, so that revision-guarded effects can succeed;
    - adjusts the probe's restart helper;
    - adds probe fixtures: the echoing test secret store.
  - **Leaf 2**:
    - adds `Elsa.Weaver.Connections`, which is distinct from `Elsa.Agent.Connections`, and composes it in the Workbench;
    - adds the Connections EF store to the Workbench project and feature catalog for the durable host;
    - composes Connections, the Connections API and `Elsa.Weaver.Connections` in the probe host;
    - adds probe fixtures: the test connection type, and the OAuth2 composition, which the normal host lacks.
  - **Leaf 3**:
    - adds the `Elsa.Foundation.Identity.Core` reference to `Elsa.Weaver.Secrets`, which evaluates the source's disclosure rule;
    - adds probe fixtures: a test secret store that really removes values, and secret-repository save and revision-conflict faults that stay revision-aware, because the secret manager behaves differently with a repository that is not revision-aware.

  `Elsa.Weaver.Workflows.Runtime` already exists (#3732), and later leaves extend it.
- **FR-003**: **Studio packages.** A Studio contribution MUST live in its owning Studio module, through the public SDK. A satellite package, `Elsa.Studio.Weaver.<Domain>` under `Extensions/Weaver/studio/`, is added only where a host must keep the module but drop its Weaver support. Framework §2.2's sub-rule does not apply to it, because a satellite defines its own Context Kind models. The Built-in Studio Screens are such a case. Their contributions go in `Elsa.Studio.Weaver.Hosting`, which leaf 5 creates, together with its SDK read seams.
- **FR-004**: Each module MUST ship its own Result Renderers and its own bounded, versioned module guidance, in its own packages. Module guidance is trusted, installed-module text, kept distinct from resource content. Shared Weaver code holds neither.

#### Generic changes

- **FR-005**: Weaver core, the shell's collector and review shell, and the provider adapters MUST gain no row's domain knowledge. These are the generic changes:
  - the privilege policy (FR-015), including each class's state in the bootstrap response;
  - the review shell showing an effect's classes and the *performed* outcome;
  - *performed* (FR-016);
  - declared module-code sets and their validation (FR-016);
  - owner-unavailable from a module's preparation and execution errors. Today only tool-authorization violations and context diagnostics can carry that classification, while preparation and execution errors forward only the module's code, status and message. FR-023's canary search covers that message;
  - a principal seam that hands a tool the caller's authenticated principal at preparation and at execution, including the explicit authenticated execute of a recovered approval. Today no principal reaches the tool. Approval calls no module, so there is no approval-time module seam (FR-014);
  - the Weaver store's schema family 1.1.0 and the new leaf 0 feature's static dormancy requirement (FR-018);
  - the registry-based answer of story 1;
  - the probe fixture's effects;
  - doc comments on the risk enum, matching FR-013;
  - SDK read seams for `Elsa.Studio.Weaver.Hosting` (leaf 5);
  - new outcome codes, only where no existing code fits.

  Leaf 0 delivers all of these except the SDK read seams. It also adds a dated pointer, "Amended by spec 1203", to spec 1186's session-policy contract and to spec 1189's recovery contract.

#### Owner prerequisites

- **FR-006**: Where a row needs a read, an authorization or an operation that today exists only behind the owning module's HTTP endpoints, or outside its `.Core`, the owning module MUST supply a `.Core` contract that Weaver calls under the caller's principal.
  - **Placement in `.Api`**: the contract is implemented in the owning module's `.Api` package, because that is where the module's authorization composition lives: its `Elsa.Foundation.Identity.Core` reference and its use of `IPermissionAuthorizationService`. Weaver support for a module therefore requires the module's API feature. This is a stated limitation.
  - **Principal**: the contract takes the principal from the principal seam (FR-005), checks that the actor matches it, and decides permissions through `IPermissionAuthorizationService`.
  - **When checks run**: every check the owning module makes runs at preparation and again at execution, including a recovered approval's execute. This covers its availability, permissions, revisions and versions, the secret-consumer answer and secret ownership.
  - **Failing closed**: the outcome is owner-unavailable when the contract has no implementation, because its `.Api` feature is absent, which the Weaver package classifies as owner-unavailable; or when `IPermissionAuthorizationService` is missing or duplicated, which the implementation detects itself. This holds on the tool path, where preparation runs before the host's tool authorizer, and on the context-provider path, which never passes that authorizer. It is tested once per tool and context provider (FR-014).
  - **Exceptions**: infrastructure exceptions are wrapped (framework §2.23.5).
  - **Writes**: a write to an existing resource checks an expected revision before any side effect. A composition that cannot honor the revision reports it as unsupported, so the write never runs unconditionally. A create uses the owning module's create-if-absent.
  - **Native behaviour**: native pages keep working, each with a regression test. Apart from the #3779 defect fix, no native Secrets or Connections behaviour changes.
  - **Secret ownership**: ownership is decided by name. A secret is connection-owned when its name lies under `connection:<name>:`, the name space that the Connections-owned convention uses for inline secret fields, the OAuth grant secret and PKCE secrets. The convention has one definition in Connections `.Core`, which Integrations' OAuth2 names also use. A hand-made secret whose name lies in that space is treated as connection-owned and refused, which is the safe direction.

  The prerequisites below are contract-level. The plan names signatures, internal types and mechanisms.

  | Owning module and leaf | Prerequisite |
  | --- | --- |
  | Secrets, leaf 1 | A metadata read with the revision, the effective condition and the latest version's expiry. A revisioned metadata update. A value test at an expected version that reports whether it ran. |
  | Secrets, leaf 2 | A generic, bounded, exact-prefix enumeration of secret names in a tenant, paged, with a truncation flag, in every shipped secret repository. It is a Secrets query, not a Connections concept. Today's list search matches substrings, so it cannot serve. |
  | Connections, leaf 2 | A read that adds the version and bindings, omits settings values, and shows bound non-owned and named-credential names only under `secrets:read`. Updates (display name, tags) and rebinds at an expected version, through the Weaver-facing contract. That rebind checks `secrets:read`, refuses as if the secret were unknown when that check fails, and refuses a connection-owned secret by name. The convention is consolidated into one Connections `.Core` definition. A test at an expected version, checked before the external call, which reports whether it ran and a module code; today a missing connection or an unknown type looks like a failed test. Revoke at an expected version. A Weaver delete checks the version and completes bounded enumeration before any side effect, and refuses if that answer is incomplete. Native deletion fixes exactly [#3779](https://github.com/elsa-workflows/elsa-foundation/issues/3779): retain today's owned-field cleanup, also remove the deterministic OAuth grant name and the PKCE nonce-pattern names found by bounded exact-prefix enumeration. Enumeration failure never prevents native deletion: it deletes what it can and reports incomplete cleanup. It leaves hand-made names that merely match the inline-field prefix alone. The leaf PR says "Fixes #3779". The native regression covers incomplete enumeration and those hand-made names; Weaver's stricter preflight refusal is separate. |
  | Secrets, leaf 3 | Revoke and delete at the revision, unsupported without a revision-aware repository. A fan-in (framework §2.6.1): the source contract `ISecretConsumerSource` and the Sequential event `SecretConsumersCollecting` in Secrets `.Core`, with their single aggregating handler in `Elsa.Secrets`. Secrets `.Core` gains a reference to `Elsa.Events.Core`. The Secrets features take no hard dependency on the `Events` feature, so hosts that compose Secrets without it keep working (framework §4.2). The fan-in runs when the event pipeline is composed. When it is not, or no source answers, the result is "not checked", never "no consumers". |
  | Connections, leaf 3 | An `ISecretConsumerSource` implementation in `Elsa.Connections`. Per secret, it returns the binding connections, whether the secret is connection-owned by name, and the disclosure rule for connection names (`workflows/design:read` or `connections:manage`). It is backed by a bounded consumer query that every shipped Connections repository implements, with a truncation flag. A repository that does not implement the query reports it as unsupported. |

- **FR-007**: **Read authority.** A rule that is exactly one permission MAY be checked through the tool's declared permission. Any other rule MUST be decided by the module that owns it. A declared rule is evaluated, never reimplemented: where a review shows another module's names, the Weaver package evaluates the disclosure rule the source declares, and shows a count when the actor does not meet it.
- **FR-008**: Deployment shapes MUST keep working (constitution §E2.2.3). Design-side contributions register only where Design is composed, and Runtime-side ones only where Runtime is. An absent side is explicitly unavailable.

#### Context and disclosure

- **FR-009**: **Common controls.** Every Context Kind and tool MUST have a negative case, tested at the cheapest level that proves it (ADR 0080 D1, framework §2.23), for each of these:
  - **Module removed or unloaded**: nothing of the module is offered, and context is re-admitted without it.
  - **Denied, or a cross-tenant target**: nothing is disclosed, not even existence.
  - **Owner-unavailable**: nothing from the owning module is disclosed.
  - **Stale target**: there is zero effect, and navigation never retargets.
  - **Excluded data**: seeded canaries appear nowhere.
  - **Untrusted content**: resource content and client-asserted kinds are marked untrusted.

  Content that the user explicitly attaches, such as selected console lines, is the user's own disclosure. It is client-asserted and untrusted, and it is excluded from the canary search.
- **FR-010**: Each Context Kind MUST be module-owned, versioned, bounded and authorized. It names the host it describes. It is attributed to one of:
  - the owning module's revision;
  - for a list, the visible query and selection;
  - for unsaved state, the client binding and the base revision.

  Server context providers admit only their own kinds, and produce their own permission and catalog kinds. A tool never takes data from another package's kind. A nested picker contributes its own kind from its own module. A form's own fields belong to the form's module. A nested picker never changes a pending Operation Batch's target.
- **FR-011**: Each Context Kind MUST declare its bounds. Each leaf tests its densest pages, nested pickers included, against the per-collection attachment count, and each kind against the per-attachment size budget ([#3710](https://github.com/elsa-workflows/elsa-foundation/issues/3710)). A session at its disclosure budget is refused.
- **FR-012**: Each disclosure allowlist MUST be authored by the module that defines the data's meaning, and ship in that module's Weaver package. It excludes unlisted fields by default. It is proven by a canary in an unlisted field that the owning module's own redaction misses.

#### Effects

- **FR-013**: Every effect MUST declare its risk, privilege classes, stale check and receipt.
  - **Risk order**: ReadOnly < ReviewRequired < Destructive < Admin. *Destructive* removes or replaces existing state. *Admin* touches sensitive or administrative state.
  - **Effective risk**: the higher of the descriptor's risk and the risk the module reports at preparation.
  - **Gating**: [ADR 0003](../../docs/adr/0003-weaver-direct-apply-risk-classification-is-elsa-owned.md) keeps classification Elsa-owned. Gating comes from the classes.
  - **Unaffected**: spec 1193's client working-state Destructive batches are unaffected.

  Stale checks run at execution (FR-006).

  | Effect | Leaf | Risk | Classes | Stale check | Receipt |
  | --- | --- | --- | --- | --- | --- |
  | Secret metadata update | 1 | `Admin` | administrative | revision | readback; persisted |
  | Secret value test | 1 | `Admin` | administrative | version | *performed*, with success or failure and a module code; refused, with its code, when it could not run |
  | Connection prefill | 2 | `ReviewRequired` | none (`connections:manage`) | client binding | applied locally |
  | Connection update, or rebind of a field whose secret is not connection-owned | 2 | `Admin` | administrative | version | readback; persisted |
  | Rebind away from an owned inline secret | 2 | `Admin` | administrative, irrecoverable | version | readback; persisted, with a warning if cleanup failed |
  | Connection test | 2 | `Admin` | administrative | version, before the external call | persisted, with the result and a module code; refused when it could not run; *uncertain* when the write fails after the external call |
  | Connection revoke | 2 | `Admin` | administrative, irrecoverable | version | readback; persisted |
  | Connection delete | 2 | `Admin` | administrative, irrecoverable | version; owned-secret enumeration complete | absence readback; persisted; *uncertain* when partial |
  | Secret revoke | 3 | `Admin` | administrative, irrecoverable | revision; consumer answer recomputed | readback; persisted |
  | Secret delete | 3 | `Admin` | administrative, irrecoverable | revision; consumer answer recomputed | absence readback; persisted; *uncertain* when partial |

- **FR-014**: **Common effect outcomes.** Every effect MUST be tested, at the cheapest level that proves it, for each of these:
  - **Review**: the effect is reviewed in every autonomy mode, including `full-auto`.
  - **Approval**: approval calls no module or registry. It checks only what Weaver core holds: the live privilege policy against the frozen class set and frozen risk (including the legacy administrative veto in FR-015), the prepared deadline, and the proposal's state.
  - **Refusal**: the policy is read again at preparation, approval, execution and recovery. An effect whose class is off at a step is refused there, and the proposal ends. Nothing is dispatched at execution. An effect without the owning module's permission is refused before review, and again at execution if the permission is gone.
  - **Owner-unavailable**: when the owner contract fails closed (FR-006), at preparation or at execution, the outcome is owner-unavailable, with zero effect.
  - **Module removed after review**: approval proceeds on core's checks, and execution is unavailable, with zero effect.
  - **Restart**: in the durable host, a pending approval survives a restart. A recovered approval runs only on an explicit authenticated execute, which rechecks the classes and everything the owning module holds.
  - **Uncertain**: an effect whose response is lost after dispatch, or that may have run before a restart, is *uncertain*, and is never replayed. For a server effect, the receipt says what to check on the owning page. M4 has no Weaver resolution path for this; it is a recorded limitation (FR-020). For a client effect, the session owner resolves it, as spec 1189 defines.
  - **Local edits**: these carry correlated acknowledgements, and are unavailable, with zero effect, if their page is gone.
- **FR-015**: **Privilege policy** (owner decisions 1, 2, 4 and 5). The Weaver module MUST gain an integrator-configured privilege policy. It is a backend host option, never a session setting. It has three classes:
  - **administrative**: required by every effect whose effective risk is `Admin`. Weaver core adds it when a module's declared set omits it.
  - **irrecoverable**: a module MUST declare this class for its own irrecoverable effects. A third-party irrecoverable effect that is not `Admin` and declares no class is not caught, which is a recorded limitation.
  - **activation**: feature selection apply on the Server host.

  Legacy preparations with an empty class set retain their original digest. At approval, execution and recovery, Weaver core also derives the administrative veto from their frozen `Admin` risk; it never retrofits classes into the old prepared payload. The review shows that effective requirement. Upgrade tests cover pending and already-approved legacy `Admin` operations with that class off and on, proving zero dispatch when off and unchanged digest when on.

  Each effect declares a set of classes and needs all of them. The owning module declares the set at preparation, and a non-empty set is frozen into the approval digest. An empty set is left out of the digest, so unclassed proposals keep the digest an older build computes. Every class is off by default, so an unconfigured host refuses every classed effect.

  The Workbench enables all three classes in its base `appsettings.json` (owner decision 5). Every Workbench environment loads that file, including the compose images, which run as Production, so `appsettings.Production.json` MUST NOT override the classes. Every other host keeps all three off unless its integrator enables them. Studio shows the policy read-only, from the bootstrap response.
- **FR-016**: **Outcomes.**
  - ***Performed***: leaf 0 MUST add *performed* (owner decision 3).
    - It is a new proposal status that reports the action as executed.
    - It has a new no-persisted-effect receipt scope.
    - The executor's and the integrity check's validation accept that scope only for *performed*.
    - A tool descriptor declares whether the tool can end *performed* (FR-018).
    - The plan names every server and Studio mapping site.
  - **Partial failure**: a module reports a partial failure, where a side effect happened before the failure, by throwing. Weaver core records the outcome as *uncertain*.
  - **Module codes**: a tool descriptor and a context-provider descriptor MAY declare a closed set of module codes. Every M4 tool and context provider declares one.
    - **Validation**: Weaver core validates each code from a declaring source, in receipts, refusals, preparation and execution errors, authorization violations and context diagnostics.
    - **Replacement**: a code outside the set is classified first, and then replaced by a generic code with that classification. A generic code that replaces an owner-unavailable code is itself outside `weaver.`, so it stays owner-unavailable and never becomes forbidden.
    - **Mapping**: each `Elsa.Weaver.<Domain>` package maps its owning module's native codes into its set. This includes splitting a code with two meanings, such as the Secrets `not-found` code used for both a missing secret and a missing configuration entry, into two declared codes.
    - **Format**: mapped codes are dotted ASCII identifiers outside `weaver.`. A receipt carries its code in a new member, which the plan names.
    - **Undeclared sources**: sources that declare no set pass their codes through unchanged.
  - **Everything else**: every other outcome reuses the existing outcomes and codes. A new code is added only where none fits, such as the privilege-policy refusal. A status alone is never evidence of an effect.

#### Contracts, catalogs and records

- **FR-017**: Replacement contracts MUST detect a second registration (framework §2.6.2). The Weaver-wide gap remains #3633. Visibility follows framework §2.23.3 and ADR 0080 D3.
- **FR-018**: **Durable records and SemVer.**
  - **Placement**: the frozen class set, the receipt's module code, and the *performed* status and receipt scope are held in the existing Weaver EF records, without a relational schema change. Where each is stored is plan decision PD2.
  - **Schema family**: the Weaver family follows [ADR 0078](../../docs/adr/0078-workflow-executions-are-virtual-actors-and-cluster-membership-is-a-foundation-contract.md)'s schema version gate, which is built.
    - Leaf 0 adds 1.1.0 to the Weaver family. The payloads are protected, so the upcaster from 1.0.0 is the identity, and default values (no module code, an empty class set) are supplied when a payload is deserialized. Readers accept 1.0.0 and 1.1.0 rows of every kind.
    - The audit sequence counter is never re-stamped, so 1.0.0 stays in the Weaver family's readable range for as long as it exists. Leaf 0 records this on #3428.
    - 1.1.0 finalizes automatically: at activation on a cluster of one, and otherwise once every counted schema-fleet member can read it. An operator hold blocks finalization.
    - Once 1.1.0 is finalized, a host that cannot read it refuses activation.
  - **Dormancy**: leaf 0 supplies store-specific features for its privilege-class, *performed* and module-code behaviour, through framework §2.5 feature inheritance. Only the selected store's feature is enabled; existing Weaver features gain no requirement and remain unaffected.
    - The in-memory feature supplies the shared behaviour without an EF-family requirement. The EF-specific feature inherits that behaviour and statically declares Weaver family 1.1.0 on its feature class, using [spec 182](../182-dormant-features-until-finalization/spec.md)'s shared mechanism. The EF-store composition selects that feature; it cannot expose an ungated sibling path to the same operations.
    - The EF feature's operations check shared availability at preparation, before review. Missing observation or unfinalized 1.1.0 refuses with `schema-version-not-finalized`; the catalog, Attention and `/capabilities` expose its dormancy. Absence of a family is never interpreted as a satisfied requirement. There is no per-tool descriptor-based dormancy dispatch.
    - Composition tests cover in-memory availability, EF held/unobserved refusal and finalized availability, including the absence of an ungated path. The plan names the features, metadata declaration and check sites.
    - Completeness/backfill is not required: the feature writes new data and needs no projection over old rows.
  - **SemVer** follows framework §4.2, with pre-GA Majors under ADR 0080 D4 and Elsa constitution §E5.
    - **Major**:
      - *performed*: the new status, the receipt scope and the validation. These change model semantics, as owner decision 3 amends them.
      - the privilege policy: with its classes off by default, it refuses `Admin` tools, third-party ones included, that ran before.
    - **Patch**: native connection delete cleanup for #3779, limited to its OAuth grant and PKCE nonce names; native deletion remains best-effort.
    - **Minor**:
      - the Weaver schema family 1.1.0: an additive version behind the gate, dormant until finalization;
      - opt-in module-code sets, the *performed* declaration and the receipt member;
      - owner-unavailable from preparation and execution errors;
      - the Secrets exact-prefix enumeration;
      - the secret-consumer fan-in, with Secrets `.Core`'s reference to `Elsa.Events.Core` and no hard dependency on `Events`;
      - new members on existing stores and contracts, added so that existing implementations keep compiling;
      - new contracts, events, packages, Context Kinds, tools, renderers, codes and permissions.
- **FR-019**: **Catalogs** MUST change with the code (framework §2.22.1).
  - **Leaf 0**:
    - `Extensions/Weaver/src/Elsa.Weaver/EXTENSION_POINTS.md` gains the privilege policy, class sets, module-code sets, the *performed* declaration, owner-unavailable errors, the principal seam and *performed*;
    - `Extensions/Weaver/src/Persistence/EntityFrameworkCore/EXTENSION_POINTS.md` gains the schema family 1.1.0; the new leaf 0 feature catalog records its static dormancy requirement.
  - **Leaf 1**:
    - updates the Secrets Core, `Elsa.Secrets` and Secrets EF catalogs;
    - creates `Essentials/src/Secrets/Api/EXTENSION_POINTS.md`.
  - **Leaf 2**:
    - updates the Connections Api, Connections Core and `Elsa.Connections` catalogs, for the convention and the delete cleanup;
    - updates the Secrets Core, `Elsa.Secrets`, Secrets Api and Secrets EF catalogs, for the exact-prefix enumeration;
    - creates the Connections Api README.
  - **Leaf 3**:
    - the Secrets Core catalog gains the event and the source contract, with Connections' implementation as a cross-domain Known implementation;
    - the `Elsa.Secrets` catalog gains the handler;
    - the Connections Core catalog gains the new repository member;
    - the `Elsa.Connections` catalog gains the implementation, and leaf 3 creates that package's README with its cross-domain note;
    - the Connections EF catalog gains the bounded query.
  - **Leaf 5**: `Studio/src/essentials/Elsa.Studio.Api/EXTENSION_POINTS.md` gains the SDK read seams.
  - **Every leaf**:
    - lists its contributors in the Weaver catalog, as cross-domain Known implementations;
    - gives each new package that exposes extension points its own catalog;
    - updates the root index (framework §2.22.2);
    - lists each package's cross-domain contributions in its README.
- **FR-020**: **Records.** Evidence goes on each PR and on the leaf's sub-issue, as [AGENTS.md](../../AGENTS.md) requires. #3443 and #3428 get summary comments at each state transition; #3428's authoritative matrix links its M4 detail in FR-001 ([program goal](../../docs/program-goals/weaver-across-studio.md)).
  - **Issue and Project reconciliation**: at each state transition, each leaf also sets its sub-issue's and #3443's status in Project #65 to match.
  - **Claims refresh**: leaf 0 refreshes the current claims on #3443 and on the overlap issues under Assumptions, and records the result on #3443.

  Leaf 0 MUST file these `needs-triage` follow-ups:
  - field-level sensitivity for connection settings (X);
  - ExtensionBuilder workspace source inspection (X);
  - console record reads (X);
  - a server-verifiable path for browser-only writes (V);
  - a durable, cluster-wide Workflow Runtime Diagnostics Setting (D);
  - views for log time range and retention, and for OpenTelemetry metrics and logs (S);
  - working-state adapters for ETL creation, task forms, BPMN processes and activity contract proposals (W);
  - coverage of the irrecoverable class for third-party effects that declare no class;
  - detection of secret consumers in workflows and Nuplane feed credentials, and of consumers that resolve connections by name;
  - a Weaver resolution path for uncertain server effects.

  These rules apply to follow-ups:
  - The Program's own follow-ups are triaged under the 29 October delegation.
  - Every follow-up is recorded on #3428.
  - A follow-up in a parked program's area is filed without a parent.
  - Nothing that already exists is filed: context-budget progress goes on #3710, the OAuth grant leftover is #3779 (assigned to leaf 2, #3833), and Full payloads stay with Studio ADR 0009.

  Leaf 0 also records on #3428:
  - every narrowing;
  - the deferrals to other programs: #3604, and #3519's authoring;
  - the decisions open to owner review;
  - the Weaver family's permanent 1.0.0 readability (FR-018).
- **FR-021**: **Glossary.** Leaf 0 MUST make these changes.
  - **Elsa glossary** ([docs/glossary/elsa.md](../../docs/glossary/elsa.md)), in its sentence case, for backend Weaver concepts, each linked from Studio/CONTEXT.md:
    - the outcome words *proposed*, *approved*, *persisted*, *applied locally*, *refused*, *uncertain*, *performed*, *owner-unavailable*, *unavailable*, *unsupported* and *stale*;
    - *Privilege policy*, stating that it is part of Studio's *Host Policy*, and how "Administrator policy has final veto" applies through it;
    - *Privilege class*;
    - *Module code*;
    - *Secret consumer*;
    - *Connection-owned secret*.
  - **Studio/CONTEXT.md**, for Studio concepts:
    - *Module Load Status* is redefined as the diagnostics status values: available, loaded, disabled, incompatible and failed. The entry notes that the registry's own status adds pending and unknown. It replaces "discovered, accepted, activated, disabled, or rejected", and its _Avoid_ line stays.
    - *Host Policy* gains a link to *Privilege policy*.
    - *Privileged Tool* is widened to a Weaver Tool whose effect needs a privilege class.
    - *Built-in Studio Screen* is added: a screen that the `Elsa.Studio.Hosting` client renders itself.
    - *Client-Asserted Kind* is added: a Context Kind whose content only the browser supplies. It is untrusted, confers no authority, and the server checks only its schema and bounds.

#### Evidence

- **FR-022**: Each story MUST have end-to-end evidence.
  - **Hosts**: a scenario that needs test code runs in the probe host, which carries the leaf's fixtures. The normal host and the durable host run only shipped code. Each story's Independent Test assigns its scenarios.
  - **Restart cases**: these run in their leaf's durable host, except story 1's, which run in the probe host's restart configuration.
  - **Adapter coverage**: every tool and effect MUST have a conformance-probe case on both provider adapters: Copilot's delegated loop and Anthropic's host-driven loop.
  - **Negative cases**: these run at the cheapest level that proves them.
- **FR-023**: The canary search MUST cover HTTP bodies, SSE frames, adapter payloads, tool results, module codes, module messages in preparation and execution errors, review cards and audit records.
- **FR-024**: Evidence MUST keep three categories distinct:
  - deterministic component tests;
  - deterministic provider-protocol evidence on the actual adapters, with packaged consumers;
  - opt-in live-provider evidence (none claimed).

  Probe evidence is frozen at the exact reviewed head. Failures, skips and quarantines are recorded as they occurred.

#### Later leaves

- **FR-025**: Each later leaf MUST get its own spec, numbered per [the spec lifecycle](../../docs/reference/spec-lifecycle.md#numbering), and its own sub-issue of #3443 that carries the leaf's acceptance. Both are created when that leaf starts. Each leaf spec is bound by FR-002 to FR-024 and by its FR-001 rows, and carries those rows' evidence.
  - **Grouping**: each owning module gets its own leaf. Rows from different modules share a leaf only where they share a page or a journey. A module's rows may span several leaves for size.
  - **Assignment**: leaf 0 assigns every row its refresh adds. #3443 cannot close while any row is unassigned.
  - **Reassign escalation**: if leaf 15's spec keeps user-task reassign at `Admin`, its spec PR escalates the choice on #3428 before that spec merges.

  | Leaf | FR-001 rows | Scope |
  | --- | --- | --- |
  | 4 | Structured logs, traces | allowlisted reads; grouped for log-to-trace correlation |
  | 5 | Diagnostics, appearance/themes, settings sections | Studio Hosting's screens, with Hosting.Themes. Creates `Elsa.Studio.Weaver.Hosting`, and widens *Theme Selection Preference* |
  | 6 | Console | console context, and lines the user attaches |
  | 7 | Home | widget snapshots, personal layout and snooze; grouped because they share the Home page |
  | 8 | Runtime evidence | the setting change |
  | 9 | Activity availability, activity definitions | Activities Design |
  | 10 | Features | the draft, and the Server-host apply |
  | 11 | Modules/feeds | administration reads |
  | 12 | Backends | profile reads |
  | 13 | ETL definitions, connectors, runs | reads |
  | 14 | ETL definitions, connectors, runs | draft updates and run cancellation |
  | 15 | User tasks | reads; claim, release and reassign, with the reassign escalation above |
  | 16 | Agents | reads |
  | 17 | BPMN | reads |
  | 18 | Expressions | with #3711 |
  | 19 | Weather | a client-asserted page context, through a small package |
  | 20 | ExtensionBuilder | installed-module guidance |
  | 21 | Workflow rows | supported run wait/stop/recovery controls, library deletes and Executables lifecycle including dispatch, handed back by spec 1193; retain exact occurrence, authority and owner-confirmed outcomes |
  | 22 | Decisions | newly composed module-owned reads and bounded guidance for exact library/draft/version/table/model/case/evaluation and nested workflow-editor contexts; its own spec and sub-issue are mandatory under FR-025, and live inventory qualification remains required |

### Key Entities

- **Privilege policy and class set**: the integrator's enabled classes, and the set of classes each effect declares and needs (FR-015).
- **Module-code set**: the closed codes a tool or context provider declares, and the mapping from native codes (FR-016).
- ***Performed* and its receipt scope**: the outcome of an approved action that persisted no change (FR-016). Both are durable (FR-018).
- **Secret-consumer answer**: the binding connections, whether the secret is connection-owned, the disclosure rule and the truncation flag, or "not checked" (FR-006, story 4).
- **Durable Weaver fields**: the frozen class set, the receipt's module code, and the *performed* status and receipt scope, held in the Weaver store's schema family 1.1.0 and dormant until it is finalized (FR-018).

## Success Criteria

### Measurable Outcomes

- **SC-001**: Every FR-001 row has linked evidence, all linked from #3428. The evidence for the rows of leaves 0–3 comes from this spec, and the evidence for later rows from their leaf specs (FR-025). Every narrowing states its grounds, and no row is left unassigned.
- **SC-002**: Stories 1–4 pass their acceptance scenarios, each in the host its Independent Test assigns (FR-022). An unconfigured host refuses every classed effect.
- **SC-003**: Seeded canaries appear in none of the channels FR-023 lists. Content the user explicitly attaches is excluded.
- **SC-004**: For each contributing package, a composition test that deselects the package removes exactly its contributions, and leaves every other row unchanged.
- **SC-005**: Each leaf PR's review evidence includes a diff check of its changed projects against FR-005's list. The check shows that Weaver core, the shell's collector and review shell, the provider adapters and other modules changed only through FR-005. The owning-module changes that FR-006 requires are exempt, including Integrations' use of the Connections convention.
- **SC-006**: After leaf 0, the independent package witness is re-run and passes. The program goal requires later lifecycle changes to requalify.
- **SC-007**: The native-page regression tests (FR-006) and the attachment tests (FR-011) pass.
- **SC-008**: Each leaf PR meets these gates:
  - it carries [merging](../../docs/contributing/merging.md)'s PR evidence;
  - it keeps the Elsa constitution §E7 test-tier manifest correct for every new test project;
  - it passes locked-mode restore;
  - it keeps the solution filters current.

## Assumptions and Boundaries

- **Delivery**:
  - **Sub-issues**: leaves 0–3 get sub-issues of #3443 when this spec merges. Each leaf PR says "Fixes #<leaf issue>, Refs #3443". The last M4 leaf to merge, which may belong to a later leaf spec, also fixes #3443. Each PR maps the #3443 acceptance items it advances.
  - **Order**: leaves 1–3 depend on leaf 0, and each depends on the leaf before it.
  - **Status**: this spec's PR sets it to *Approved* in its own diff. Leaf 0's PR sets *In progress*. The spec stays *In progress* until the last M4 leaf merges, because FR-002 to FR-024 bind every later leaf spec. That leaf's PR sets *Implemented*.
  - **Bookkeeping**: every leaf posts its summary comments and reconciles Project #65 at each state transition (FR-020).
- **Program owner decisions** (Sipke Schoorstra), quoted verbatim:
  - **3 November 2026** ([receipt](https://github.com/elsa-workflows/elsa-foundation/issues/3428#issuecomment-6612627295)). The receipt's "spec 1202" is this spec, before it was renumbered ([note](https://github.com/elsa-workflows/elsa-foundation/issues/3443#issuecomment-6614698826)):
    1. **Irrecoverable deletes and revokes** of secrets and Service connections: "Let the integrator decide what privileges weaver has via configuration"
    2. **Feature selection apply**: "Let the integrator decide via configuration"

       This lane reads it as widening #3443's activation boundary only for hosts that enable the activation class.
    3. **A generic outcome for an approved action that persists nothing**: "Add it in leaf 0"
    4. **The privilege default**: "Off; Workbench always on"
  - **4 November 2026** ([receipt](https://github.com/elsa-workflows/elsa-foundation/issues/3428#issuecomment-6617907022)):
    5. **The Workbench's classes**: "Enable all three"

       The receipt states that the Workbench's base configuration enables the administrative, irrecoverable and activation classes, and every other host keeps all three off.
- **Decisions taken under Program authority, open to owner review**:
  - **Packaging**: one Weaver package per owning module.
  - **Class sets**: declared by consequence, satisfied in full, and frozen into the approval digest when non-empty. Every effect whose effective risk is `Admin` requires the administrative class.
  - **Secret revoke and delete**:
    - consumers and ownership come from Connections;
    - connection-owned secrets are refused by name, which is stricter than the native page, and hand-made secrets in that name space are treated as owned;
    - an effect fails closed when no source answers, so hosts without Connections cannot do this through Weaver;
    - the answer is recomputed at execution.
  - **Connection delete**: #3779 cleanup is limited to the existing owned fields, deterministic grant and PKCE nonce-pattern names. Native deletion stays best-effort; only Weaver refuses incomplete enumeration before effects. Consumers resolving a connection by name are not detected.
  - **Module codes**: declared closed sets, mapped by each Weaver package.
  - **Ground L choices**: the remaining FR-001 narrowings marked L; broader workflow journeys are delivered by leaf 21.
  - **Console**: server reads of records are narrowed (X).
  - **Uncertain server effects**: these have no M4 resolution path.
  - **Expressions**: delivered as Local edits with #3711.
  - **Post-M0 targets**: user tasks get read, claim, release and reassign, with reassign subject to escalation (FR-025). Agents, BPMN and backends are read-only.
  - **Runtime capture**: its semantics are untouched. Full payloads wait for Studio ADR 0009.
  - **Browser-only writes**: these are narrowings (V).
  - **Personal layout**: changes to it are delivered.
  - **Cancellation**: ETL cancellation is reviewed. BPMN run operations are outside the target (T).
- **Plan decisions**:
  - **PD1**: each package's shell feature.
  - **PD2**: the contract signatures, internal names and types, the principal seam and the revision mechanisms; where permission constants live; where the durable Weaver fields are stored; and the store mechanics behind FR-018, such as the counter's version check, the schema-fleet and activation-refusal types, and the enumeration's limits.
  - **PD3**: every non-atomic compare-then-write window, and how it is bounded. In particular, the Connections consumer comparison and Secrets revoke/delete span owners: specify an owner-supported atomic check/fence if available, or explicitly record the remaining interval, the review warning and observation-attributed receipt semantics of story 4. A secret-revision CAS alone does not serialize a concurrent connection binding. A barrier test creates a binding inside that interval; the implementation must not claim atomic consumer protection when it has none.
- **Overlap**, reconciled without reparenting:
  - ETL: #2894, #2906 and the ETL defects (#3709, #3719, #3721, #3724, #3562).
  - Home dashboards: #3604.
  - The parked Diagnostics Observability Readiness and Runtime Execution Evidence goals.
  - User Tasks: #3688 (closed).
  - Studio Expression Developer Experience: #2310, #3711 and #2349. No children are added under #2310.
  - Elsa 3.8/3.9 Parity: #2771, which owns Elsa 3 import.
  - The [BPMN Engine program](../../docs/program-goals/bpmn-engine.md).
  - Agents: #3519. Runtime agents: #2892.
  - Weaver: #3710, #3720 and #3761.
  - Feed credentials: #3735.
  - OAuth grant leftover: #3779, assigned to leaf 2 (#3833).
- **Out of scope**:
  - releases, deployments and provider purchases;
  - live-provider credentials and credential extraction;
  - blanket administration;
  - arbitrary workspace edits;
  - package promotion and package activation, and publisher activation. Feature selection apply is in scope only through the activation class.
  - integrated qualification, which remains #3444.

## Issue Traceability

| #3433 criterion | Requirements and stories |
| --- | --- |
| Home/dashboard, ETL, Connections, Secrets metadata, Diagnostics, runtime evidence, activity availability and installed settings/administration surfaces have module-owned support | FR-001 to FR-004, FR-025; stories 2–4 |
| Each supported action has context, tool, renderer/effect and negative-case evidence; absent mutation support is explicit | FR-001, FR-004, FR-013, FR-014, FR-022 |
| Secret values and unrelated data are not exposed; runtime/model semantics and existing domain ownership remain intact | FR-002, FR-006, FR-007, FR-009, FR-012; Assumptions; owner decisions 1, 3 and 4 |
| Scope is derived from the installed/composed inventory, including nested surfaces and module removal | FR-001, FR-008, FR-010; story 1; SC-004 |

| #3443 acceptance or task | Requirements and stories |
| --- | --- |
| Each M0 row becomes an owning-module entry with acceptance and negative-case evidence | FR-001, FR-025; stories 1–4; SC-001, SC-002 |
| Cover the named surfaces, including nested contexts (Secret picker, connection picker) | FR-001, FR-010, FR-025; stories 2–4 |
| Same generic contribution path; module knowledge, renderers and guidance in the owning module | FR-002 to FR-008; SC-004 to SC-006 |
| Read where mutation is unavailable; review and apply where allowed; secret values and unrelated data excluded | FR-012 to FR-016; stories 2–4; SC-003 |
| Normal-host composition, removed modules, authority and stale targets; no claim for absent modules | FR-001, FR-008, FR-009, FR-014, FR-022 to FR-024; story 1; SC-002, SC-004 |
| Per-module leaves; reconcile overlap without reparenting | FR-025; Delivery; Overlap; FR-020 |
| Boundaries | FR-001, FR-015; owner decisions |
| Task: refresh source, owning-module contracts, current issue claims and the required inputs | FR-001 (inventory refresh), FR-006, FR-020 (claims refresh) |
| Task: implementation and evidence | FR-002 to FR-019, FR-022 to FR-025 |
| Task: run review and CI gates, and reconcile the issue and Project after verified completion | SC-008; FR-020 (issue and Project reconciliation); Delivery |
