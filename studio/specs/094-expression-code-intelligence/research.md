# Research: Expression Code Intelligence

## Decision 1: Use CodeMirror 6 behind the existing Studio editor contract

**Decision**: Continue with the existing `Elsa.Studio.CodeEditor` abstraction and use CodeMirror 6 for compact and expanded profiles.

**Rationale**: The repository already lazy-loads CodeMirror for JavaScript. CodeMirror supports small embedded editors, configurable keymaps, completion/lint extensions, a first-party Liquid language package, touch browsers, and persistent `EditorState`. The engine remains replaceable because no CodeMirror type enters the public Studio SDK.

**Alternatives considered**:

- Monaco: excellent JavaScript service but substantially heavier, explicitly lacks mobile support, and is awkward for many tiny inspector fields.
- Plain inputs plus highlighted overlays: lightweight but fragile for selection, IME, completion, diagnostics, accessibility, and shared undo.
- Separate engines for compact and expanded: rejected because it breaks session continuity and parity.

## Decision 2: Keep one workflow-lifetime editor session per Expression Document

**Decision**: Key memory-only sessions by backend, tenant/user scope, draft ID, activity/node ID, property reference key, and Expression Type. Store CodeMirror editor state, current diagnostic/tooling versions, and surface-independent presentation state.

**Rationale**: The current URI uses only the property name and collides across activities. A shared session preserves selection and undo when compact and expanded surfaces unmount/remount.

**Alternatives considered**:

- Keep the expanded editor mounted offscreen: wastes memory and risks hidden focus/a11y content.
- Persist selection/undo in the workflow draft: editor state is transient and must not become workflow data.
- Separate sessions per surface: contradicts the continuity requirement.

## Decision 3: Focus activates compact rich editing

**Decision**: Unfocused compact fields render a lightweight, syntax-colored preview; focusing a single-line value mounts the compact rich editor. Multiline values render a one-line preview and an accessible expand control. A newline pasted/inserted in compact mode is preserved and opens expanded editing.

**Rationale**: This meets the latency and density goals without sacrificing source fidelity.

**Alternatives considered**:

- Mount every visible editor: rejected for memory/startup cost.
- Strip or reject newlines: rejected because source is durable user data.

## Decision 4: Split language-neutral context from language projection

**Decision**: Workflows supplies a versioned `ExpressionAuthoringContext` containing design-time names, shapes, and scope relations. JavaScript and Liquid adapters project that context and the module catalog into language-appropriate completion/hover/signature items.

**Rationale**: Workflow scope is not owned by any expression language, while spelling, insertion text, tags, filters, and member rules are language-specific.

**Alternatives considered**:

- A universal list of “Elsa globals”: rejected because symbols and access syntax differ per Expression Type.
- Each language re-derives workflow scope: duplicates complex scope rules and risks disagreement.

## Decision 5: Foundation is authoritative; Studio uses additive capability links

**Decision**: Consume the optional Foundation capability `expressions.tooling.v1`, whose five no-store relations provide context, bounded symbols, source-aware completions, hover, and validation. Missing links, forbidden responses, incompatible versions, stale revisions, and cancellation map to distinct states.

**Rationale**: Studio's existing capability resolver rejects unknown capability majors. Additive links preserve old clients while a separate tooling contract version permits independent negotiation. Runtime modules remain authoritative.

**Alternatives considered**:

- Increase the Expressions capability major: would make the entire expression API incompatible with current Studio.
- Add a Studio bridge that invents runtime symbols: violates backend/module authority.
- Embed static catalogs in the UI only: cannot represent installed modules, policy, permissions, or runtime semantics.

## Decision 6: Cache reusable catalogs, not field context

**Decision**: Reuse only bounded, permission-scoped symbol pages in memory, keyed by backend, user, tenant, Expression Type, context revision, query, and page. Source-aware completion, hover, and validation results are never cached. Cancel stale searches and semantic requests.

**Rationale**: Built-in module metadata is shared by many fields; workflow scope is field-specific and changes with the draft.

**Alternatives considered**:

- Fetch a full catalog per field: unnecessary latency and load.
- Persist catalogs across browser sessions: risks stale permissions and cross-session disclosure.

## Decision 7: Local syntax feedback and authoritative semantic feedback are separate

**Decision**: CodeMirror language parsers provide immediate local syntax diagnostics. Studio debounces and cancels backend semantic validation and discards results whose document, source, context, or catalog versions no longer match.

**Rationale**: Responsive parsing and runtime-accurate semantics have different latency/authority characteristics.

**Alternatives considered**:

- Backend-only diagnostics: poor typing experience and network-sensitive.
- Client-only diagnostics: cannot guarantee runtime agreement or gate publication.
- Initial full TypeScript language service: too heavy for the required JavaScript scope.

## Decision 8: Consequential gates are backend-owned

**Decision**: Studio displays full-draft results and warnings, but Foundation rejects known-invalid Test Runs/publications. Test Run may proceed with a warning only when authoritative validation is unavailable and no known syntax/semantic error exists; publication fails closed.

**Rationale**: UI-only gates are bypassable and cannot protect API callers.

**Alternatives considered**:

- Disable buttons from client diagnostics only: insufficient authority.
- Block draft save: conflicts with iterative authoring and current draft behavior.

## Decision 9: Formatting is explicit and optional

**Decision**: No formatting on typing, blur, expand/collapse, or autosave. Expose a format action only when the Expression Type advertises it.

**Rationale**: Exact source preservation is more important than normalization, especially across type switching.

## Decision 10: Documentation and telemetry are treated as sensitive boundaries

**Decision**: Render module documentation as sanitized Markdown with HTML and executable links disabled. Telemetry records only aggregate usage, latency, availability, and non-sensitive error codes.

**Rationale**: Catalog documentation is untrusted, and expression/source-derived identifiers can contain secrets or business data.
