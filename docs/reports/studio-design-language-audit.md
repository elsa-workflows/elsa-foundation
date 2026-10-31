# Studio design-language audit

Audit started 2026-10-31 from source `ab46bc22a52aa6e456bca6ac0e803c80904de081` and the running
local Studio at `https://localhost:7030`. The running process's exact build was not established;
live observations are product evidence, not proof of the source revision. Tracking: [#3511](https://github.com/elsa-workflows/elsa-foundation/issues/3511).
Implementation and checks: [spec 1175](../../specs/1175-studio-design-language/tasks.md).

## Findings

The application has semantic theme tokens and several shared controls, but page and collection
composition is mostly module-local. The normative UX guidance describes a common hierarchy that
the implementation does not consistently enforce. Adding another theme cannot fix that divergence.

| Pattern | Observed inconsistency | Resolution |
| --- | --- | --- |
| Page frame | Secrets fills a bordered card; Connections uses a different whole-page card; workflows are flat; ETL adds another inset border | Common flat page surface and spacing; reserve cards for meaningful sections |
| Header | h1/h2, icons, eyebrows, sizes, padding, descriptions and count placement vary; Templates has an oversized title | Shared page header; optional meaningful context, consistent title/description/action placement |
| Actions | Create/New emphasis differs; Refresh appears in header, filter row or as an icon | One primary page action; common secondary controls and row actions |
| Search/filter | Definitions searches on the right; Activity definitions on the left; Secrets has a grid; Connections has a capped form | Wrapping toolbar with search first, adjacent filters; preserve live vs submitted query semantics |
| Tables | Secrets and Executables uppercase shaded headers; Connections and definitions sentence case; row padding and borders differ | One token-based table/ARIA-table recipe, consistent headers and density |
| Counts/paging | Counts in headers, body labels or footers with different wording and placement | Shared footer; retain truthful loaded/shown/total and opaque cursor semantics |
| Feedback | Raw paragraphs, large centered illustrations, bordered alerts and dashed boxes for equivalent states | Shared result states and alerts with explicit severity and recovery action |
| Reuse | Public React primitives depend on host CSS; external host examples copy visual rules | Ship the actual component stylesheet through the public package |

## Coverage and migration inventory

`Live` means route/visible controls inspected in the running host. `Source` means source-reviewed,
not a claim that the page was reachable there. Implementation/verification remains unchecked until
recorded below. Aliases share their canonical page; dialogs and panels belong to their owning family.

| Module / surfaces | Audit evidence | Intentional differences / migration scope | Adopted |
| --- | --- | --- | --- |
| Workflows: definitions and folder navigation | Live + source | Collection; retain lifecycle, folders, selection, continuation and stale-row safeguards | [ ] |
| Workflows: runs and run details | Live list + source detail | Collection + runtime workspace; preserve advanced draft filters and pinned executable evidence | [ ] |
| Workflows: executables and inspector | Live list + source inspector | Collection + immutable detail; preserve artifact identity and inspection actions | [ ] |
| Workflows: activity definitions and editors | Live list + source editor | Collection + authoring workspace; retain provider ownership, version/cursor and graph behavior | [ ] |
| Workflows: templates | Live + source | Catalog cards are appropriate; normalize frame, search and actions | [ ] |
| Workflows: availability and runtime evidence settings | Live + source | Settings sections, policy precedence and apply behavior | [ ] |
| Workflows: Elsa 3 import and upgrade workbench | Source | Ordered review steps and durable receipts remain visible | [ ] |
| Secrets: list, dialogs and editors | Live list + source dialogs | Preserve masking, scope, loaded/server counts and destructive confirmations | [ ] |
| Connections: list, type picker, details | Live list + source detail | Preserve readiness, OAuth/test/revoke flows; no secrets revealed | [ ] |
| ETL: file and connector definitions, drafts, versions, runs | Live unavailable state + source | Split workbench, validation/publication and paged runs | [ ] |
| Hosting: appearance, modules, feeds, theme builder | Live + source | Settings navigation; split inspectors and theme preview remain purposeful | [ ] |
| Feature management | Live + source | Review-before-apply, selection and host-specific sections | [ ] |
| Dashboard, Workflows.Dashboard, Attention | Source | Widgets use dashboard composition, not collection chrome | [ ] |
| Diagnostics landing, ConsoleStream, StructuredLogs, OpenTelemetry | Live + source | Dense streams/trace detail and docked variants; page header only on full pages | [ ] |
| ExtensionBuilder | Source; not in active host navigation | Generation workbench; use common page and feedback patterns | [ ] |
| Samples.WeatherForecast | Source; not in active host navigation | Public module example; semantic table and supported public imports | [ ] |
| JavaScript/Liquid expression editors, CodeEditor | Source | Embedded editors retain syntax-specific layout; reuse field/control tokens | [ ] |
| Weaver host UI and workflow contributions | Source | Conversational/assistant workspace and designer contributions; retain integration contracts | [ ] |

## Evidence limits

The live ETL routes expose unavailable-API states, so populated ETL layouts require fixtures.
Disabled modules and embedded contribution variants need source and fixture qualification. Existing
runtime data was only read; no credentials, secrets or personal data belong in this audit.

## Delivery evidence

Pending implementation. Record exact commands, pass/fail/skip counts and reviewed revisions on
the issue and PR; this inventory must not be marked complete merely because a header changed.
