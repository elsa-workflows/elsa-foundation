# Feature Specification: Reusable Studio Theme Store

**Feature Branch**: `agent/codex/3360-studio-theme-store`
**Created**: 2026-10-26
**Status**: Draft
**Input**: Program #3340 task #3360: extract the existing optional theme capability so custom Studio hosts can reference packages instead of copying Web classes. Include the accepted #3380 follow-ups recorded on #3360.

## User Scenarios & Testing

### User Story 1 - Compose optional themes in a custom Studio (Priority: P1)

A host author references the reusable theme package, chooses the core, picker and management features independently, and uses the standard host authorization seam.

**Independent Test**: A host using public composition APIs serves only the enabled theme routes; a base host has none. Authentication rejects unauthenticated theme requests when enabled.

**Acceptance Scenarios**:
1. Given core alone, its store/capability/asset reads work while picker and management routes remain absent.
2. Given picker or management, core is available through the existing feature dependency; the other optional surface stays absent.
3. Given an authenticated host, the installed host policy governs the theme routes.
4. Given no theme package, base hosting has no theme registration or dependency.

### User Story 2 - Keep directory sources and managed persistence distinct (Priority: P1)

A theme administrator uses configured directory themes alongside managed themes without unrelated writes adopting directory definitions.

**Independent Test**: Load a directory definition, perform visibility/default/asset writes, remove the source definition and prove it does not reappear from the managed store. Existing deliberate or historical managed records survive.

**Acceptance Scenarios**:
1. Directory definitions win by ID in reads, while a shadowed managed record retains its original bytes and becomes visible again after directory removal.
2. Explicit save/import/duplicate operations update only their intended managed definitions; unrelated definitions are not adopted or overwritten.
3. Removing a directory-only definition removes it from inventory after the source is reread. A route that cannot persist the requested change reports that fact.
4. Concurrent mutations preserve complete managed state and produce valid atomic JSON.

### User Story 3 - Preserve configured storage and appearance behavior (Priority: P2)

A host author configures source, writable store and asset locations independently, while administrators retain existing appearance validation and import/export behavior.

**Independent Test**: Run with a content root distinct from the current directory and nondefault storage paths, save/import/export a theme and upload/read/delete an asset, and exercise the browser editor with an empty projected default.

**Acceptance Scenarios**:
1. Existing JSON theme collections and managed stores round-trip, preserving modes, palette, material, typography, shape and layout.
2. Source modes, reserved built-in IDs, publication/enabled/default/visibility rules and asset limits remain coherent across server and browser.
3. Invalid source modes and unusable path choices fail clearly at startup; later mutations of retained callback objects do not redirect installed storage.
4. After deletion with an empty projected default, the editor selects an available inventory entry or displays a coherent empty state.

### Edge Cases

Missing directory sources; single-file versus directory collections; overlapping managed/directory IDs; retired built-ins; empty DirectoryOnly inventory; invalid appearance values; cancelled writes/uploads; invalid asset IDs; concurrent visibility/default/theme writes; shell reload; configured default overriding stored default.

## Requirements

- **FR-001** Ship optional `Elsa.Studio.Hosting.Themes` with the three existing feature IDs and unchanged route/JSON formats. It must not reference Web, Nuplane, backend identity or the complete in-box module set.
- **FR-002** Provide the canonical public options/composition seam, retain existing Themes keys, and expose separate writable store and asset paths resolved against ContentRoot. Installed choices require restart.
- **FR-003** Preserve independently selected features and host authorization. Register feature discovery through the existing CShells seam; do not build a new feature system.
- **FR-004** Separate complete managed persistence from directory-enriched and source-policy-projected reads. Never infer that legacy managed records are accidental copies and purge them.
- **FR-005** Preserve directory precedence, built-in reservation, enabled/published/default/visibility constraints and import/export wire compatibility; make unpersistable source-owned changes explicit.
- **FR-006** Coordinate every read/modify/write cycle with the existing host mutation gate and write atomically. Cancellation cleans uncommitted temporary files.
- **FR-007** Preserve token/font/radius/layout and asset size/type validation and qualify server/browser parity.
- **FR-008** Adopt the package in Web and remove the moved implementations and now-obsolete host writer when its remaining consumers permit. No production friend is introduced.
- **FR-009** Keep browser management usable with an empty projected default or inventory; distinguish inventory from selectable themes.
- **FR-010** Prove public-host behavior, affected whole-project and browser checks, candidate package closure, reviewed delivery and resulting-main verification before closing the task.

### Key Entities

Managed theme state; directory collection; merged management inventory; selectable appearance view; installed theme options; material asset. Meanings and UI tokens remain in the existing Studio design-token/Theme Store documentation.

## Success Criteria

- **SC-001** A custom public-API host supports all three feature combinations without copied production classes or a Web/Nuplane dependency.
- **SC-002** Removing a directory definition after an unrelated write cannot resurrect it from newly copied managed data; shadowed and legacy managed data remain intact.
- **SC-003** Nondefault store/source/asset locations work with existing JSON and appearance constraints; unauthorized requests and invalid inputs fail before mutation.
- **SC-004** Editor deletion/empty inventory journeys complete coherently. Reviewed checks and actual resulting-main/package evidence cover every acceptance criterion.

## Assumptions and Scope

This is the already authorized #3360 leaf under Program #3340. #3359 remains stopped and unchanged. Storage is host-owned local presentation state, following the existing Theme Store ownership ADR. No distributed store, theme backend, new built-in theme, release/deploy/publisher activation or unrelated program is introduced. Final isolated consumer combinations remain #3364; this leaf proves its focused public package/host integration.
