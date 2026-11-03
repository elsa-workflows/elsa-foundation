# Research: Studio definition list consistency

## Decision: reuse proven Studio presentation surfaces

Use the public `CopyableIdentifier`, disclosure, button, and overflow-menu controls already exported by Studio UI. Reuse `WorkflowsPageFrame` only for consumers already inside its established scope; align Activity Definitions' custom collection header where its domain controls fit. Preserve the existing semantic table roles.

**Rationale**: Workflow Definitions, Runs, and Activity Definitions already have distinct domain behavior and partially shared presentation. Existing primitives cover the identified interaction needs without adding a public API.

**Alternatives considered**: Replace all three collections with a new generic grid/list framework, or widen `WorkflowsPageFrame` into a universal list shell. Both would merge domain contracts and add an abstraction not demonstrated by the current consumers.

## Decision: progressive identity disclosure with isolated row actions

Keep the human-readable name and primary status/context in the default row. Put secondary technical identifiers behind an accessible disclosure and use `CopyableIdentifier` for exact keyboard-copyable values. Interactive disclosure and copy controls must not activate the containing Activity row.

**Rationale**: Runs currently displays several identifiers in a dense row, while operator scanability and exact identity remain requirements. The existing copy control preserves the full identifier for assistive technology and copies the untruncated value.

**Alternatives considered**: Hide identifiers in hover titles, truncate them without a copy path, or add another clipboard component. These do not satisfy keyboard access and exact-value needs.

## Decision: restore Activity collection state per browser history entry

Associate each Activity Definition collection origin with its own search/provider/authority/page-size and opaque-cursor context. Restore that context through editor Back and browser Back. Keep server cursors opaque and out of newly invented URL fields; use existing restart/recovery behavior when the cursor or authorization context becomes invalid.

**Rationale**: The collection currently owns these values locally and unmounts when the workbench opens. A single mutable last-view snapshot cannot distinguish multiple history origins.

**Alternatives considered**: Keep one module-level “last collection” snapshot, put opaque cursors into new query parameters, or rewrite the accepted workflow-definition browse session. These either lose entry identity, expose an internal continuation token, or change the unrelated #3068 contract.

## Decision: validate the resulting composition in the normal host

Fix the existing Activity history-return behavior first, then add exact identity/nested-control behavior, then finish visual alignment. Retain the existing Workflows browse tests and inspect the normal source-built Workbench with the Studio Web shell using authorized backing data. Route-mocked browser fixtures are supplemental only.

**Rationale**: Unit and fixture tests can prove local transitions but cannot establish that authorization, server paging, assets, themes, and host composition behave together.

**Alternatives considered**: Treat fixture-only screenshots or source presence as host acceptance. Neither exercises the real composition or backing APIs.

## Source grounding and open verification

The canonical repository graph was ready, but its scoped searches returned no Studio TSX symbols. Exact source fallback confirmed the relevant behavior in `ActivityDefinitionsPage.tsx`, `WorkflowDefinitions.tsx`, `WorkflowInstances.tsx`, `pages.tsx`, and the existing Workflows client tests. The issue's four pre-claim refinements and current claim/scope note were read. These are source-level findings, not runtime test results. No implementation or runtime test has been performed in this planning slice; the implementation worker must record the actual baseline and focused test results at the qualified base.
