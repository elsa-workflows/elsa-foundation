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
not a claim that the page was reachable there. The adoption column records the implementation disposition, including intentional
retention of specialized surfaces; it does not claim every surface was visited live. Aliases share their canonical page; dialogs and panels belong to their owning family.

| Module / surfaces | Audit evidence | Intentional differences / migration scope | Adopted |
| --- | --- | --- | --- |
| Workflows: definitions and folder navigation | Live + source | Collection; retain lifecycle, folders, selection, continuation and stale-row safeguards | [x] |
| Workflows: runs and run details | Live list + source detail | Collection + runtime workspace; preserve advanced draft filters and pinned executable evidence | [x] |
| Workflows: executables and inspector | Live list + source inspector | Collection + immutable detail; preserve artifact identity and inspection actions | [x] |
| Workflows: activity definitions and editors | Live list + source editor | Collection + authoring workspace; retain provider ownership, version/cursor and graph behavior | [x] |
| Workflows: templates | Live + source | Catalog cards are appropriate; normalize frame, search and actions | [x] |
| Workflows: availability and runtime evidence settings | Live + source | Settings sections, policy precedence and apply behavior | [x] |
| Workflows: Elsa 3 import and upgrade workbench | Source | Ordered review steps and durable receipts remain visible | [x] |
| Secrets: list, dialogs and editors | Live list + source dialogs | Preserve masking, scope, loaded/server counts and destructive confirmations | [x] |
| Connections: list, type picker, details | Live list + source detail | Preserve readiness, OAuth/test/revoke flows; no secrets revealed | [x] |
| ETL: file and connector definitions, drafts, versions, runs | Live unavailable state + source | Split workbench, validation/publication and paged runs | [x] |
| Hosting: appearance, modules, feeds, theme builder | Live + source | Settings navigation; split inspectors and theme preview remain purposeful | [x] |
| Feature management | Live + source | Review-before-apply, selection and host-specific sections | [x] |
| Dashboard, Workflows.Dashboard, Attention | Source | Widgets use dashboard composition, not collection chrome | [x] |
| Diagnostics landing, ConsoleStream, StructuredLogs, OpenTelemetry | Live + source | Dense streams/trace detail and docked variants; page header only on full pages | [x] |
| ExtensionBuilder | Source; not in active host navigation | Generation workbench; use common page and feedback patterns | [x] |
| Samples.WeatherForecast | Source; not in active host navigation | Public module example; semantic table and supported public imports | [x] |
| JavaScript/Liquid expression editors, CodeEditor | Source | Embedded editors retain syntax-specific layout; reuse field/control tokens | [x] |
| Weaver host UI and workflow contributions | Source | Conversational/assistant workspace and designer contributions; retain integration contracts | [x] |

## Evidence limits

The live ETL routes expose unavailable-API states, so populated ETL layouts require fixtures.
Disabled modules and embedded contribution variants need source and fixture qualification. Existing
runtime data was only read; no credentials, secrets or personal data belong in this audit.

## Delivery evidence

Implementation: [PR #3512](https://github.com/elsa-workflows/elsa-foundation/pull/3512).
The [module-author guide](../../Studio/docs/module-ui.md) and standalone React/vanilla examples
consume the public package. No new program is needed: this work is a bounded delivery under
Elsa 4 Simplification, with #3511 as its durable record.

The implementation removes duplicate page/table recipes and copied example component CSS,
exports the actual shared styles, and retains domain behavior. Browser contracts cover common
collection typography, selection, control height and overflow at 320px/1440px, light/dark and
flat/Linen appearance. Public-only fixtures cover a disabled sample's failure/retry/empty states
and exact identifier copying without layout shifts. Existing resource, authoring, ETL and
settings behavior suites provide the corresponding interaction checks.

Prepared npm tarballs are installed into a standalone copy of the React host outside the workspace;
its production build and strict public-declaration check qualify package boundaries. Local
frontend build, unit tests, typecheck/lint, architecture guards and generated maps also pass.
The PR records exact revisions, commands, counts, failures, skips and review outcomes, including
the full browser regression and final CI/integration gates. Those delivery gates are not inferred
from the migration checkmarks above.
