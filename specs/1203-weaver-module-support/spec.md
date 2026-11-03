# Feature Specification: Module-owned Weaver support for the current Studio inventory

**Feature Branch**: `agent/claude/3443-weaver-module-support`

**Created**: 2026-11-03

**Status**: Draft

**Input**: [Feature #3443](https://github.com/elsa-workflows/elsa-foundation/issues/3443), M4 ([#3433](https://github.com/elsa-workflows/elsa-foundation/issues/3433)) of [Program #3428](https://github.com/elsa-workflows/elsa-foundation/issues/3428). The issue is [in progress](https://github.com/elsa-workflows/elsa-foundation/issues/3443#issuecomment-6612633571).

The Program owner's standing authorization covers autonomous delivery within #3443's boundaries, as amended by the owner decisions under Assumptions. It grants no blanket administration permission, no arbitrary workspace editing, and no package promotion or activation.

The scope is:
- the M0 inventory in [docs/reports/weaver-baseline.md](../../docs/reports/weaver-baseline.md);
- #3443's planning comments of 30 and 31 October;
- the `/user-tasks` and `/agents` rows that the [claim](https://github.com/elsa-workflows/elsa-foundation/issues/3443#issuecomment-6605270638) adds;
- the screens merged since M0: `/settings/backends` (PR #3600) and the BPMN pages (PR #3603).

Prerequisites #3440, #3441 and #3442 are complete.

**Review**: Seventeen independent review rounds took place on 2026-11-03, each with a correctness lens and a standards lens, and every finding was verified against `origin/main`. The approval basis is the Program owner's standing authorization and the owner decisions recorded under Assumptions.

This spec contains three things:
- the M4 delivery matrix (FR-001);
- the cross-cutting requirements that every leaf must meet;
- leaves 0–3, specified as stories 1–4.

Each later leaf gets its own spec (FR-025). The plan PR, the one that adds `plan.md`, also adds `contracts/privilege-policy.md` and `contracts/outcomes.md`.

**Terms**:
- **Glossary.** Studio terms have the meanings in [Studio/CONTEXT.md](../../Studio/CONTEXT.md), and *Service connection* has its meaning in the [Elsa glossary](../../docs/glossary/elsa.md). Leaf 0 adds the Weaver outcome words and this spec's new terms to the Elsa glossary (FR-021).
- **Outcome words:**
  - *Proposed*, *approved*, *persisted*, *refused* and *uncertain*: an effect's lifecycle states.
  - *Applied locally*: a change made to visible, unsaved state only.
  - *Performed*: added by leaf 0. The approved action ran, and the owning module persisted no change.
  - *Owner-unavailable*: introduced by spec 1193's slice A (`AgentViolation.OwnerUnavailable`). FR-006 widens it to cover an absent owner feature.
  - *Unavailable*: an effect that fails closed.
  - *Stale*: an effect refused because its target changed after review.
- **"Owner"** always carries a qualifier:
  - the *owning module* owns a row's resource;
  - the *Program owner* is Sipke Schoorstra;
  - a Service connection's *Owner field* is part of its metadata.
- **Module code:** a code that a tool or context provider reports from its declared set (FR-016).
- **Secret consumer:** a resource that uses a secret, such as a Service connection that binds or owns it. This is distinct from a workflow's *Secret Reference*.
- **Names:** domain nouns keep their current names until their own [ADR 0080 D5](../../docs/adr/0080-elsa-4-simplification-decisions.md#d5--plain-language-renamed-now) batch, so no type is renamed twice. For example, new Attention packages and types keep "Attention", and are renamed in Attention's batch. New contributor-style interfaces use the `I<X>Extension` suffix now.

**Hosts**:
- **Normal host:** the Workbench backend (`Elsa.Workbench`) with Studio (`Elsa.Studio.Web`), Design and Runtime combined, in the Development configuration.
- **Durable host:** the normal host plus the Agent EF store, through the overlay that the Workbench's durable-store test applies. Secrets EF is already composed. From leaf 2 on, the host also includes the Connections EF store, which leaf 2 adds to the Workbench project and feature catalog.
- **Probe host:** the conformance probe's packaged consumer. Its restart configuration enables the Agent EF store through runtime configuration. Leaves 1 and 2 change it (FR-002).
- **Test overlay:** one definition per leaf, applied to whichever host a case needs. It adds that leaf's test-only fixtures (FR-002).

## User Scenarios & Testing

Each story's test also covers the common controls (FR-009), the common effect outcomes of each effect (FR-014), and the canary search (FR-023). Every tool and effect has a conformance-probe case on both provider adapters (FR-022). Each step runs in a single host.

### User Story 1 - Explicit answers, the privilege policy and *performed* (leaf 0) (Priority: P1)

A user asks about a module that is not installed or is disabled, or about a page this feature does not support. Weaver answers from two live sources: the Studio module registry, which module load status shows, and the Weaver contributions each host registers. It never answers from a route, or from M0's historical module list.

The user can also see:
- which privilege classes the integrator has enabled;
- the classes each effect needs, in its review;
- *performed*, for an approved effect that persists nothing.

**Why this priority**: Every later leaf depends on this one.

**Independent Test**:
1. In the normal host, run leaf 0's inventory refresh. ExtensionBuilder is disabled by default.
2. Deselect structured logs: its backend feature, its EF store (its only dependant) and its Studio module.
3. Ask Weaver about structured logs and about the extension builder. Then open `/diagnostics/structured-logs` and ask again.
4. Enable ExtensionBuilder in the backend configuration and in Studio's shells, open it, and ask again.
5. In the probe host, extend the fixture module (leaf 0) with:
   - an administrative effect;
   - an effect that is both administrative and irrecoverable;
   - an effect that persists nothing;
   - a tool and a context provider that each declare a module-code set and emit a code outside it, one of which echoes a canary;
   - an authorization violation that carries such a code.
6. Propose each effect in four configurations:
   - classes unconfigured;
   - only the administrative class enabled;
   - all three classes enabled, as the Workbench's base configuration has them;
   - a class switched off between review and approval, between approval and execution, and before recovery.
7. In the probe host, deselect the fixture module while a page nests its widget.
8. In the probe host's restart configuration:
   - leave an effect pending, restart, remove the fixture module, and approve;
   - simulate the leaf 0 upgrade with one server effect pending approval and one approved client invocation waiting.

**Acceptance Scenarios**:

1. **Given** a module that the registry does not list, **When** the user asks about it or opens its route, **Then** Weaver answers that it is not installed, and the route is the only context it discloses.
2. **Given** a disabled module, or an enabled module without Weaver contributions, **When** the user asks for help on its page, **Then** Weaver says so and offers no tool.
3. **Given** a deselected module, **When** a page that would nest its widget is open, **Then** nothing of that module is offered, and other modules' support is unchanged.
4. **Given** unconfigured classes, **When** any classed effect is proposed, **Then** it is refused before review.
5. **Given** only the administrative class enabled, **When** an administrative and irrecoverable effect is proposed, **Then** it is refused before review. **When** an administrative-only effect is proposed, **Then** it is reviewed.
6. **Given** all three classes enabled, **When** each effect is proposed, **Then** it is reviewed, and its review shows the classes it needs.
7. **Given** a bootstrap request, which carries no session, **When** the host answers it, **Then** the answer reports every class's state, and Studio shows the states read-only.
8. **Given** an effect with a class that is off at approval, execution or recovery, **When** that step is reached, **Then** the proposal is refused there and ends. Nothing is dispatched. The user needs a new proposal once the class is enabled.
9. **Given** an approved effect whose owning module persisted nothing, **When** it is recorded, **Then** it is *performed*, with the no-persisted-effect scope, and the review shell shows it.
10. **Given** a code outside the declared set, **When** it is recorded, **Then** it is reported as a generic code that keeps an owner-unavailable code's classification, and no canary is disclosed. This applies to a code in a receipt, refusal, preparation error, authorization violation or context diagnostic.
11. **Given** a pending effect whose module is removed, before or after a restart, **When** it is approved or recovered, **Then** it is unavailable, with zero effect.
12. **Given** a server effect pending approval across the leaf 0 upgrade, **When** it is approved, **Then** it is refused as a binding mismatch and never executed. **Given** an approved client invocation waiting across the upgrade, **Then** it is *uncertain* (FR-018).

---

### User Story 2 - Inspect, describe and test Secrets without exposing a value (leaf 1) (Priority: P2)

A user on the Secrets page (`/security/secrets`, or the alias `/secrets`) asks which secrets exist, why one is expired, or whether one is usable, or asks Weaver to improve a description. Weaver reads each secret's metadata:
- name, display name and description;
- type, store, scope, tags and status;
- the effective condition and the latest version's expiry, both computed by the owning module;
- current version, revision, and created and updated times.

Weaver never discloses a value. It can propose a metadata update or the owning module's value test.

**Why this priority**: Secrets is the smallest row, with the highest exclusion risk. It sets the pattern for the rest.

**Independent Test**: In the normal host with leaf 1's test overlay, seed:
- an active secret, one whose latest version has expired, and a revoked secret, each holding a canary value;
- a secret whose test message echoes its value;
- a secret in another tenant.

Read the list on both routes, improve a description, test a secret, and compare each receipt with the owning module's readback.

Negative cases:
- no `secrets:read`, no `secrets:write`, or no `secrets:test`;
- a target in another tenant;
- an edit or rotation in another tab between review and approval;
- a test of a missing or inactive secret;
- the Secrets EF store deselected, so revisions cannot be checked;
- the administrative class off;
- a restart with an approved effect pending, in the durable host;
- requests to create, rename, reveal, rotate, import, export, revoke or delete.

**Acceptance Scenarios**:

1. **Given** the list with a filter and a selection, **When** Weaver reads context, **Then** it receives the filter, the selected identities and their metadata, bounded by `secrets:read` and the tenant. An expired secret shows as expired.
2. **Given** an approved description update, **When** it is applied, **Then** it is saved at the reviewed revision, and the receipt carries the readback. Without a revision-aware repository, the update is unavailable.
3. **Given** an approved test, **When** it runs at the reviewed version, **Then** it is *performed*, and its receipt carries only success or failure and the module code. A test of a missing or inactive secret is refused, with the module code.
4. **Given** a request to create, rename, reveal, rotate, import or export, **When** Weaver receives it, **Then** it refuses on the grounds in FR-001. **Given** a request to revoke or delete, **Then** in leaf 1 Weaver says this is not yet supported.

---

### User Story 3 - Understand, prepare, update, rebind, test, revoke and delete a Service connection (leaf 2) (Priority: P3)

A user on the Connections page, in its creation form, or at a connection picker in a workflow asks what a connection is or why it fails. The user can also have Weaver prepare, update, rebind, test, revoke or delete the connection.

Through a new read in the owning module (FR-006), Weaver sees:
- the connection's name, display name, type, authentication scheme and status;
- its settings field names and secret-bound field names;
- its managing party and Owner field;
- its tags, last test time and version.

Bound non-owned secret names and named-credential names are disclosed only under `secrets:read`. Settings values are never disclosed.

**Why this priority**: "Why does my integration fail" is the most common Connections question.

**Independent Test**: In the normal host with leaf 2's test overlay, use a test connection type that records invocations, carries canary token fields, and returns a test message that echoes a resolved value. Seed:
- a connection with canary settings, secret and token values;
- a connection with an owned inline secret and an OAuth grant secret;
- active, expired and inactive text secrets;
- another connection's owned inline secret, grant secret and PKCE secret;
- a connection in another tenant.

Explain each connection. Then prefill, update, rebind, test, revoke and delete, and compare each receipt with the owning module's readback.

Negative cases:
- an edit, or a user-run test that ran, between proposal and approval;
- no `workflows/design:read` and no `connections:manage` (reads);
- no `secrets:read` (bound names, rebind);
- no `connections:manage` (prefill, update, rebind, revoke, delete);
- no `connections:bind-secret`;
- no `connections:test`;
- a target in another tenant;
- a rebind to an expired, inactive or non-text secret;
- a rebind to another connection's owned inline, grant or PKCE secret;
- a rebind away from the owned inline secret, where cleanup fails after commit;
- a test that cannot run, one that fails, and one whose write fails after the external call;
- a test message or module code that echoes a resolved value;
- a dropped test response;
- a delete that removes owned secrets but fails to remove the connection;
- the creation form closed before the prefill completes;
- the administrative or irrecoverable class off;
- a restart with an approved effect pending, in the durable host;
- requests to create a connection, authorize OAuth, change the redirect URI, bind a named credential, replace an owned inline secret's value, or set a settings value.

**Acceptance Scenarios**:

1. **Given** the creation form, **When** Weaver reads it, **Then** it sees the type, scheme, field names and validation errors, and no entered value. **Given** an approved prefill under `connections:manage`, **Then** only the form's name, type and scheme change, applied locally. Creating the connection stays the user's action.
2. **Given** an approved update or rebind, **When** it is applied, **Then** it applies at the reviewed version under `connections:manage`. A rebind also needs:
   - `connections:bind-secret` and `secrets:read`;
   - an active text secret in the tenant;
   - a secret outside the connection-owned name convention (`connection:{name}:…`), which covers inline, OAuth grant and PKCE secrets.

   The candidates shown to the client grant nothing.
3. **Given** a rebind away from an inline secret that the connection owns, **When** it is reviewed, **Then** it needs the administrative and irrecoverable classes. The review states that the secret is deleted after commit, unless another connection still uses it, and that the deletion cannot be undone. It also states that only other connections are checked. If cleanup fails after commit, the effect is persisted with a warning.
4. **Given** an approved test, **When** it runs, **Then** the reviewed version is checked before the external call. When the type's test runs, the owning module records the test time and advances the version, and other pending proposals for that connection go stale. The receipt is persisted with the result and the module code, and the message is redacted. A test that cannot run is refused, with the module code. A test whose write fails after the external call is *uncertain*.
5. **Given** a revoke or delete, **When** it is reviewed, **Then** it needs the administrative and irrecoverable classes. The review states that it cannot be undone and that workflows using the connection will fail. For a delete, it also:
   - lists by field name the owned inline secrets that will be deleted;
   - states that a secret another connection still uses is kept;
   - states that the OAuth grant secret is kept.
6. **Given** an approved revoke or delete, **When** it runs, **Then** the reviewed version is checked before any side effect. A delete's receipt reads back the connection's absence. A delete that removes owned secrets but fails to remove the connection is *uncertain*.

---

### User Story 4 - Revoke and delete a secret, knowing its connection consumers (leaf 3) (Priority: P4)

On the Secrets page, a user asks Weaver to revoke or delete a secret. The review shows the consumers Connections reports: the connections whose fields bind the secret, and whether a connection owns it. Weaver refuses owned secrets, and states what it cannot detect.

**Why this priority**: Irrecoverable secret effects are only safe once their consumers are known, to the extent they can be.

**Independent Test**: In the normal host with leaf 3's test overlay, which adds a secret-repository save fault, seed:
- a secret bound by two connections;
- an unused secret;
- an owned inline secret;
- an OAuth grant secret;
- a secret whose delete removes the value and then fails to save the metadata.

Revoke and delete each through Weaver, and compare each receipt with the owning module's readback.

Negative cases:
- a binding added or removed between review and approval, or between approval and execution;
- a truncated consumer answer whose page or count changes;
- no consumer source answering, either because Connections is not composed or because its repository query is unsupported;
- a repository that is not revision-aware;
- an actor who cannot read connections;
- no `secrets:delete`;
- the partial delete;
- the administrative or irrecoverable class off;
- a restart with an approved effect pending, in the durable host.

**Acceptance Scenarios**:

1. **Given** a revoke, **When** it is reviewed, **Then** the review states that the revoked version's value cannot be restored, and that rotating reactivates the secret with a new value. **Given** a delete, **Then** the review states that it cannot be undone. Both need `secrets:delete` and the administrative and irrecoverable classes.
2. **Given** a review, **When** consumers are listed, **Then** the list is advisory. It shows the binding connections by name when the actor meets the read rule that Connections declares, and as a count otherwise. A truncated answer says so. The review states that workflow references and Nuplane feed credentials are not detected.
3. **Given** a secret that a connection owns, as an inline, OAuth grant or PKCE secret, **When** a revoke or delete is proposed, **Then** Weaver refuses it before review. This is stricter than the native page. The user can act on the native pages instead.
4. **Given** no consumer source answered, **When** a revoke or delete is proposed, **Then** the review states that connection bindings could not be checked, and the effect is refused before review. This covers Connections not composed, an older Connections without the source, Connections in another shell, and an unsupported repository query. As a recorded limitation, a host without Connections cannot revoke or delete secrets through Weaver; the user acts on the native page.
5. **Given** a repository that is not revision-aware, **When** a revoke or delete is proposed, **Then** it is unavailable.
6. **Given** an approved revoke or delete, **When** it is approved, and again when it runs, **Then** the consumer answer is recomputed and compared by full identity, not by the count a non-reader sees. A truncated answer is stale if its page or its count changes. Any change makes the proposal stale. The reviewed revision is checked before any side effect.
7. **Given** a delete, **When** it runs, **Then** its receipt reads back the secret's absence, because a deleted secret is hidden. If the value is removed but the metadata save fails, the outcome is *uncertain*.

## Requirements

### Functional Requirements

#### Delivery matrix

- **FR-001**: #3428 owns the Program acceptance matrix, and records M4's rows from this table.
  - **Inventory refresh:** leaf 0 MUST refresh the normal host's live, composed module inventory, and record the result on #3443. M0's module list is historical.
  - **Changed rows:** a row the refresh finds absent or changed is updated by leaf 0's PR. A screen the refresh finds that the table lacks is added, and assigned under FR-025.
  - **Outcomes:** every row MUST end at its outcome and evidence. Every narrowing of an M0 target, and every existing owner operation that Weaver does not offer, MUST state its grounds.

  **Outcome words:**
  - **Read:** read and explain.
  - **Reviewed:** a reviewed effect.
  - **Local:** a reviewed edit of visible, unsaved state.
  - **Delivered by #3442:** delivered in M3.
  - **None in M4:** not delivered in M4.

  **Grounds:**
  - **A:** absent or disabled in the refreshed inventory.
  - **B:** the Program boundary: secret values, package promotion and activation, and module and feed administration. Feature selection apply is outside B where the activation class is enabled (owner decision 2).
  - **C:** a credential, file or value would pass through Weaver.
  - **D:** the owning module does not hold it durably or cluster-wide.
  - **H:** a human's decision.
  - **N:** no owner operation exists.
  - **O:** owned by another issue, spec or open decision.
  - **S:** the screen does not show it.
  - **T:** outside the row's target. For post-M0 rows, the target is this spec's choice, and is open to owner review.
  - **V:** the write is reachable only from the browser, so the server cannot verify its result.
  - **W:** needs a module-owned working-state adapter.
  - **X:** needs its own exclusion design first.

  Each M0 target is quoted from the baseline. Rows other than Secrets and Service connections give their outcome here; their leaf specs give the details.

  | Row (owning module) | Target (M0, quoted; or post-M0) | Outcome; narrowings (grounds) |
  | --- | --- | --- |
  | Secrets (Secrets) | "Reference/metadata/availability inspection; domain-authorized effects, with no secret values in context" | Read; Reviewed metadata update and value test (story 2), revoke and delete (story 4). Value-bearing create and rotate (B); reveal (C); configuration-key create and rotate (T); rename, import, export (N); the workflow property's Secrets editor (O: #3711) |
  | Connections (Connections) | "Metadata, bindings and diagnostics; supported changes/test actions without credentials in context" | Read; Local prefill; Reviewed update, rebind, test, revoke and delete (story 3). Settings values (X: no field declares its sensitivity); replacing an owned inline secret's value (B); create and OAuth authorize (C); redirect URI (N: read-only public URL); named-credential binding (T) |
  | Optional sample: Weather (Samples, Studio-only) | "Qualify only when installed; no current composed-support claim" | Read of a client-asserted page context when installed (its leaf spec); None in M4 when absent (A) |
  | Optional ExtensionBuilder (ExtensionBuilder) | "Optional inspection/guidance; no source-control push, package promotion or publisher activation" | Read, as installed-module guidance when enabled (its leaf spec). Workspace source inspection (X); None in M4 while disabled (A) |
  | Structured logs (StructuredLogs) | "Current filters/time range/selected records; bounded redacted correlation" | Read (its leaf spec). Time range and retention (S) |
  | Traces (OpenTelemetry) | "Exact trace/span/resource/query with authorized detail and availability" | Read (its leaf spec). Metrics and OpenTelemetry logs (S) |
  | Diagnostics (Studio Hosting and diagnostic modules) | "Accepted/rejected/disabled module state and bounded visible diagnostic results" | Read (its leaf spec). Module administration (B) |
  | Console (`Elsa.Studio.ConsoleStream`, console log streaming) | "Current source/filter/record/truncation/freshness and authorized bounded inspection" | Read (its leaf spec). Filter (S: none exists); server reads of records (X); lines the user attaches are the user's own client-asserted disclosure (FR-009) |
  | Home (Studio Dashboard, Studio Preferences, Attention, Workflows.Dashboard) | "Explain visible widget snapshots, filters, freshness and unavailable data; module-owned supported preferences" | Read; Reviewed personal layout change and Attention Item snooze (its leaf spec). Default layout (T); new widgets and saved queries (O: #3604) |
  | Runtime evidence (Workflows Runtime) | "Explain capture policy and exact proposed configuration differences" | Read; Reviewed Workflow Runtime Diagnostics Setting change within today's capture rules (its leaf spec). Full payloads (O: Studio ADR 0009); durable, cluster-wide setting (D) |
  | Activity availability (Activities Design) | "Effective availability/diagnostics and supported settings changes" | Read; Reviewed change (its leaf spec) |
  | Features (FeatureManagement client, Modularity server) | "Selected host, revision, filters, settings draft and reviewed differences through existing authorization" | Read; Local draft; Reviewed Server-host apply in the activation class (its leaf spec). Studio-host apply (V); configuration values and edits (C) |
  | Appearance/themes (Studio Hosting, Hosting.Themes) | "Supported preference/theme edits, exact persistence semantics and built-in/read-only constraints" | Read; Local appearance and builder edits (its leaf spec). Theme Store writes (V) |
  | Modules/feeds (Hosting.Nuplane; client Studio Hosting) | "Explain exact host/package/feed/reconcile state; package promotion/activation remains outside authorization" | Read (its leaf spec). Package and feed operations (B); feed credentials never disclosed (#3735) |
  | Backends (Studio Backends) | post-M0: read the profiles | Read (its leaf spec). Profile edits (V) |
  | Settings sections (Studio Hosting) | post-M0: the resolved section and host (#3443, 31 October, item 4) | Read (its leaf spec) |
  | ETL definitions (ETL) | "Exact selected definition/draft, filters, validation and supported authoring" | Read; Reviewed draft update (its leaf spec). Create (W); delete (N); publish (H) |
  | ETL connectors (ETL) | "Exact connector definition, authorized bindings/metadata and validated proposals" | Read; Reviewed draft update (its leaf spec). Create (W); delete (N); publish (H) |
  | ETL runs (ETL) | "Exact run/partition and bounded evidence; supported explicit cancellation" | Read; Reviewed cancellation (its leaf spec). Start (N); completing a run (T: the target names only cancellation) |
  | Activity definitions (Activities Design; Workflows.Publishing; Elsa3) | "Exact catalog/version/dependency inspection and supported draft/import/upgrade proposals" | Read; Reviewed draft add, update, discard and upgrade plans (its leaf spec). Import (C); publish (H); other definition and version operations, and test runs (T); contract proposals (W) |
  | User tasks (`Elsa.Studio.UserTasks`, Workflows Runtime) | post-M0: read; claim, release, reassign | Read; Reviewed claim, release, reassign (its leaf spec). Decisions (H); prefill values and responses (C); form authoring (W) |
  | Agents (Agent Specialists) | post-M0: read | Read (its leaf spec). Review decisions (H); the decider's identity (C); authoring (O: #3519) |
  | BPMN (Activities BPMN) | post-M0: read | Read (its leaf spec). Run and work operations, artifact registration, interchange management (T); direct publish (H); process authoring (W) |
  | Expressions (Expression Modules) | "Language-owned guidance and exact expression edits without runtime evaluation or live-value capture" | Read; Reviewed expression edits (its leaf spec, with #3711) |
  | New workflow, definitions list, draft, Executables, Runs (Workflows) | M3 rows | Delivered by #3442. Its refusals stand: run controls, library deletes, and edits to published or historical versions and Executables (O: spec 1193's own decisions) |

#### Packaging and ownership

- **FR-002**: **Server packages.** This spec's Program-authority decision is one Agent package per owning module, named `Elsa.Agent.<Domain>`, at the path the architecture guard derives. #3428 allows optional integrations to be "separately packaged with their owning module", but the ExtensionBoundary guard forbids core projects from referencing an extension. Framework §2.2's secondary-domain sub-rule does not apply, because these packages define their own Context Kind, tool and result models.
  - **References:** a package references its owning module's `.Core` where one exists, the Agent packages in its own bucket, and `Elsa.Foundation.Identity.Core` where the package itself applies a permission rule. A package for a module without a `.Core` admits only client-asserted kinds, and exists only where SC-004 needs it. A reference to another extension bucket, such as Etl, is declared in the ExtensionBoundary guard's edge list, with a justification.
  - **Leaf 1:**
    - adds `Elsa.Agent.Secrets`, which references Secrets `.Core`;
    - packages the Secrets API, the Secrets EF store and `Elsa.Agent.Secrets` for the probe host;
    - composes them in the probe baseline, with Secrets EF on SQLite, so revision-guarded effects can succeed;
    - adjusts the probe's restart helper;
    - its test overlay holds the test secret store.
  - **Leaf 2:** adds `Elsa.Agent.Connections`, which is distinct from `Elsa.Agent.Specialists.Connections`, and composes Connections, the Connections API and it in the probe host. Its test overlay holds the test connection type.
  - **Leaf 3:** adds the `Elsa.Foundation.Identity.Core` reference to `Elsa.Agent.Secrets`, which then applies the Connections name rule. Its test overlay holds a secret-repository save fault. The fault decorates the Secrets EF store and stays revision-aware, because the secret manager uses a revision-aware repository differently from one that is not.
  - `Elsa.Agent.Workflows.Runtime` exists already (#3732), and later leaves extend it.
- **FR-003**: **Studio packages.** A Studio contribution MUST live in its owning Studio module, through the public SDK. A satellite `Elsa.Studio.Weaver.<Domain>`, under `Extensions/Weaver/studio/`, is added only where a host must keep the module but drop its Weaver support. Framework §2.2's sub-rule does not apply to satellites either. The Built-in Studio Screens are such a case: their contributions go in `Elsa.Studio.Weaver.Hosting`, which leaf 5 creates together with its SDK read seams.
- **FR-004**: Each module MUST ship its own Result Renderers and its own bounded, versioned module guidance, in its own packages. Module guidance is trusted installed-module text, kept distinct from resource content. Shared Weaver code holds neither.

#### Generic changes

- **FR-005**: Weaver core, the shell's collector and review shell, and the provider adapters MUST gain no row's domain knowledge. These are the generic changes:
  - the privilege policy (FR-015), including the bootstrap report of each class's state;
  - the review shell showing an effect's classes and *performed*;
  - *performed* (FR-016);
  - declared module-code sets and their validation (FR-016);
  - a principal seam, which hands a tool the caller's authenticated principal at preparation, approval and execution, and at the explicit authenticated execute of a recovered approval. Today a tool receives only the actor, the tenant and the target;
  - the registry-based answer in story 1;
  - the probe fixture's effects;
  - doc comments on the risk enum that match FR-013;
  - SDK read seams for `Elsa.Studio.Weaver.Hosting` (leaf 5);
  - new outcome codes, only where no existing code fits.

  Leaf 0 delivers all of these except the SDK read seams. It also adds a dated "Amended by spec 1203" pointer to spec 1186's session-policy contract and to spec 1189's recovery contract.

#### Owner prerequisites

- **FR-006**: Some rows need a read, an authorization or an operation that today exists only behind the owning module's HTTP endpoints or outside its `.Core`. For each, the owning module MUST supply a `.Core` contract, implemented in its `.Api` package, that Weaver calls under the caller's principal.
  - **Principal:** the contract takes the principal from the principal seam (FR-005), checks that the actor matches it, and decides permissions through `IPermissionAuthorizationService`.
  - **Absence:** if the `.Api` feature is not composed, the contract is absent and the outcome is owner-unavailable, tested once per tool (FR-014). A missing authorization service is denied earlier, by the host's tool authorizer, as forbidden.
  - **Exceptions:** infrastructure exceptions are wrapped (framework §2.23.5).
  - **Writes:** a write to an existing resource checks an expected revision before any side effect. A composition that cannot honor the revision reports it as unsupported, so the write never runs unconditionally. A create uses the owning module's create-if-absent.
  - **Native pages:** native pages keep working, each with a regression test.

  Contract-level names follow; the plan names the signatures and the internal types.

  | Owning module and leaf | Prerequisite |
  | --- | --- |
  | Secrets, leaf 1 | A metadata read with the revision, the effective condition and the latest version's expiry. A revisioned metadata update. A value test at an expected version, which reports whether it ran. |
  | Connections, leaf 2 | A read that adds the version and the bindings, and omits settings values. It shows bound non-owned and named-credential names only under `secrets:read`. An update (display name and tags) and a rebind, each at an expected version. The rebind checks `secrets:read` and refuses as it would for an unknown secret. It also refuses any secret under the `connection:{name}:…` convention. Connections consolidates that convention into one definition in Connections `.Core`, which Integrations' OAuth2 grant and PKCE names use. A test at an expected version, checked before the external call, whose result adds a module code. Revoke at an expected version. A delete that checks the version before deleting any owned secret, then removes the row with the repository's version check. |
  | Secrets, leaf 3 | Revoke and delete at the revision, each unavailable without a revision-aware repository. A fan-in (framework §2.6.1): the source contract `ISecretConsumerSource` and the Sequential event `SecretConsumersCollecting` in Secrets `.Core`, with its single aggregating handler in `Elsa.Secrets`. Secrets `.Core` gains a reference to `Elsa.Events.Core`. When no source answers, the result is "not checked", never "no consumers". |
  | Connections, leaf 3 | An `ISecretConsumerSource` implementation in `Elsa.Connections`. Per secret, it returns the binding connections, whether a connection owns the secret by the convention, and the read rule for disclosing connection names (`workflows/design:read` or `connections:manage`). A bounded repository query for the connections that bind a given secret, with a truncation flag, implemented in both the EF and the in-memory repository. Its default interface member reports that the query is unsupported. |

- **FR-007**: **Read authority.** A rule that is exactly one permission MAY be checked through the tool's declared permission. Any other rule MUST be decided by the owning module, never reimplemented in a Weaver package. Another module's names in a review follow that module's declared rule, and show as a count otherwise.
- **FR-008**: Deployment shapes MUST keep working (constitution §E2.2.3). Design-side contributions register only where Design is composed, and Runtime-side contributions only where Runtime is. An absent side is explicitly unavailable.

#### Context and disclosure

- **FR-009**: **Common controls.** Every Context Kind and tool MUST have a negative case, at the cheapest level that proves it (ADR 0080 D1, framework §2.23), for each of these:
  - **Module removed or unloaded:** nothing of the module is offered, and context is re-admitted without it.
  - **Denied, or a cross-tenant target:** nothing is disclosed, not even existence.
  - **Owner-unavailable:** nothing from the owning module is disclosed.
  - **Stale target:** the effect has zero effect, and navigation never retargets it.
  - **Excluded data:** seeded canaries appear nowhere.
  - **Untrusted content:** resource content and client-asserted kinds are marked as untrusted, distinct from module guidance.

  Content that the user explicitly attaches, such as selected console lines, is the user's own disclosure. It is client-asserted and untrusted, and it is excluded from the canary search.
- **FR-010**: Each Context Kind MUST be module-owned, versioned, bounded and authorized. It names the host it describes, and is attributed to one of these:
  - the owning module's revision;
  - the visible query and selection, for a list;
  - the client binding and base revision, for unsaved state.

  Server context providers:
  - admit only their own kinds;
  - produce their permission and catalog kinds from their own reads.

  Other rules:
  - A tool never takes data from another package's kind.
  - A nested picker contributes its own kind from its own module, while a form's own fields belong to the form's module.
  - A nested picker's context never changes a pending Operation Batch's target.
- **FR-011**: Each Context Kind MUST declare its bounds. Each leaf tests its densest pages, nested pickers included, against the per-collection attachment count. It tests each kind against the per-attachment size budget ([#3710](https://github.com/elsa-workflows/elsa-foundation/issues/3710)). A session at its disclosure budget is refused.
- **FR-012**: Each disclosure allowlist MUST be authored by the module that defines the data's meaning, and ship in that module's Agent package. It excludes unlisted fields by default. A canary in an unlisted field that the owning module's own redaction misses proves it.

#### Effects

- **FR-013**: Every effect MUST declare its risk, its privilege classes, its stale check and its receipt.
  - **Risk ordering:** ReadOnly < ReviewRequired < Destructive < Admin. *Destructive* removes or replaces existing state, and *Admin* touches sensitive or administrative state.
  - **Gating:** [ADR 0003](../../docs/adr/0003-weaver-direct-apply-risk-classification-is-elsa-owned.md) keeps classification Elsa-owned. Gating comes from the classes.
  - **Unaffected:** spec 1193's client working-state Destructive batches are unaffected.

  | Effect | Leaf | Risk | Classes | Stale check | Receipt |
  | --- | --- | --- | --- | --- | --- |
  | Secret metadata update | 1 | `Admin` | administrative | revision | readback; persisted |
  | Secret value test | 1 | `Admin` | administrative | version | *performed*, with success or failure and the module code; refused when it could not run |
  | Connection prefill | 2 | `ReviewRequired` | none (`connections:manage`) | client binding | applied locally |
  | Connection update; rebind of a field whose secret the connection does not own | 2 | `Admin` | administrative | version | readback; persisted |
  | Rebind away from an owned inline secret | 2 | `Admin` | administrative, irrecoverable | version | readback; persisted, with a warning if cleanup fails |
  | Connection test | 2 | `Admin` | administrative | version, checked before the external call | persisted with the result and module code; refused when it could not run; *uncertain* when the write fails after the external call |
  | Connection revoke | 2 | `Admin` | administrative, irrecoverable | version | readback; persisted |
  | Connection delete | 2 | `Admin` | administrative, irrecoverable | version | absence readback; persisted; *uncertain* when partial |
  | Secret revoke | 3 | `Admin` | administrative, irrecoverable | revision; consumer answer recomputed | readback; persisted |
  | Secret delete | 3 | `Admin` | administrative, irrecoverable | revision; consumer answer recomputed | absence readback; persisted; *uncertain* when partial |

- **FR-014**: **Common effect outcomes.** Every effect MUST be tested, at the cheapest level that proves it, for each of these outcomes:
  - **Review:** the effect is reviewed in every autonomy mode, including `full-auto`.
  - **Refusal:**
    - The policy is read again at preparation, approval, execution and recovery.
    - An effect whose class is off at a step is refused there, and the proposal ends. Nothing is dispatched at execution.
    - An effect without the owning module's permission is refused before review.
  - **Owner-unavailable at approval:** the owning `.Api` feature is absent, so the effect is owner-unavailable, with zero effect.
  - **Module removed at approval:** the effect is unavailable, with zero effect.
  - **Restart:** in the durable host, a pending approval survives a restart. A recovered approval runs only on an explicit, authenticated execute, which rechecks the revision, the permission and the classes.
  - **Uncertain:** an effect whose response is lost after dispatch, or that may have run before a restart, is *uncertain* and is never replayed.
    - For a server effect, the receipt says what the user should check on the owning page. M4 has no Weaver resolution path for uncertain server effects; this is a recorded limitation (FR-020).
    - For a client effect, the session owner resolves it, as spec 1189 defines.
  - **Local edits:** these carry correlated acknowledgements, and are unavailable, with zero effect, if their page is gone.
- **FR-015**: **Privilege policy** (owner decisions 1, 2 and 4). The Agent module MUST gain an integrator-configured privilege policy. It is a backend host option, never a session setting. It has three classes:
  - **administrative:** every `Admin` effect requires it. A module may add classes, but never remove this one.
  - **irrecoverable:** a module MUST declare this class for its own irrecoverable effects. A third-party irrecoverable effect that is not `Admin` and declares no class is not caught, which is a recorded limitation.
  - **activation:** feature selection apply on the Server host.

  The policy works as follows:
  - **Declaration:** each effect declares a set of classes and needs all of them. The owning module declares the set at preparation, and the set is frozen into the approval digest.
  - **Defaults:** every class is off by default, so an unconfigured host refuses every classed effect. The Workbench enables all three classes in its base configuration, which is this spec's reading of the owner decisions (Assumptions).
  - **Display:** Studio shows the policy read-only, from the bootstrap response.
- **FR-016**: **Outcomes.**
  - ***Performed*:** leaf 0 MUST add *performed* (owner decision 3). It is a new proposal status that reports the action as executed, with a new no-persisted-effect receipt scope. Validation in the executor and in the invocation integrity check accepts that scope only for *performed*. The plan names every server and Studio mapping site.
  - **Module codes:** a tool descriptor and a context-provider descriptor MAY declare a closed set of module codes. Every M4 tool and context provider declares one.
    - Agent core validates each code from a declaring source: in receipts, refusals, preparation errors, authorization violations and context diagnostics.
    - A code outside the set is replaced by a generic code that keeps the original's classification. An owner-unavailable code stays owner-unavailable, and is never turned into forbidden.
    - Each `Elsa.Agent.<Domain>` package maps its owning module's native codes into its set. Mapping resolves collisions, such as the Secrets manager's two "not found" codes.
    - Mapped codes are dotted ASCII identifiers outside `agent.`.
    - A receipt carries its code in a new member, which the plan names.
    - Sources that declare no set pass their codes through unchanged.
  - **Existing outcomes:** every other outcome reuses the existing outcomes and codes. A new code is added only where none fits, such as the privilege-policy refusal. A status alone is never evidence of an effect.

#### Contracts, catalogs and records

- **FR-017**: Replacement contracts MUST detect a second registration (framework §2.6.2), and the Agent-wide gap remains #3633. Visibility follows framework §2.23.3 and ADR 0080 D3.
- **FR-018**: **SemVer** under framework §4.2. Pre-GA Majors follow ADR 0080 D4 and Elsa constitution §E5.
  - **Major:**
    - *performed*, which changes the model semantics that owner decision 3 amends: the new proposal status, the new receipt scope, and the new validation.
    - The privilege policy: with every class off by default, it refuses `Admin` tools, including third-party tools, that ran before.
    - Freezing the class set into the approval digest. A server effect pending approval across the upgrade is refused as a binding mismatch, and an approved client invocation that is waiting becomes *uncertain* (story 1, scenario 12).
  - **Durable records:** the Agent EF store keeps proposals inside its serialized session payload, so the new fields need no relational schema change.
    - Leaf 0 bumps the store's schema-version stamp, so an older reader refuses newer rows rather than dropping fields ([ADR 0077](../../docs/adr/0077-a-module-upgrades-in-place-only-when-its-persisted-schema-is-unchanged.md)).
    - Existing rows read with default values: no module code, and an empty class set.
    - Mixed-version clusters across leaf 0 are unsupported while the Agent API is in preview.
  - **Minor:**
    - opt-in module-code sets and the receipt member;
    - new members on existing stores and contracts, added so that existing implementations keep compiling;
    - Secrets `.Core`'s reference to `Elsa.Events.Core`;
    - new contracts, events, packages, Context Kinds, tools, renderers, codes and permissions.
- **FR-019**: **Catalogs** MUST change with the code (framework §2.22.1):
  - **Leaf 0:**
    - `Extensions/Agent/src/Elsa.Agent/EXTENSION_POINTS.md`: the privilege policy, class sets, module-code sets, the principal seam and *performed*;
    - `Extensions/Agent/src/Persistence/EntityFrameworkCore/EXTENSION_POINTS.md`: the payload fields and the schema-version stamp.
  - **Leaf 1:**
    - updates the Secrets Core, `Elsa.Secrets` and Secrets EF catalogs;
    - creates `Essentials/src/Secrets/Api/EXTENSION_POINTS.md`.
  - **Leaf 2:**
    - updates the Connections Api and Connections Core catalogs;
    - creates the Connections Api README.
  - **Leaf 3:**
    - the Secrets Core catalog gains the event and the source contract, with Connections' implementation as a cross-domain Known implementation;
    - the `Elsa.Secrets` catalog gains the handler;
    - the `Elsa.Connections` catalog gains the implementation, and leaf 3 creates that package's README with its cross-domain note;
    - the Connections EF catalog gains the bounded query.
  - **Leaf 5:** `Studio/src/essentials/Elsa.Studio.Api/EXTENSION_POINTS.md` gains the SDK read seams.
  - **Every leaf:**
    - the Agent catalog lists its contributors as cross-domain Known implementations;
    - each new package that exposes extension points gets its own catalog;
    - the root index is updated (framework §2.22.2);
    - each package README lists its cross-domain contributions.
- **FR-020**: **Records.** Evidence goes on each PR, on the leaf's sub-issue, on #3443 and on #3428. #3428 records M4's rows from FR-001 ([program goal](../../docs/program-goals/weaver-across-studio.md)). Leaf 0 MUST file the following follow-ups as `needs-triage`:
  - field-level sensitivity for connection settings (X);
  - ExtensionBuilder workspace source inspection (X);
  - console record reads (X);
  - a server-verifiable path for browser-only writes (V);
  - a durable, cluster-wide Workflow Runtime Diagnostics Setting (D);
  - log time range and retention, and OpenTelemetry metrics and logs views (S);
  - working-state adapters for ETL creation, task forms, BPMN processes and activity contract proposals (W);
  - irrecoverable-class coverage for third-party effects that declare no class;
  - detection of secret consumers in workflows and Nuplane feed credentials;
  - a Weaver resolution path for uncertain server effects (FR-014).

  The Program's own follow-ups are triaged under the 29 October delegation. Each follow-up is recorded on #3428, and one in a parked program's area is filed without a parent. Nothing that already exists is filed: context-budget progress goes on #3710, and Full payloads stay with Studio ADR 0009's open decision.

  Leaf 0 also records these on #3428:
  - every narrowing;
  - the deferrals to other programs (#3604, and #3519's authoring);
  - the decisions open to owner review.
- **FR-021**: **Glossary.** Leaf 0 MUST add these entries to the [Elsa glossary](../../docs/glossary/elsa.md), each linked from Studio/CONTEXT.md:
  - the Weaver outcome words: *proposed*, *approved*, *persisted*, *applied locally*, *refused*, *uncertain* and *performed*;
  - *Privilege Class*;
  - *Module code*;
  - *Secret consumer*.

  In Studio/CONTEXT.md, leaf 0 MUST:
  - replace *Module Load Status*'s state list ("discovered, accepted, activated, disabled, or rejected") with the reported states: available, loaded, disabled, incompatible, failed and unknown. Its _Avoid_ line stays.
  - widen *Privileged Tool* to mean a Weaver Tool whose effect needs a privilege class.
  - add *Built-in Studio Screen*: a screen the `Elsa.Studio.Hosting` client renders itself.
  - add *Client-Asserted Kind*: a Context Kind whose content only the browser supplies. It is untrusted, confers no authority, and the server checks only its schema and bounds.

#### Evidence

- **FR-022**: Each story MUST have end-to-end evidence. Restart cases run in their leaf's durable host, except story 1's, which run in the probe host's restart configuration. Fixture cases run in the probe host. Every tool and effect MUST have a conformance-probe case on both provider adapters: Copilot's delegated loop and Anthropic's host-driven loop. Negative cases run at the cheapest level that proves them.
- **FR-023**: The canary search MUST cover HTTP bodies, SSE frames, adapter payloads, tool results, module codes, review cards and audit records.
- **FR-024**: Evidence MUST keep three categories distinct:
  - deterministic component tests;
  - deterministic provider-protocol evidence on the actual adapters, with packaged consumers;
  - opt-in live-provider evidence (none is claimed).

  Probe evidence is frozen at the exact reviewed head, and failures, skips and quarantines are recorded as they occurred.

#### Later leaves

- **FR-025**: Each later leaf MUST get its own spec and its own sub-issue of #3443, both created when that leaf starts. The spec's number is the highest number ever used, counting open PRs, plus one. The sub-issue carries the leaf's acceptance. Each leaf spec is bound by FR-002 to FR-024 and by its FR-001 rows.
  - **Grouping:** each owning module gets its own leaf. Rows share a leaf only where they share a page or a journey. A module's rows may span several leaves for size.
  - **New rows:** leaf 0 assigns every row its refresh adds, and #3443 cannot close while a row is unassigned.

  | Leaf | FR-001 rows | Scope |
  | --- | --- | --- |
  | 4 | Structured logs, traces | allowlisted reads, grouped for log-to-trace correlation |
  | 5 | Diagnostics, appearance/themes, settings sections | Studio Hosting's screens, with Hosting.Themes; creates `Elsa.Studio.Weaver.Hosting`; the *Theme Selection Preference* widening |
  | 6 | Console | console context and attached lines |
  | 7 | Home | widget snapshots, personal layout and snooze, grouped by the shared Home page |
  | 8 | Runtime evidence | setting change |
  | 9 | Activity availability, activity definitions | Activities Design |
  | 10 | Features | draft and Server-host apply |
  | 11 | Modules/feeds | administration reads |
  | 12 | Backends | profile reads |
  | 13 | ETL definitions, connectors, runs | reads |
  | 14 | ETL definitions, connectors, runs | draft update, run cancellation |
  | 15 | User tasks | reads, claim, release, reassign; reassign escalated if it stays `Admin` |
  | 16 | Agents | reads |
  | 17 | BPMN | reads |
  | 18 | Expressions | with #3711 |
  | 19 | Weather | client-asserted page context, through a small package |
  | 20 | ExtensionBuilder | installed-module guidance |

## Success Criteria

### Measurable Outcomes

- **SC-001**: Every FR-001 row has linked evidence on #3428, and every narrowing has its grounds. No row is unassigned.
- **SC-002**: Stories 1–4 pass their acceptance scenarios in the hosts FR-022 names. An unconfigured host refuses every classed effect.
- **SC-003**: Seeded canaries appear in none of the channels FR-023 lists. Content the user explicitly attaches is excluded from the search.
- **SC-004**: For each contributing package, a composition test that deselects it removes exactly its contributions, and leaves every other row unchanged.
- **SC-005**: Each leaf PR's review evidence includes a diff check of its changed projects against FR-005's list. The check shows that Weaver core, the shell's collector and review shell, the provider adapters and other modules changed only through FR-005. The owning-module changes that FR-006 requires are exempt, including Integrations' OAuth2 use of the Connections convention.
- **SC-006**: After leaf 0, the independent package witness is re-run and passes. The program goal requires later lifecycle changes to requalify.
- **SC-007**: The native-page regression tests (FR-006) and the attachment tests (FR-011) pass.
- **SC-008**: Each leaf PR carries the PR evidence that [merging](../../docs/contributing/merging.md) requires, keeps the Elsa constitution §E7 test-tier manifest correct for each new test project, passes locked-mode restore, and keeps the solution filters current.

## Assumptions and Boundaries

- **Delivery:**
  - **Sub-issues:** when this spec merges, leaves 0–3 each get a sub-issue of #3443. Each leaf PR says "Fixes #<leaf issue>, Refs #3443", and the closing leaf also fixes #3443. Each PR maps the #3443 acceptance items it advances.
  - **Order:** leaves 1–3 depend on leaf 0, and each depends on the one before it.
  - **Status:** the PR that merges this spec sets it to *Approved*. Leaf 0's PR sets *In progress*, and the closing leaf's PR sets *Implemented*.
  - **Bookkeeping:** every leaf comments on #3428 and #3443 at each state transition.
- **Program owner decisions, 2026-11-03** (Sipke Schoorstra; [receipt](https://github.com/elsa-workflows/elsa-foundation/issues/3428#issuecomment-6612627295)). The receipt says "spec 1202", which means this spec after it was renumbered ([note](https://github.com/elsa-workflows/elsa-foundation/issues/3443#issuecomment-6614698826)):
  1. **Irrecoverable deletes and revokes** of secrets and Service connections: "Let the integrator decide what privileges weaver has via configuration."
  2. **Feature selection apply:** "Let the integrator decide via configuration." This widens #3443's activation boundary ("a manage claim alone cannot expand it", 31 October), but only for hosts that enable the class.
  3. **A generic outcome for an approved action that persists nothing:** "Add it in leaf 0."
  4. **The privilege default:** "Off; Workbench always on."
- **Decisions taken under Program authority, open to owner review:**
  - **Workbench classes:** the Workbench enables all three classes in its base configuration. This is this spec's reading of owner decisions 1, 2 and 4, which the receipt records as how they apply.
  - **Agent packages:** one per owning module.
  - **Class sets:** declared by consequence, satisfied in full, and frozen into the approval digest. Every `Admin` effect requires the administrative class.
  - **Secret revoke and delete:**
    - Consumers come from Connections, including ownership.
    - An owned secret is refused, which is stricter than the native page.
    - The effect fails closed when no source answers, so hosts without Connections cannot revoke or delete secrets through Weaver.
    - The consumer answer is recomputed at approval and at execution.
  - **Module codes:** declared, closed sets, each mapped by its Agent package.
  - **Console:** server reads of records are narrowed (X).
  - **Uncertain server effects:** M4 has no Weaver resolution path for them.
  - **Spec 1193's refusals:** they stand (O).
  - **Expressions:** delivered with #3711.
  - **Post-M0 targets:** user tasks get read, claim, release and reassign, with reassign flagged. Agents, BPMN and backends are read-only.
  - **Runtime capture:** its semantics are untouched, and Full payloads wait for Studio ADR 0009.
  - **Browser-only writes:** these are narrowings (V).
  - **Home layout:** personal layout changes are delivered, and the default layout stays administration (T).
  - **Run cancellation:** ETL cancellation is reviewed, while BPMN run operations are outside the target (T).
- **Plan decisions:**
  - **P1:** each package's shell feature.
  - **P2:** the contract signatures and internal type names, the principal seam, the revision mechanisms, and the placement of permission constants.
  - **P3:** every non-atomic compare-then-write window, and how it is bounded.
- **Overlap**, reconciled without reparenting:
  - ETL: #2894, #2906, and the ETL defects #3709, #3719, #3721, #3724 and #3562.
  - Home dashboards: #3604.
  - Parked: the Diagnostics Observability Readiness and Runtime Execution Evidence goals.
  - User Tasks: #3688.
  - Studio Expression Developer Experience: #2310, #3711 and #2349. No children are added under #2310.
  - Elsa 3.8/3.9 Parity: #2771, which owns Elsa 3 import.
  - The [BPMN Engine program](../../docs/program-goals/bpmn-engine.md).
  - Agents: #3519. Runtime agents: #2892.
  - Weaver: #3710, #3720 and #3761.
  - Feed credentials: #3735.
- **Out of scope:** releases, deployments, provider purchases, live-provider credentials, blanket administration, arbitrary workspace edits, package promotion and package activation. Feature selection apply is in scope only through the activation class. Integrated qualification remains #3444.

## Issue Traceability

| #3433 criterion | Requirements and stories |
| --- | --- |
| Home/dashboard, ETL, Connections, Secrets metadata, Diagnostics, runtime evidence, activity availability and installed settings/administration surfaces have module-owned support | FR-001 to FR-004, FR-025; stories 2–4 |
| Each supported action has context, tool, renderer/effect and negative-case evidence; absent mutation support is explicit | FR-001, FR-004, FR-013, FR-014, FR-022 |
| Secret values and unrelated data are not exposed; runtime/model semantics and existing domain ownership remain intact | FR-002, FR-006, FR-007, FR-009, FR-012; Assumptions; owner decision 3 for *performed* |
| Scope derived from the installed/composed inventory, including nested surfaces and module removal | FR-001, FR-008, FR-010; story 1; SC-004 |

| #3443 acceptance or task | Requirements and stories |
| --- | --- |
| Each M0 row becomes an owning-module entry with acceptance and negative-case evidence | FR-001, FR-025; stories 1–4; SC-001, SC-002 |
| Cover the named surfaces, including nested contexts | FR-001, FR-010, FR-025; stories 2–4 |
| Same generic contribution path; module knowledge, renderers and guidance in the owning module | FR-002 to FR-008; SC-004 to SC-006 |
| Read where mutation is unavailable; review and apply where allowed; secret values and unrelated data excluded | FR-012 to FR-016; stories 2–4; SC-003 |
| Normal-host composition, removed modules, authority, stale targets; no claim for absent modules | FR-001, FR-008, FR-009, FR-014, FR-022 to FR-024; story 1; SC-002, SC-004 |
| Per-module leaves; reconcile overlap without reparenting | FR-025; Delivery; Overlap; FR-020 |
| Boundaries | FR-001, FR-015; owner decisions |
| Tasks: refresh, implementation and evidence, gates and reconciliation | FR-001, FR-006, FR-020, FR-022 to FR-025; SC-008; Delivery |
