# Feature Specification: Keep Studio Header Actions Reachable on Narrow Screens

**Feature Branch**: `agent/codex/3165-narrow-header-actions`

**Created**: 2026-10-21

**Status**: Implemented

**Input**: Issue #3165, child of #3057 and program #2559: correct the narrow Studio header so actions remain reachable without moving the module page horizontally.

## User Scenarios & Testing

### User Story 1 - Use the Studio header on a narrow viewport (Priority: P2)

As a Studio user on a narrow viewport, I can reach the header actions and use them without the module page shifting sideways.

**Why this priority**: Clipped actions and status information make core shell controls difficult to find or use.

**Independent Test**: In a normal composed Studio host at 320px and 360px, focus and activate the visible appearance, account, and Weaver controls. Confirm every retained direct control fits, the relevant horizontal scroll widths do not exceed their client widths, and the module page and outer frame retain their original horizontal positions. Root recorded current-main account-menu automation observations at 320px (frame scroll 0→106px; content x=16→−90px) and 360px (0→79px; x=16→−63px). Those measurements came from locator-driven activation and may include Playwright's scroll-into-view behavior; they are not proof that a physical pointer click caused the movement. The fixture independently confirms that the full account target is clipped and that a physical click on the visible portion does not scroll. Final native-pointer/keyboard candidate proof is linked in the plan. Compare before and after screenshots at the same source, host, state, and viewport.

**Acceptance Scenarios**:

1. **Given** Studio at 320px or 360px with left, top, or adaptive navigation, **when** the header renders with its available direct actions, **then** each retained direct action and hit target is fully visible and no horizontal action scroller is needed.
2. **Given** the same narrow layout, **when** a user focuses or activates appearance, account, or Weaver, **then** the frame does not scroll horizontally and the module page does not move left.
3. **Given** the existing theme selector, **when** a user opens it with pointer or keyboard, **then** all supported color modes, themes, and navigation preferences remain available, the selected state is correct, Escape closes the menu, and focus returns to its trigger.
4. **Given** the header source links, **when** the layout reflows, **then** the GitHub and Designer links remain native anchors with their existing destinations and keyboard order follows the visible order.
5. **Given** Studio at the 640px/641px breakpoint edge or desktop width, **when** the header renders, **then** the selected responsive behavior is stable and the desktop header, quick controls, and sidebar account presentation remain polished and operable.
6. **Given** authenticated users with a long display name, anonymous users, and Weaver with or without session status, **when** the narrow header renders, **then** no required action is clipped and account identity and session status remain understandable.

## Edge Cases

- Long account names, active Weaver session badges, and their combined width at 320px and 360px.
- A resize across 640px/641px while keyboard focus is on a header action.
- The theme menu opened near a viewport edge at 320px or 360px.
- Left, top, and adaptive navigation preferences, authenticated and anonymous sessions, and settled Light, Dark, and supported High contrast modes.

## Requirements

### Functional Requirements

- **FR-001**: Studio MUST keep every retained direct header control and its full hit target inside the viewport at 320px and 360px.
- **FR-002**: Focusing or activating appearance, account, or Weaver MUST NOT cause horizontal scrolling of the frame or move the module page from its original horizontal position.
- **FR-003**: Studio MUST preserve access to all currently supported color modes, theme choices, and navigation preferences.
- **FR-004**: Studio MUST preserve the native GitHub and Designer source anchors, account behavior, Weaver toggle and session status, and existing navigation preferences.
- **FR-005**: Header keyboard order, selected states, Escape dismissal, and focus restoration MUST remain coherent with the rendered layout.
- **FR-006**: The responsive correction MUST preserve usable desktop presentation and behave stably at the selected breakpoint and its adjacent width.

## Success Criteria

### Measurable Outcomes

- **SC-001**: At 320px and 360px, every retained direct action has a bounding box fully within the viewport in the required account and Weaver states.
- **SC-002**: At those widths, the document, main frame, and topbar actions have no unintended horizontal overflow; focusing and activating the tested controls leaves horizontal scroll offsets and the module page's left coordinate unchanged.
- **SC-003**: All existing mode, theme, navigation, account, source-link, and Weaver interactions remain usable by pointer and keyboard in focused regression coverage.
- **SC-004**: The 640px/641px and 1024px/1025px breakpoint edges and a 1280px or 1440px desktop reference remain visually and functionally acceptable.

## Assumptions

- The correction remains within the existing Studio shell and uses existing controls and appearance menus.
- The existing account, authentication, Weaver, theme, and navigation behavior is the compatibility baseline.
- Current-main normal-host reproduction and visual comparison of candidate layouts precede implementation. The prior Light-control 79px scroll receipt is bound to source `0ddc6aba`, not current main. Clicking Light at 360px did not shift the current-main page. Current-main `a710adf26ad16ff9fe7fbbd1db5818c604aee45d` has the same header CSS as the measured `4ca6025872966914f15a34bf795a317f0e112081` baseline, and root's layout comparison remains applicable. However, the locator-driven 79px/106px account activation measurements may include Playwright scroll-into-view; final native interaction proof is linked in the plan; it does not establish baseline native-pointer causality.
- The parent qualification finding #3075 remains staged until this correction and its other prerequisites qualify.
