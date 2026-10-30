# Feature Specification: Linen Studio and host branding

**Feature Branch**: `agent/codex/3496-linen-studio`
**Created**: 2026-10-31
**Status**: Draft
**Issue**: [#3496](https://github.com/elsa-workflows/elsa-foundation/issues/3496)
**Program**: [Elsa 4 Simplification](../../docs/program-goals/elsa-4-simplification.md)
**Input**: Deliver the owner-selected Linen design end to end, including existing themes, modular branding and image-led customizable login pages, after the owner approved the visual prototypes.

## User Scenarios & Testing

### User Story 1 - Work in Linen (Priority: P1)
An author opens Studio and gets the selected warm, rounded, restrained Linen presentation across the shell, shared controls and real workflow designers.
**Why this priority**: This is the selected product direction.
**Independent Test**: Open a real workflow, select/edit an activity, save and reload it, and navigate settings and lists.
**Acceptance Scenarios**:
1. Given a fresh profile, when Studio opens, then Linen with a contrasting dark left navigation and warm neutral work surfaces is the default.
2. Given a workflow, when its designer and inspector render, then their geometry, typography and surfaces follow Linen while editing, keyboard interaction, selection and persistence still work.

### User Story 2 - Keep personal and organizational appearance (Priority: P1)
Users independently choose palette, appearance mode, navigation and workspace arrangement; integrator themes remain usable.
**Why this priority**: Modular presentation must not flatten theme identities or remove valid arrangements.
**Independent Test**: Change each preference, reload, and exercise all built-in palettes with Light, Dark, Dim and High contrast.
**Acceptance Scenarios**:
1. Given an existing saved preference, when upgrading, then its palette, mode and explicit navigation/arrangement remain valid.
2. Given Linen, when changing palette, then geometry remains rounded and sans-serif, with readable navigation, selection, focus and semantic states.
3. Given High contrast or reduced motion, when Studio renders, then decorative gradients/motion do not obscure readable text and focus.

### User Story 3 - Brand the complete entry and workspace (Priority: P1)
An integrator configures a product name, logos, links, palette defaults and login imagery/layout, or replaces login presentation, without changing auth behavior or editing product source.
**Why this priority**: The owner explicitly approved branded imagery and requested a modular white-label experience.
**Independent Test**: Run a branded host configuration and inspect its sign-in flow and authenticated shell; replace the credential presentation through its public extension seam.
**Acceptance Scenarios**:
1. Given host branding, when opening Studio and its actual login page, then the configured name/logo/visuals appear consistently.
2. Given a built-in login layout, when viewed at desktop or narrow width with an error, then imagery, labels, validation and submit controls remain accessible and usable.
3. Given a custom login renderer, when authenticating, then existing credential validation, antiforgery and local return-url rules remain in force.

### User Story 4 - Integrate a branded module host (Priority: P2)
Integrators follow documentation and a complete sample to supply branding, responsive image assets, palette defaults and replacement presentation; modules consume shared design roles.
**Why this priority**: Customization must be discoverable and maintainable.
**Independent Test**: Build/run the documented sample using the public options and replacement seam.
**Acceptance Scenarios**:
1. Given the guide, when configuring a branded host, then no product-source fork is required.
2. Given a custom module, when it uses the documented shared roles, then it adapts to Linen and supported appearances.

### Edge Cases
- Missing/failed logo or decorative image leaves a usable branded sign-in form.
- Long product names and narrow screens do not hide navigation or form controls.
- Existing custom theme IDs may arrive asynchronously and must not be reset.
- Anonymous deployments still boot without a login; external identity pages remain provider-owned.
- Untrusted configuration is encoded at HTML/script boundaries; unsafe asset/link schemes are rejected.

## Requirements

### Functional Requirements
- **FR-001**: Studio MUST use Linen as its default workspace presentation, with sans-serif type, rounded controls/cards, restrained copper accent and subtle strategic gradients.
- **FR-002**: Studio MUST preserve independent persisted workspace, palette, appearance and navigation choices; existing built-in and integrator palettes MUST remain usable.
- **FR-003**: Shared design roles MUST carry the presentation into modules without module-specific raw palette literals.
- **FR-004**: Login MUST support bundled image-led presentations and host-supplied imagery, crop/focal position and branding.
- **FR-005**: The actual credential page MUST expose replaceable presentation independently of its auth handlers/providers, preserving validation, antiforgery and safe returns.
- **FR-006**: Hosts MUST configure Studio branding and sensible default preferences without overriding explicit user choices.
- **FR-007**: Integrator docs and a working branded-host example MUST explain assets, login replacement, shared roles, appearance and accessibility.
- **FR-008**: The change MUST preserve read-only/source-owned behavior, permissions, workflow state, undo/redo, logical graph handles, keyboard navigation and responsive access.

### Key Entities
- Workspace presentation: geometry/type/surface composition, independently selected from palette.
- Brand: host-owned names, logos, links and login artwork/presentation defaults.
- Palette: existing color-role definitions and appearance variants, including integrator themes.

## Success Criteria

### Measurable Outcomes
- **SC-001**: Fresh-profile and persisted-preference checks pass for all four appearance modes and all built-in palettes.
- **SC-002**: An author can edit/save/reload a workflow and use the inspector under Linen without losing state or functionality.
- **SC-003**: Login controls remain usable at 320px, 768px and desktop widths, with visible keyboard focus and readable error states.
- **SC-004**: A branded-host example builds and demonstrates configuration and replacement using documented public entry points.
- **SC-005**: Affected suites, browser checks, architecture/maps and exact-head review/CI evidence support delivery.

## Assumptions
- The owner-approved Linen prototype is the visual reference; discarded concepts are not new production choices.
- Existing authentication lifetimes and token handling are outside scope.
- Host configuration/assets are trusted deployment input, but still encoded/validated at rendering boundaries.
- Bundled approved login images may ship as optimized local assets; remote assets are optional integrator choices.
- Existing workspace arrangements remain configurable; Linen is the standard presentation and fresh-profile default.
