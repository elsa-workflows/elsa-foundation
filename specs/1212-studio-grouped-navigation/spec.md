# Studio grouped navigation and theme spacing

**Status**: Implemented — effective on merge of PR #3866; exact-head required checks remain the merge gate.
**Issue**: #3863 · Program #2559 · Owner approved Open groups and theme polish, 2026-11-05.

## User outcome

People can predict where to find authoring, operations, integrations and configuration without opening a catch-all menu. The [owner-approved mockup](design/open-groups-reference.png) defines the hierarchy and restrained styling; existing Studio controls and theme tokens define implementation.

## Requirements

1. Home comes first. Initially open, independently collapsible Build, Operate and Integrations groups follow; Settings stays separate at the bottom. Remember explicit disclosure choices; reveal active/search-matching destinations. Hide groups with no accessible destinations. Preserve icon rail, top navigation, keyboard operation and backend-qualified workspace links.
2. Build contains Workflows, Agents and Library. Operate contains Runs, User tasks and Diagnostics. Integrations contains Connections and Secrets. Existing additional authoring/operational modules declare an appropriate named group. New top-level contributions without placement do not silently enter a catch-all.
3. Library presents Activity definitions and Templates as local views. Workflows presents Definitions and Executables as local views. Diagnostics presents its existing overview/widgets and Console, Structured logs and OpenTelemetry as local views. Preserve the diagnostics drawer. Modules continue owning their actual routes/components and backend requirements.
4. Activity availability moves to Settings / Workflow authoring; Runtime evidence settings moves to Settings / Runtime. Link from Library and Diagnostics respectively. Preserve direct bookmarks and access checks, including runtime-only/design-only compositions and denied administration.
5. All existing themes adopt generous, consistent shell/page/navigation spacing, quiet headings, simpler icon treatment and restrained structural borders while retaining palettes, typography, layouts, high-contrast meaning and the independent navigation/appearance preferences. No new theme preset or duplicated domain screen.
6. Remove obsolete More tools behavior, storage, tests and guidance. Update module navigation guidance and the affected regression journeys.

## Acceptance and exclusions

Use the approved Open groups preview and existing Connections/Workflow Definitions layouts as references. Cover normal, empty/unavailable, no-match, denied/read-only, restored disclosure, active deep-link, search, rail/top, keyboard/focus, light/dark/high contrast and narrow/coarse-pointer states. Cover moved collection list/detail transitions and retained workspace tabs without changing domain data/lifecycles. Reuse collection validation/recovery suites; no new forms or backend schema changes are part of this work.

Current-commit composed hosts, inspected downloadable screenshots/traces, affected whole suites and independent correctness/standards/UX acceptance are required before reporting delivery acceptance. A passing fixture is supporting evidence only.
