# Contract: Studio UI Components

## Module-facing rules

- Modules consume shared UI through exported components, stable SDK contracts, or
  documented CSS variables.
- Modules MUST NOT target private host selectors such as `.topbar`, `.content`,
  or internal implementation classes to achieve native styling.
- Shared primitives MUST expose states through props or data attributes rather
  than requiring module-local class name replication.

## Required primitive families

### Shell and navigation

- `StudioShell`
- `StudioSidebar`
- `StudioTopCommandBar`
- `StudioNavItem`
- `StudioBackendStatus`

### Page and command surfaces

- `StudioPage`
- `StudioPageHeader`
- `StudioToolbar`
- `StudioSearchField`
- `StudioSegmentedControl`
- `StudioActionGroup`

### Data and resource surfaces

- `StudioSummaryStrip`
- `StudioDataGrid`
- `StudioResourceList`
- `StudioPagination`
- `StudioStatusChip`

### Detail and inspection

- `StudioInspector`
- `StudioDrawer`
- `StudioTabs`
- `StudioMetadataList`
- `StudioStickyActionFooter`

### Forms and settings

- `StudioSettingGroup`
- `StudioField`
- `StudioSwitch`
- `StudioSelect`
- `StudioTextInput`
- `StudioSecretInput`
- `StudioNumberInput`
- `StudioTextArea`
- `StudioJsonEditorShell`
- `StudioValidationMessage`

### Feedback and diagnostics

- `StudioAlert`
- `StudioEmptyState`
- `StudioLoadingState`
- `StudioErrorState`
- `StudioBottomPanel`
- `StudioLogRow`
- `StudioTimeline`

## Component state requirements

Every shared primitive documents whether it supports:

- Loading
- Disabled
- Focused
- Selected
- Active
- Dirty
- Empty
- Error
- Warning
- Success
- Read-only
- Collapsed/expanded

## Accessibility requirements

- Controls have accessible names.
- Focus order follows visual order.
- Keyboard operation is documented for non-native interactions.
- Focus indicators are visible in light and dark themes.
- Validation messages are associated with their fields.
- Drawers, popovers, dialogs, and menus manage focus predictably.

## Visual requirements

- Typography comes from Studio tokens.
- Colors and statuses come from Studio tokens.
- Radius, border, spacing, shadow, and focus rings come from Studio tokens.
- Page titles stay compact; no hero-scale headings in admin surfaces.
- Resource pages avoid card-heavy layouts unless cards are repeated widgets.
