# Feature Specification: Module-owned Weaver support for the current Studio inventory

**Feature Branch**: `agent/claude/3443-weaver-module-support`

**Created**: 2026-11-03

**Status**: Draft

**Input**: [Feature #3443](https://github.com/elsa-workflows/elsa-foundation/issues/3443), which is M4 ([#3433](https://github.com/elsa-workflows/elsa-foundation/issues/3433)) of [Program #3428](https://github.com/elsa-workflows/elsa-foundation/issues/3428). The feature is [in progress](https://github.com/elsa-workflows/elsa-foundation/issues/3443#issuecomment-6612633571).

Delivery is authorized by the Program owner's [standing authorization](https://github.com/elsa-workflows/elsa-foundation/issues/3428#issuecomment-6615643669), within #3443's boundaries as the owner decisions under Assumptions amend them. The authorization grants no blanket administration permission, no arbitrary workspace editing, and no package promotion or activation.

The scope comprises:
- the M0 inventory in [docs/reports/weaver-baseline.md](../../docs/reports/weaver-baseline.md);
- #3443's planning comments of 30 and 31 October;
- the `/user-tasks` and `/agents` rows that the [claim](https://github.com/elsa-workflows/elsa-foundation/issues/3443#issuecomment-6605270638) adds;
- the screens merged since: `/settings/backends` (PR #3600) and the BPMN pages (PR #3603).

The prerequisites #3440, #3441 and #3442 are complete.

**Review**: Eighteen independent review rounds took place on 2026-11-03, each with a correctness lens and a standards lens. Every finding was verified against `origin/main`. The approval basis is the standing authorization and the owner decisions recorded under Assumptions.

This spec contains:
- the M4 delivery matrix (FR-001);
- the cross-cutting requirements every leaf must meet;
- leaves 0–3, as stories 1–4.

Each later leaf gets its own spec (FR-025). The plan PR, which adds `plan.md`, also adds `contracts/privilege-policy.md` and `contracts/outcomes.md`.

**Terms**:
- **Where definitions live**:
  - Studio terms use the meanings in [Studio/CONTEXT.md](../../Studio/CONTEXT.md).
  - *Service connection* uses its meaning in the [Elsa glossary](../../docs/glossary/elsa.md).
  - Leaf 0 adds the backend Agent terms below to the Elsa glossary, and the Studio terms to Studio/CONTEXT.md (FR-021).
- **Outcome words**:
  - *Proposed*, *approved*, *persisted*, *refused* and *uncertain* are the states an effect passes through.
  - *Applied locally* means a change to visible, unsaved state only.
  - *Performed* is new in leaf 0: the approved action ran, and the owning module persisted no change.
  - *Owner-unavailable* means the owning module could not decide. Spec 1193's slice A introduced it, and FR-006 widens it.
  - *Unavailable* means the effect fails closed because its module or client is gone.
  - *Stale* means the effect was refused because its target changed after review.
- **Privilege Class**: a class of effects, such as administrative, irrecoverable or activation, that the integrator enables in the Agent module's configuration (FR-015).
- **Module code**: a code that a tool or context provider reports from its declared set (FR-016).
- **Secret consumer**: a resource that uses a secret, such as a Service connection that binds or owns it. Do not confuse it with a workflow's *Secret Reference*.
- **"Owner"** is always qualified:
  - the *owning module* owns a row's resource;
  - the *Program owner* is Sipke Schoorstra;
  - a Service connection's *Owner field* is part of its metadata.
- **Naming**: domain nouns keep their current names until their own [ADR 0080 D5](../../docs/adr/0080-elsa-4-simplification-decisions.md#d5--plain-language-renamed-now) batch, so that no type is renamed twice. New Attention packages and types therefore keep "Attention", and are renamed in Attention's batch. New contributor-style interfaces use the `I<X>Extension` suffix now.

**Hosts**:
- **Normal host**: the Workbench backend (`Elsa.Workbench`) with Studio (`Elsa.Studio.Web`), with Design and Runtime combined, in the Development configuration. Leaves 1 and 2 add their Agent packages to it (FR-002).
- **Durable host**: the normal host plus the Agent EF store, which the overlay of the Workbench's durable-store test adds. Secrets EF is already composed. From leaf 2, it also includes the Connections EF store.
- **Probe host**: the conformance probe's packaged consumer. Its restart configuration enables the Agent EF store. Leaves 1 and 2 change it (FR-002).
- **Test overlay**: one definition per leaf, applied to whichever host a case needs (FR-002).

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
   - an effect that persists nothing;
   - a tool and a context provider that each declare a module-code set and emit a code outside it, one of them echoing a canary;
   - an authorization violation that carries such a code.
5. Propose each effect four times:
   - with the classes unconfigured;
   - with only the administrative class enabled;
   - with all three classes enabled, as in the Workbench's base configuration;
   - with a class switched off between review and approval, between approval and execution, and before recovery.
6. In the probe host, deselect the fixture module while a page nests its widget.
7. In the probe host's restart configuration:
   - leave an effect pending, restart, remove the fixture module, and approve;
   - simulate the leaf 0 upgrade with one server effect pending approval, one approved client invocation waiting, and pre-upgrade session, message, snapshot, context, proposal, key and audit rows.

**Acceptance Scenarios**:

1. **Given** a module that the registry does not list, **When** the user asks about it or opens its route, **Then** Weaver answers that it is not installed, and the route is the only context it discloses.
2. **Given** a disabled module, or an enabled module without Weaver contributions, **When** the user asks for help on its page, **Then** Weaver says so and offers no tool.
3. **Given** a deselected module, **When** a page that would nest its widget is open, **Then** nothing of that module is offered, and other modules' support is unchanged.
4. **Given** unconfigured classes, **When** any classed effect is proposed, **Then** it is refused before review.
5. **Given** only the administrative class enabled, **When** an administrative and irrecoverable effect is proposed, **Then** it is refused before review. **When** an administrative-only effect is proposed, **Then** it is reviewed.
6. **Given** all three classes enabled, **When** each effect is proposed, **Then** it is reviewed, and its review shows the classes it needs.
7. **Given** a bootstrap request, which carries no session, **When** the host answers, **Then** the answer reports every class's state, and Studio shows the states read-only.
8. **Given** an effect whose class is off at approval, execution or recovery, **When** that step is reached, **Then** the proposal is refused and ends there. Nothing is dispatched, and a new proposal is needed once the class is enabled.
9. **Given** an approved effect whose owning module persisted nothing, **When** it is recorded, **Then** it is *performed*, with the no-persisted-effect scope, and the review shell shows it.
10. **Given** a code outside the declared set, **When** it is recorded in a receipt, refusal, preparation error, authorization violation or context diagnostic, **Then** it is replaced by a generic code. The generic code keeps the original classification, and no canary is disclosed.
11. **Given** a pending effect whose module is removed, before or after a restart, **When** it is approved or recovered, **Then** it is unavailable, with zero effect.
12. **Given** a server effect pending approval across the leaf 0 upgrade, **When** it is approved, **Then** it is refused as a binding mismatch, and never executed. **Given** an approved client invocation waiting across the upgrade, **When** it is resumed, **Then** it is *uncertain*. **Given** pre-upgrade rows, **When** the upgraded store reads them, **Then** they read with default values (FR-018).

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

**Independent Test**: In the normal host, with leaf 1's test overlay, seed:
- an active secret, one whose latest version has expired, and a revoked secret, each holding a canary value;
- a secret whose test message and test code both echo its value;
- a secret in another tenant.

Read the list on both routes, improve a description and test a secret. Compare each receipt with the owning module's readback.

Negative cases:
- no `secrets:read`, no `secrets:write`, or no `secrets:test`;
- a target in another tenant;
- an edit or rotation in another tab between review and approval;
- a test of a missing or inactive secret;
- the Secrets EF store deselected;
- the administrative class off;
- in the durable host, a restart with an approved effect pending;
- requests to create, rename, reveal, rotate, import, export, revoke or delete.

**Acceptance Scenarios**:

1. **Given** the list with a filter and a selection, **When** Weaver reads context, **Then** it receives the filter, the selected identities and their metadata, bounded by `secrets:read` and the tenant. An expired secret shows as expired.
2. **Given** an approved description update, **When** it is applied, **Then** it is saved at the reviewed revision, and the receipt carries the readback. Without a revision-aware repository, the update is unavailable.
3. **Given** an approved test, **When** it runs at the reviewed version, **Then** it is *performed*. The receipt carries only success or failure and a declared module code. **When** the secret is missing or inactive, **Then** the test is refused, with its own declared code.
4. **Given** a request to create, rename, reveal, rotate, import or export, **When** Weaver receives it, **Then** it refuses on the grounds in FR-001. **Given** a request to revoke or delete in leaf 1, **When** Weaver receives it, **Then** Weaver says the operation is not yet supported.

---

### User Story 3 - Understand, prepare, update, rebind, test, revoke and delete a Service connection (leaf 2) (Priority: P3)

A user on the Connections page, in its creation form with its secret picker, or at a connection picker in a workflow asks what a connection is or why it fails. The user may also ask Weaver to prepare, update, rebind, test, revoke or delete one.

Through a new read in the owning module (FR-006), Weaver sees:
- name, display name, type, authentication scheme and status;
- settings field names and secret-bound field names;
- managing party and Owner field;
- tags, last test time and version.

Bound non-owned secret names and named-credential names are disclosed only under `secrets:read`. Settings values are never disclosed.

**Why this priority**: "Why does my integration fail" is the commonest Connections question.

**Independent Test**: In the normal host, with leaf 2's test overlay, use a test connection type that records invocations, carries canary token fields, and returns a test message that echoes a resolved value. Seed:
- a connection with canary settings, secret and token values;
- a connection with an owned inline secret, an OAuth grant secret and a PKCE secret;
- active, expired and inactive text secrets;
- another connection's owned inline, grant and PKCE secrets;
- a connection in another tenant.

Explain each connection. Then prefill, update, rebind, test, revoke and delete, and compare each receipt with the owning module's readback.

Negative cases:
- an edit, or a user-run test that ran, between proposal and approval;
- no `workflows/design:read` and no `connections:manage`, for reads;
- no `secrets:read`, for bound names, the picker and a rebind;
- no `connections:manage`, for the prefill, update, rebind, revoke or delete;
- no `connections:bind-secret`;
- no `connections:test`;
- a target in another tenant;
- a rebind to an expired, inactive or non-text secret, or to another connection's inline, grant or PKCE secret;
- a rebind away from the owned inline secret, where cleanup fails after commit;
- a test that cannot run, one that fails, and one whose write fails after the external call;
- a test message or code that echoes a resolved value;
- a dropped test response;
- a delete that removes owned secrets and then fails to remove the connection;
- a delete of a connection with an OAuth grant where the grant cleanup is not available;
- the creation form closed before the prefill completes;
- the administrative or irrecoverable class off;
- in the durable host, a restart with an approved effect pending;
- requests to create a connection, authorize OAuth, change the redirect URI, bind a named credential, replace an owned inline secret's value, or set a settings value.

**Acceptance Scenarios**:

1. **Given** the creation form, **When** Weaver reads it, **Then** it sees the type, scheme, field names and validation errors, and no entered value. The form's secret picker is a Connections contribution that lists candidates under `secrets:read`.
2. **Given** an approved prefill under `connections:manage`, **When** it is applied, **Then** only the form's name, type and scheme change, and the prefill is applied locally. Creating the connection stays the user's action.
3. **Given** an approved update or rebind, **When** it is applied, **Then** it applies at the reviewed version, under `connections:manage`. A rebind also needs `connections:bind-secret`, `secrets:read`, and an active text secret in the tenant. The secret must be outside the connection-owned name convention `connection:{name}:…`, which covers inline, OAuth grant and PKCE secrets. The candidates shown to the client grant nothing.
4. **Given** a rebind, **When** it is reviewed, **Then** the review states that the connection sends the new secret's value to the endpoint it is configured for, that Weaver does not disclose that endpoint, and that the user should check it on the connection page.
5. **Given** a rebind away from an owned inline secret, **When** it is reviewed, **Then** it needs the administrative and irrecoverable classes. The review states that:
   - the secret is deleted after commit unless another connection still uses it;
   - only other connections are checked;
   - the deletion cannot be undone.

   If cleanup fails after commit, the effect is persisted with a warning.
6. **Given** an approved test, **When** it runs, **Then** the reviewed version is checked before the external call. When the type's test runs, the owning module records the test time and advances the version, and other pending proposals for that connection go stale. The receipt is persisted with the result and a declared module code, and the message is redacted. **When** the test cannot run, **Then** it is refused, with its declared code. **When** its write fails after the external call, **Then** it is *uncertain*.
7. **Given** a revoke or delete, **When** it is reviewed, **Then** it needs the administrative and irrecoverable classes. The review states that the action cannot be undone, and that consumers which resolve the connection by name are not detected: workflows, ETL bindings and specialist model profiles. For a delete, the review also lists, by field name, the owned inline secrets and the OAuth grant and PKCE secrets that will be deleted. A secret that another connection still uses is kept.
8. **Given** a connection with an OAuth grant, **When** a delete is proposed and the composed Connections cannot delete its grant and PKCE secrets (FR-006), **Then** Weaver refuses it before review.
9. **Given** an approved revoke or delete, **When** it runs, **Then** the reviewed version is checked before any side effect. A delete's receipt reads back the connection's absence. A delete that removes owned secrets but then fails to remove the connection is *uncertain*.

---

### User Story 4 - Revoke and delete a secret, knowing its connection consumers (leaf 3) (Priority: P4)

On the Secrets page, a user asks Weaver to revoke or delete a secret. The review shows the consumers that Connections reports: the connections whose fields bind the secret, and whether a connection owns it. Weaver refuses owned secrets, and states what it cannot detect.

**Why this priority**: Irrecoverable secret effects are safe only once their consumers are known, as far as they can be known.

**Independent Test**: In the normal host, with leaf 3's test overlay, use a test secret store that really removes values, plus a repository save fault. Seed:
- a secret bound by two connections;
- an unused secret;
- an owned inline secret;
- an OAuth grant secret;
- a secret whose delete removes the value and then fails to save the metadata.

Revoke and delete through Weaver, and compare each receipt with the owning module's readback.

Negative cases:
- a binding added or removed between review and approval, or between approval and execution;
- a truncated answer whose page or count changes;
- no consumer source answering;
- a repository that is not revision-aware;
- an actor who cannot read connections;
- no `secrets:delete`;
- the partial delete;
- the administrative or irrecoverable class off;
- in the durable host, a restart with an approved effect pending.

**Acceptance Scenarios**:

1. **Given** a revoke, **When** it is reviewed, **Then** the review states that the revoked version's value cannot be restored, and that rotating reactivates the secret with a new value. **Given** a delete, **When** it is reviewed, **Then** the review states that it cannot be undone. Both need `secrets:delete` and the administrative and irrecoverable classes.
2. **Given** a review, **When** consumers are listed, **Then** the list is advisory. It shows the binding connections by name when the actor meets the disclosure rule the source declares, and as a count otherwise. A truncated answer says so. The review states that workflow references and Nuplane feed credentials are not detected.
3. **Given** a secret that a connection owns, whether inline, OAuth grant or PKCE, **When** a revoke or delete is proposed, **Then** Weaver refuses it before review. This is stricter than the native page, and the user can act there instead.
4. **Given** no consumer source answered, **When** a revoke or delete is proposed, **Then** it is refused before review. The refusal states that connection bindings could not be checked. This covers four cases: Connections is not composed, Connections is an older version without the source, Connections runs in another shell, or the query is unsupported. This limitation is recorded: a host without Connections cannot revoke or delete secrets through Weaver.
5. **Given** a repository that is not revision-aware, **When** a revoke or delete is proposed, **Then** it is unavailable.
6. **Given** an approved revoke or delete, **When** it is executed, **Then** the consumer answer is recomputed and compared by full identity, as it was at approval. A truncated answer is compared by its page and count. Any change makes the proposal stale. The reviewed revision is checked before any side effect.
7. **Given** a delete, **When** it runs, **Then** its receipt reads back the secret's absence. If the value is removed but the metadata save fails, the delete is *uncertain*.

### Edge Cases

- **A module unloads mid-review:** the proposal is unavailable at approval, and context is re-admitted without the module (FR-009).
- **A shell reloads during an effect:** an effect whose response is lost is *uncertain*, and is never replayed (FR-014).
- **A secret is rotated between review and approval:** its revision has changed, so the effect is stale.
- **A connection is renamed or deleted under a pending proposal:** the version or identity check makes the proposal stale.
- **A dense page overflows the attachment budget:** the collection is refused as the budget defines, and FR-011's tests prevent this at default sizes.
- **Connections is composed in another shell:** no consumer source answers, so secret revoke and delete fail closed (story 4).

## Requirements

### Functional Requirements

#### Delivery matrix

- **FR-001**: #3428 owns the Program acceptance matrix, and records M4's rows from this table.
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
  - **N**: no owner operation exists.
  - **O**: owned by another issue, spec or open decision.
  - **P**: this spec's Program-authority choice, open to owner review.
  - **S**: the screen does not show it.
  - **T**: outside a post-M0 row's target, which is a P choice.
  - **V**: the write is reachable only from the browser, so the server cannot verify its result.
  - **W**: needs a module-owned working-state adapter.
  - **X**: needs its own exclusion design first.

  Targets are quoted from the baseline. Feature selection apply is delivered through the activation class (owner decision 2).

  | Row (owning module) | Target | Outcome; narrowings (grounds) |
  | --- | --- | --- |
  | Secrets (Secrets) | "Reference/metadata/availability inspection; domain-authorized effects, with no secret values in context" | Read; Reviewed metadata update and value test (story 2), revoke and delete (story 4). The connection form's Secret picker is a Connections surface (story 3). Value-bearing create and rotate (B); reveal (C); configuration-key create and rotate (P); rename, import and export (N); the workflow property's Secret editor (O: #3711) |
  | Connections (Connections) | "Metadata, bindings and diagnostics; supported changes/test actions without credentials in context" | Read, including the form's Secret picker; Local prefill; Reviewed update, rebind, test, revoke and delete (story 3). Settings values (X: no field declares its sensitivity); replacing an owned inline secret's value (B); create and OAuth authorize (C); redirect URI (N: a read-only public URL); named-credential binding (P) |
  | Optional sample: Weather (Samples, Studio-only) | "Qualify only when installed; no current composed-support claim" | Read of a client-asserted page context when installed (its leaf spec); None in M4 when absent (A) |
  | Optional ExtensionBuilder (ExtensionBuilder) | "Optional inspection/guidance; no source-control push, package promotion or publisher activation" | Read: installed-module guidance when enabled (its leaf spec). Workspace source inspection (X); None in M4 while disabled (A) |
  | Structured logs (StructuredLogs) | "Current filters/time range/selected records; bounded redacted correlation" | Read (its leaf spec). Time range and retention (S) |
  | Traces (OpenTelemetry) | "Exact trace/span/resource/query with authorized detail and availability" | Read (its leaf spec). Metrics and OpenTelemetry logs (S) |
  | Diagnostics (Studio Hosting and diagnostic modules) | "Accepted/rejected/disabled module state and bounded visible diagnostic results" | Read (its leaf spec). Module administration (P) |
  | Console (`Elsa.Studio.ConsoleStream`, console log streaming) | "Current source/filter/record/truncation/freshness and authorized bounded inspection" | Read (its leaf spec). Filter (S: none exists); server reads of records (X); lines the user attaches are the user's own client-asserted disclosure (FR-009) |
  | Home (Studio Dashboard, Studio Preferences, Attention, Workflows.Dashboard) | "Explain visible widget snapshots, filters, freshness and unavailable data; module-owned supported preferences" | Read; Reviewed personal layout change and Attention Item snooze (its leaf spec). Default layout (P); new widgets and saved queries (O: #3604) |
  | Runtime evidence (Workflows Runtime) | "Explain capture policy and exact proposed configuration differences" | Read; Reviewed Workflow Runtime Diagnostics Setting change within today's capture rules (its leaf spec). Full payloads (O: Studio ADR 0009); a durable, cluster-wide setting (D) |
  | Activity availability (Activities Design) | "Effective availability/diagnostics and supported settings changes" | Read; Reviewed change (its leaf spec) |
  | Features (FeatureManagement client, Modularity server) | "Selected host, revision, filters, settings draft and reviewed differences through existing authorization" | Read; Local draft; Reviewed Server-host apply in the activation class (its leaf spec). Studio-host apply (V); configuration values and edits (C) |
  | Appearance/themes (Studio Hosting, Hosting.Themes) | "Supported preference/theme edits, exact persistence semantics and built-in/read-only constraints" | Read; Local appearance and builder edits (its leaf spec). Theme Store writes (V) |
  | Modules/feeds (Hosting.Nuplane; client Studio Hosting) | "Explain exact host/package/feed/reconcile state; package promotion/activation remains outside authorization" | Read (its leaf spec). Package operations (B); feed operations (P); feed credentials never (#3735) |
  | Backends (Studio Backends) | post-M0: read the profiles | Read (its leaf spec). Profile edits (V) |
  | Settings sections (Studio Hosting) | post-M0: the resolved section and host (#3443, 31 October, item 4) | Read (its leaf spec) |
  | ETL definitions (ETL) | "Exact selected definition/draft, filters, validation and supported authoring" | Read; Reviewed draft update (its leaf spec). Create (W); delete (N); publish (H) |
  | ETL connectors (ETL) | "Exact connector definition, authorized bindings/metadata and validated proposals" | Read; Reviewed draft update (its leaf spec). Create (W); delete (N); publish (H) |
  | ETL runs (ETL) | "Exact run/partition and bounded evidence; supported explicit cancellation" | Read; Reviewed cancellation (its leaf spec). Start (N); completing a run (P: the target names only cancellation) |
  | Activity definitions (Activities Design; Workflows.Publishing; Elsa3) | "Exact catalog/version/dependency inspection and supported draft/import/upgrade proposals" | Read; Reviewed draft add, update and discard, and upgrade plans (its leaf spec). Import (C); publish (H); other definition and version operations, and test runs (P); contract proposals (W) |
  | User tasks (`Elsa.Studio.UserTasks`, Workflows Runtime) | post-M0: read; claim, release, reassign | Read; Reviewed claim, release and reassign (its leaf spec). Decisions (H); prefill values and responses (C); form authoring (W) |
  | Agents (Agent Specialists) | post-M0: read | Read (its leaf spec). Review decisions (H); the decider's identity (C); authoring (O: #3519) |
  | BPMN (Activities BPMN) | post-M0: read | Read (its leaf spec). Run and work operations, artifact registration, interchange management (T); direct publish (H); process authoring (W) |
  | Expressions (Expression Modules) | "Language-owned guidance and exact expression edits without runtime evaluation or live-value capture" | Read; Local expression edits in the draft's working state, as spec 1193's Operation Batch (its leaf spec, with #3711) |
  | Workflow rows (Workflows): definitions list, new workflow, workflow draft, Executables, Runs | "Preserve search/folder/status/page/selection; read exact resources and propose supported lifecycle changes"; "Carry caller attachment; return typed metadata suggestions or reviewed creation"; "Grounded explain/troubleshoot; atomic visible working-state edits, validation, dirty state and whole-batch undo"; "Inspect immutable compiled structure and selected source reference; dispatch remains a distinct authorized effect"; "Carry exact run/activity-execution/incident/scope; authorized evidence and supported wait/stop/recovery operations" | Delivered by #3442. Its refusals stand: run controls, library deletes, and edits to published or historical versions and Executables (O: spec 1193's own decisions) |

#### Packaging and ownership

- **FR-002**: **Server packages.** This spec's Program-authority decision is one Agent package per owning module.
  - **Name and path**: each package is named `Elsa.Agent.<Domain>`, at the path the architecture guard derives.
  - **Why the Agent bucket**: #3428 allows optional integrations to be "separately packaged with their owning module", but the ExtensionBoundary guard forbids core projects from referencing an extension. Framework §2.2's secondary-domain sub-rule does not apply, because these packages define their own Context Kind, tool and result models.
  - **References**: a package references its owning module's `.Core` where one exists, and the Agent packages in its own bucket. It also references `Elsa.Foundation.Identity.Core` where it evaluates a permission rule itself.
  - **Modules without a `.Core`**: the package admits only client-asserted kinds, and exists only where SC-004 needs it.
  - **Cross-bucket references**: a reference to another extension bucket, such as Etl, is declared in the ExtensionBoundary guard's edge list, with a justification.
  - **Leaf 1**:
    - adds `Elsa.Agent.Secrets`, which references Secrets `.Core`;
    - makes the Workbench reference and compose `Elsa.Agent.Secrets` (project reference, feature catalog and `shells.json`);
    - packages the Secrets API, Secrets EF and `Elsa.Agent.Secrets` for the probe host, and composes them in its baseline, with Secrets EF on SQLite, so that revision-guarded effects can succeed;
    - adjusts the probe's restart helper;
    - its test overlay holds the test secret store.
  - **Leaf 2**:
    - adds `Elsa.Agent.Connections`, which is distinct from `Elsa.Agent.Specialists.Connections`;
    - makes the Workbench reference and compose it, and adds the Connections EF store to the Workbench project and feature catalog for the durable host;
    - composes Connections, the Connections API and `Elsa.Agent.Connections` in the probe host;
    - its test overlay holds the test connection type.
  - **Leaf 3**:
    - adds the `Elsa.Foundation.Identity.Core` reference to `Elsa.Agent.Secrets`, which evaluates the source's disclosure rule;
    - its test overlay holds a test secret store that really removes values, and a secret-repository save fault that stays revision-aware, because the secret manager behaves differently with a repository that is not revision-aware.

  `Elsa.Agent.Workflows.Runtime` already exists (#3732), and later leaves extend it.
- **FR-003**: **Studio packages.** A Studio contribution MUST live in its owning Studio module, through the public SDK. A satellite package, `Elsa.Studio.Weaver.<Domain>` under `Extensions/Weaver/studio/`, is added only where a host must keep the module but drop its Weaver support. Framework §2.2's sub-rule does not apply to it, because a satellite defines its own Context Kind models. The Built-in Studio Screens are such a case. Their contributions go in `Elsa.Studio.Weaver.Hosting`, which leaf 5 creates, together with its SDK read seams.
- **FR-004**: Each module MUST ship its own Result Renderers and its own bounded, versioned module guidance, in its own packages. Module guidance is trusted, installed-module text, kept distinct from resource content. Shared Weaver code holds neither.

#### Generic changes

- **FR-005**: Weaver core, the shell's collector and review shell, and the provider adapters MUST gain no row's domain knowledge. These are the generic changes:
  - the privilege policy (FR-015), including each class's state in the bootstrap response;
  - the review shell showing an effect's classes and the *performed* outcome;
  - *performed* (FR-016);
  - declared module-code sets and their validation (FR-016);
  - a principal seam that hands a tool the caller's authenticated principal at preparation, approval, execution and the explicit authenticated execute of a recovered approval. Today a tool receives only the actor, the tenant and the target;
  - the registry-based answer of story 1;
  - the probe fixture's effects;
  - doc comments on the risk enum, matching FR-013;
  - SDK read seams for `Elsa.Studio.Weaver.Hosting` (leaf 5);
  - new outcome codes, only where no existing code fits.

  Leaf 0 delivers all of these except the SDK read seams. It also adds a dated pointer, "Amended by spec 1203", to spec 1186's session-policy contract and to spec 1189's recovery contract.

#### Owner prerequisites

- **FR-006**: Where a row needs a read, an authorization or an operation that today exists only behind the owning module's HTTP endpoints, or outside its `.Core`, the owning module MUST supply a `.Core` contract, implemented in its `.Api` package, that Weaver calls under the caller's principal.
  - **Principal**: the contract takes the principal from the principal seam (FR-005), checks that the actor matches it, and decides permissions through `IPermissionAuthorizationService`.
  - **Failing closed**: each contract fails closed by itself, as owner-unavailable, when its `.Api` feature is absent, or when `IPermissionAuthorizationService` is missing or duplicated. This holds on the tool path, where preparation runs before the host's tool authorizer, and on the context-provider path, which never passes that authorizer. It is tested once per tool and context provider (FR-014).
  - **Exceptions**: infrastructure exceptions are wrapped (framework §2.23.5).
  - **Writes**: a write to an existing resource checks an expected revision before any side effect. A composition that cannot honor the revision reports it as unsupported, so the write never runs unconditionally. A create uses the owning module's create-if-absent.
  - **Native pages**: native pages keep working, each with a regression test.

  The prerequisites below are contract-level. The plan names signatures, internal types and mechanisms.

  | Owning module and leaf | Prerequisite |
  | --- | --- |
  | Secrets, leaf 1 | A metadata read with the revision, the effective condition and the latest version's expiry. A revisioned metadata update. A value test at an expected version that reports whether it ran. |
  | Connections, leaf 2 | A read that adds the version and bindings, omits settings values, and shows bound non-owned and named-credential names only under `secrets:read`. Updates (display name, tags) and rebinds at an expected version. A rebind checks `secrets:read`, refuses as if the secret were unknown when that check fails, and refuses any secret under the `connection:{name}:…` convention. That convention is consolidated into one Connections `.Core` definition, which Integrations' OAuth2 grant and PKCE names use. A test at an expected version, checked before the external call, which reports a module code. Revoke at an expected version. A delete that checks the version before any side effect, and also deletes the connection's OAuth grant and PKCE secrets (fixing #3779; that PR says "Fixes #3779"). |
  | Secrets, leaf 3 | Revoke and delete at the revision, unavailable without a revision-aware repository. A fan-in (framework §2.6.1): the source contract `ISecretConsumerSource` and the Sequential event `SecretConsumersCollecting` in Secrets `.Core`, with their single aggregating handler in `Elsa.Secrets`. Secrets `.Core` gains a reference to `Elsa.Events.Core`. When no source answers, the result is "not checked", never "no consumers". |
  | Connections, leaf 3 | An `ISecretConsumerSource` implementation in `Elsa.Connections`. Per secret, it returns the binding connections, whether a connection owns the secret by the convention, and the disclosure rule for connection names (`workflows/design:read` or `connections:manage`). It is backed by a bounded consumer query that every shipped Connections repository implements, with a truncation flag. A repository that does not implement the query reports it as unsupported. |

- **FR-007**: **Read authority.** A rule that is exactly one permission MAY be checked through the tool's declared permission. Any other rule MUST be decided by the module that owns it, never reimplemented in a Weaver package. Where a review shows another module's names, the Agent package evaluates the disclosure rule that the source declares, and shows a count otherwise.
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
- **FR-012**: Each disclosure allowlist MUST be authored by the module that defines the data's meaning, and ship in that module's Agent package. It excludes unlisted fields by default. It is proven by a canary in an unlisted field that the owning module's own redaction misses.

#### Effects

- **FR-013**: Every effect MUST declare its risk, privilege classes, stale check and receipt.
  - **Risk order**: ReadOnly < ReviewRequired < Destructive < Admin. *Destructive* removes or replaces existing state. *Admin* touches sensitive or administrative state.
  - **Gating**: [ADR 0003](../../docs/adr/0003-weaver-direct-apply-risk-classification-is-elsa-owned.md) keeps classification Elsa-owned. Gating comes from the classes.
  - **Unaffected**: spec 1193's client working-state Destructive batches are unaffected.

  | Effect | Leaf | Risk | Classes | Stale check | Receipt |
  | --- | --- | --- | --- | --- | --- |
  | Secret metadata update | 1 | `Admin` | administrative | revision | readback; persisted |
  | Secret value test | 1 | `Admin` | administrative | version | *performed*, with success or failure and a module code; refused, with its code, when it could not run |
  | Connection prefill | 2 | `ReviewRequired` | none (`connections:manage`) | client binding | applied locally |
  | Connection update, or rebind of a field whose secret the connection does not own | 2 | `Admin` | administrative | version | readback; persisted |
  | Rebind away from an owned inline secret | 2 | `Admin` | administrative, irrecoverable | version | readback; persisted, with a warning if cleanup failed |
  | Connection test | 2 | `Admin` | administrative | version, before the external call | persisted, with the result and a module code; refused when it could not run; *uncertain* when the write fails after the external call |
  | Connection revoke | 2 | `Admin` | administrative, irrecoverable | version | readback; persisted |
  | Connection delete | 2 | `Admin` | administrative, irrecoverable | version | absence readback; persisted; *uncertain* when partial |
  | Secret revoke | 3 | `Admin` | administrative, irrecoverable | revision; consumer answer recomputed | readback; persisted |
  | Secret delete | 3 | `Admin` | administrative, irrecoverable | revision; consumer answer recomputed | absence readback; persisted; *uncertain* when partial |

- **FR-014**: **Common effect outcomes.** Every effect MUST be tested, at the cheapest level that proves it, for each of these:
  - **Review**: the effect is reviewed in every autonomy mode, including `full-auto`.
  - **Refusal**: the policy is read again at preparation, approval, execution and recovery. An effect whose class is off at a step is refused there, and the proposal ends. Nothing is dispatched at execution. An effect without the owning module's permission is refused before review.
  - **Owner-unavailable**: when the owner contract fails closed (FR-006), the outcome is owner-unavailable, with zero effect.
  - **Module removed at approval**: the effect is unavailable, with zero effect.
  - **Restart**: in the durable host, a pending approval survives a restart. A recovered approval runs only on an explicit authenticated execute, which rechecks the revision, the permission and the classes.
  - **Uncertain**: an effect whose response is lost after dispatch, or that may have run before a restart, is *uncertain*, and is never replayed. For a server effect, the receipt says what to check on the owning page. M4 has no Weaver resolution path for this; it is a recorded limitation (FR-020). For a client effect, the session owner resolves it, as spec 1189 defines.
  - **Local edits**: these carry correlated acknowledgements, and are unavailable, with zero effect, if their page is gone.
- **FR-015**: **Privilege policy** (owner decisions 1, 2 and 4). The Agent module MUST gain an integrator-configured privilege policy. It is a backend host option, never a session setting. It has three classes:
  - **administrative**: required by every `Admin` effect. A module may add other classes to an effect, but never remove this one.
  - **irrecoverable**: a module MUST declare this class for its own irrecoverable effects. A third-party irrecoverable effect that is not `Admin` and declares no class is not caught, which is a recorded limitation.
  - **activation**: feature selection apply on the Server host.

  Each effect declares a set of classes and needs all of them. The owning module declares the set at preparation, and it is frozen into the approval digest. Every class is off by default, so an unconfigured host refuses every classed effect. The Workbench enables all three in its base configuration, which is this spec's reading of the owner decisions (Assumptions). Studio shows the policy read-only, from the bootstrap response.
- **FR-016**: **Outcomes.**
  - ***Performed***: leaf 0 MUST add *performed* (owner decision 3).
    - It is a new proposal status that reports the action as executed.
    - It has a new no-persisted-effect receipt scope.
    - The executor's and the integrity check's validation accept that scope only for *performed*.
    - The plan names every server and Studio mapping site.
  - **Module codes**: a tool descriptor and a context-provider descriptor MAY declare a closed set of module codes. Every M4 tool and context provider declares one.
    - **Validation**: Agent core validates each code from a declaring source, in receipts, refusals, preparation errors, authorization violations and context diagnostics.
    - **Replacement**: a code outside the set is classified first, and then replaced by a generic code with that classification. A generic code that replaces an owner-unavailable code is itself outside `agent.`, so it stays owner-unavailable and never becomes forbidden.
    - **Mapping**: each `Elsa.Agent.<Domain>` package maps its owning module's native codes into its set. This includes splitting a code with two meanings, such as the Secrets `not-found` code used for both a missing secret and a missing configuration entry, into two declared codes.
    - **Format**: mapped codes are dotted ASCII identifiers outside `agent.`. A receipt carries its code in a new member, which the plan names.
    - **Undeclared sources**: sources that declare no set pass their codes through unchanged.
  - **Everything else**: every other outcome reuses the existing outcomes and codes. A new code is added only where none fits, such as the privilege-policy refusal. A status alone is never evidence of an effect.

#### Contracts, catalogs and records

- **FR-017**: Replacement contracts MUST detect a second registration (framework §2.6.2). The Agent-wide gap remains #3633. Visibility follows framework §2.23.3 and ADR 0080 D3.
- **FR-018**: **SemVer** follows framework §4.2, with pre-GA Majors under ADR 0080 D4 and Elsa constitution §E5.
  - **Major**:
    - *performed*: the new status, the receipt scope and the validation. These change model semantics, as owner decision 3 amends them.
    - the privilege policy: with its classes off by default, it refuses `Admin` tools, third-party ones included, that ran before.
    - freezing the class set into the approval digest: a server effect pending approval across the upgrade is refused as a binding mismatch, and an approved client invocation that is waiting becomes *uncertain* (story 1, scenario 12).
  - **Durable records**: the new fields live in the Agent EF store's protected payloads, including the proposal table's per-row payload, so there is no relational schema change.
    - Leaf 0 bumps the Agent schema family to 1.1.0.
    - Its reader accepts 1.0.0 rows of every record kind (sessions, messages, snapshots, contexts, proposals, keys and audit) and reads them with default values: no module code, and an empty class set. This follows [ADR 0077](../../docs/adr/0077-a-module-upgrades-in-place-only-when-its-persisted-schema-is-unchanged.md): a new schema-family version reads its predecessor's rows.
    - Older builds refuse 1.1.0 rows rather than silently dropping fields. Mixed-version clusters across leaf 0 are therefore unsupported while the Agent API is in preview.
  - **Minor**:
    - opt-in module-code sets and the receipt member;
    - new members on existing stores and contracts, added so that existing implementations keep compiling;
    - Secrets `.Core`'s reference to `Elsa.Events.Core`;
    - new contracts, events, packages, Context Kinds, tools, renderers, codes and permissions.
- **FR-019**: **Catalogs** MUST change with the code (framework §2.22.1).
  - **Leaf 0**:
    - `Extensions/Agent/src/Elsa.Agent/EXTENSION_POINTS.md` gains the privilege policy, class sets, module-code sets, the principal seam and *performed*;
    - `Extensions/Agent/src/Persistence/EntityFrameworkCore/EXTENSION_POINTS.md` gains the schema family 1.1.0 and the predecessor reads.
  - **Leaf 1**:
    - updates the Secrets Core, `Elsa.Secrets` and Secrets EF catalogs;
    - creates `Essentials/src/Secrets/Api/EXTENSION_POINTS.md`.
  - **Leaf 2**:
    - updates the Connections Api, Connections Core and `Elsa.Connections` catalogs, for the convention and the delete cleanup;
    - creates the Connections Api README.
  - **Leaf 3**:
    - the Secrets Core catalog gains the event and the source contract, with Connections' implementation as a cross-domain Known implementation;
    - the `Elsa.Secrets` catalog gains the handler;
    - the Connections Core catalog gains the new repository member;
    - the `Elsa.Connections` catalog gains the implementation, and leaf 3 creates that package's README with its cross-domain note;
    - the Connections EF catalog gains the bounded query.
  - **Leaf 5**: `Studio/src/essentials/Elsa.Studio.Api/EXTENSION_POINTS.md` gains the SDK read seams.
  - **Every leaf**:
    - lists its contributors in the Agent catalog, as cross-domain Known implementations;
    - gives each new package that exposes extension points its own catalog;
    - updates the root index (framework §2.22.2);
    - lists each package's cross-domain contributions in its README.
- **FR-020**: **Records.** Evidence goes on each PR, on the leaf's sub-issue, on #3443 and on #3428, which records M4's rows from FR-001 ([program goal](../../docs/program-goals/weaver-across-studio.md)).

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
  - Nothing that already exists is filed: context-budget progress goes on #3710, the OAuth grant leftover is #3779 (fixed in leaf 2), and Full payloads stay with Studio ADR 0009.

  Leaf 0 also records on #3428:
  - every narrowing;
  - the deferrals to other programs: #3604, and #3519's authoring;
  - the decisions open to owner review.
- **FR-021**: **Glossary.** Leaf 0 MUST make these changes.
  - **Elsa glossary** ([docs/glossary/elsa.md](../../docs/glossary/elsa.md)), for backend Agent concepts, each linked from Studio/CONTEXT.md:
    - the outcome words *proposed*, *approved*, *persisted*, *applied locally*, *refused*, *uncertain*, *performed*, *owner-unavailable*, *unavailable* and *stale*;
    - *Privilege Class*;
    - *Module code*;
    - *Secret consumer*.
  - **Studio/CONTEXT.md**, for Studio concepts:
    - *Module Load Status* names the diagnostics module status, `StudioModuleStatus`, whose values are available, loaded, disabled, incompatible and failed. The entry notes that the registry's status, `StudioModuleRegistryStatus`, adds pending and unknown. It replaces "discovered, accepted, activated, disabled, or rejected", and its _Avoid_ line stays.
    - *Privileged Tool* is widened to a Weaver Tool whose effect needs a privilege class.
    - *Built-in Studio Screen* is added: a screen that the `Elsa.Studio.Hosting` client renders itself.
    - *Client-Asserted Kind* is added: a Context Kind whose content only the browser supplies. It is untrusted, confers no authority, and the server checks only its schema and bounds.

#### Evidence

- **FR-022**: Each story MUST have end-to-end evidence.
  - **Restart cases**: these run in their leaf's durable host, except story 1's, which run in the probe host's restart configuration.
  - **Fixture cases**: these run in the probe host.
  - **Adapter coverage**: every tool and effect MUST have a conformance-probe case on both provider adapters: Copilot's delegated loop and Anthropic's host-driven loop.
  - **Negative cases**: these run at the cheapest level that proves them.
- **FR-023**: The canary search MUST cover HTTP bodies, SSE frames, adapter payloads, tool results, module codes, review cards and audit records.
- **FR-024**: Evidence MUST keep three categories distinct:
  - deterministic component tests;
  - deterministic provider-protocol evidence on the actual adapters, with packaged consumers;
  - opt-in live-provider evidence (none claimed).

  Probe evidence is frozen at the exact reviewed head. Failures, skips and quarantines are recorded as they occurred.

#### Later leaves

- **FR-025**: Each later leaf MUST get its own spec, numbered per [the spec lifecycle](../../docs/reference/spec-lifecycle.md#numbering), and its own sub-issue of #3443 that carries the leaf's acceptance. Both are created when that leaf starts. Each leaf spec is bound by FR-002 to FR-024 and by its FR-001 rows.
  - **Grouping**: each owning module gets its own leaf. Rows from different modules share a leaf only where they share a page or a journey. A module's rows may span several leaves for size.
  - **Assignment**: leaf 0 assigns every row its refresh adds. #3443 cannot close while any row is unassigned.

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
  | 15 | User tasks | reads; claim, release and reassign. Reassign is escalated if it stays `Admin` |
  | 16 | Agents | reads |
  | 17 | BPMN | reads |
  | 18 | Expressions | with #3711 |
  | 19 | Weather | a client-asserted page context, through a small package |
  | 20 | ExtensionBuilder | installed-module guidance |

### Key Entities

- **Privilege policy and class set**: the integrator's enabled classes, and the set of classes each effect declares and needs (FR-015).
- **Module-code set**: the closed codes a tool or context provider declares, and the mapping from native codes (FR-016).
- ***Performed* and its receipt scope**: the outcome of an approved action that persisted no change (FR-016).
- **Secret-consumer answer**: the binding connections, the ownership flag, the disclosure rule and the truncation flag, or "not checked" (FR-006, story 4).
- **Durable payload fields**: the class set, the module code and the *performed* scope, held in the Agent store's schema family 1.1.0 (FR-018).

## Success Criteria

### Measurable Outcomes

- **SC-001**: Every FR-001 row has linked evidence on #3428, and every narrowing states its grounds. No row is left unassigned.
- **SC-002**: Stories 1–4 pass their acceptance scenarios in the hosts FR-022 names. An unconfigured host refuses every classed effect.
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
  - **Sub-issues**: leaves 0–3 get sub-issues of #3443 when this spec merges. Each leaf PR says "Fixes #<leaf issue>, Refs #3443". The closing leaf also fixes #3443. Each PR maps the #3443 acceptance items it advances.
  - **Order**: leaves 1–3 depend on leaf 0, and each depends on the leaf before it.
  - **Status**: the PR that merges this spec sets it to *Approved*. Leaf 0's PR sets *In progress*, and the closing leaf's PR sets *Implemented*.
  - **Bookkeeping**: every leaf comments on #3428 and #3443 at each state transition.
- **Program owner decisions, 2026-11-03** (Sipke Schoorstra; [receipt](https://github.com/elsa-workflows/elsa-foundation/issues/3428#issuecomment-6612627295)). The receipt's "spec 1202" is this spec, before it was renumbered ([note](https://github.com/elsa-workflows/elsa-foundation/issues/3443#issuecomment-6614698826)):
  1. **Irrecoverable deletes and revokes** of secrets and Service connections: "Let the integrator decide what privileges weaver has via configuration."
  2. **Feature selection apply**: "Let the integrator decide via configuration." This widens #3443's activation boundary only for hosts that enable the activation class.
  3. **A generic outcome for an approved action that persists nothing**: "Add it in leaf 0."
  4. **The privilege default**: "Off; Workbench always on."
- **Decisions taken under Program authority, open to owner review**:
  - **Workbench classes**: the Workbench enables all three classes in its base configuration. This is this spec's reading of owner decisions 1, 2 and 4.
  - **Packaging**: one Agent package per owning module.
  - **Class sets**: declared by consequence, satisfied in full, and frozen into the approval digest. Every `Admin` effect requires the administrative class.
  - **Secret revoke and delete**:
    - consumers come from Connections, including ownership;
    - owned secrets are refused, which is stricter than the native page;
    - an effect fails closed when no source answers, so hosts without Connections cannot do this through Weaver;
    - the answer is recomputed at approval and at execution.
  - **Connection delete**: it also deletes the OAuth grant and PKCE secrets (#3779). Consumers that resolve a connection by name are not detected.
  - **Module codes**: declared closed sets, mapped by each Agent package.
  - **Ground P choices**:
    - module administration;
    - feed operations;
    - configuration-key secret create and rotate;
    - named-credential binding;
    - the default layout;
    - completing an ETL run;
    - the other activity definition operations and test runs.
  - **Console**: server reads of records are narrowed (X).
  - **Uncertain server effects**: these have no M4 resolution path.
  - **Spec 1193's refusals**: they stand (O).
  - **Expressions**: delivered as Local edits with #3711.
  - **Post-M0 targets**: user tasks get read, claim, release and reassign, with reassign flagged. Agents, BPMN and backends are read-only.
  - **Runtime capture**: its semantics are untouched. Full payloads wait for Studio ADR 0009.
  - **Browser-only writes**: these are narrowings (V).
  - **Personal layout**: changes to it are delivered.
  - **Cancellation**: ETL cancellation is reviewed. BPMN run operations are outside the target (T).
- **Plan decisions**:
  - **P1**: each package's shell feature.
  - **P2**: the contract signatures, internal names, the principal seam and the revision mechanisms; where permission constants live; and payload placement.
  - **P3**: every non-atomic compare-then-write window, and how it is bounded.
- **Overlap**, reconciled without reparenting:
  - ETL: #2894, #2906 and the ETL defects (#3709, #3719, #3721, #3724, #3562).
  - Home dashboards: #3604.
  - The parked Diagnostics Observability Readiness and Runtime Execution Evidence goals.
  - User Tasks: #3688.
  - Studio Expression Developer Experience: #2310, #3711 and #2349. No children are added under #2310.
  - Elsa 3.8/3.9 Parity: #2771, which owns Elsa 3 import.
  - The [BPMN Engine program](../../docs/program-goals/bpmn-engine.md).
  - Agents: #3519. Runtime agents: #2892.
  - Weaver: #3710, #3720 and #3761.
  - Feed credentials: #3735.
  - OAuth grant leftover: #3779, fixed in leaf 2.
- **Out of scope**:
  - releases, deployments and provider purchases;
  - live-provider credentials;
  - blanket administration;
  - arbitrary workspace edits;
  - package promotion and package activation, and publisher activation. Feature selection apply is in scope only through the activation class.
  - integrated qualification, which remains #3444.

## Issue Traceability

| #3433 criterion | Requirements and stories |
| --- | --- |
| Home/dashboard, ETL, Connections, Secrets metadata, Diagnostics, runtime evidence, activity availability and installed settings/administration surfaces have module-owned support | FR-001 to FR-004, FR-025; stories 2–4 |
| Each supported action has context, tool, renderer/effect and negative-case evidence; absent mutation support is explicit | FR-001, FR-004, FR-013, FR-014, FR-022 |
| Secret values and unrelated data are not exposed; runtime/model semantics and existing domain ownership remain intact | FR-002, FR-006, FR-007, FR-009, FR-012; Assumptions; owner decision 3 for *performed* |
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
| Tasks: refresh, implementation and evidence, gates and reconciliation | FR-001, FR-006, FR-020, FR-022 to FR-025; SC-008; Delivery |
