# Elsa Studio UI

The shared UI layer defines the Modular Admin Workbench language for Studio
screens. Host pages can consume these React primitives directly. Independently
bundled modules should depend on the documented token aliases and stable SDK
contracts until the component export boundary is finalized.

Core patterns:

- Resource pages use compact toolbars, rows, grids, status chips, and inspectors.
- Configuration pages use split inspector or grouped settings layouts.
- Diagnostics pages use dense rows, severity chips, timelines, and bottom panels.
- Typography and colors come from `tokens.css`; module CSS should use the
  `--studio-*` variables or compatibility aliases rather than one-off values. The
  versioned token contract module authors depend on — every stable token, the
  `--studio-material-*` surface-role ladder, and the
  rules (no raw literals, no cross-module private namespaces, no `[data-theme]`
  selectors in module CSS, material treatments gated on `[data-theme-material]`) — is
  documented in [`docs/design-tokens.md`](../../../../../../../docs/design-tokens.md) and
  enforced by the `elsa/no-raw-color-literal` stylelint rule (`pnpm lint:css`).

Visual rules:

- Shell/page titles stay compact; no hero-scale admin headings.
- Resource lists are row-first. Avoid stacked large cards for scan-heavy data.
- Resource workbenches use the available content width; avoid fixed desktop
  max-width caps that leave unused space beside lists or inspectors.
- Use true white or cool neutral surfaces, hairline borders, minimal shadow, and
  5-8px radii for admin chrome.
- Selection uses the shared blue/cyan accent; statuses use the shared semantic
  green, amber, and red tokens.
- Summary metrics are compact strips, not oversized dashboard cards.

## Host-side worked examples

The names below describe the current host-side source patterns. They are not a promise that independently bundled modules can import these components: keep those modules on the documented token aliases and stable SDK contracts until the export boundary is finalized.

A field keeps its label, optional help, and control together; use a supported shared button variant for the primary action:

```tsx
<StudioField label="Base URL" description="Address of the service" meta={<span>Required</span>}>
  <input name="baseUrl" type="url" required />
</StudioField>
<StudioButton variant="primary" size="md" type="submit">Create service</StudioButton>
```

For a configured optional section, start it open, show the set count, and allow the author to collapse it. Pass only the host's actual error/force-open state; the disclosure contract owns how those states remain visible.

```tsx
<AdvancedSection
  label="More options"
  setCount={configuredOptionalCount}
  defaultOpen={configuredAtOpen}
  allowCollapseWhenSet
  hasError={optionalHasBlockingError}
  forceOpen={optionalSourceFailed}
>
  {optionalFields}
</AdvancedSection>
```

`StudioDataGrid` activates a row on click and on Enter/Space. It does not stop events from nested controls. A caller embedding a copy button must stop both click and activation-key propagation:

```tsx
<button
  type="button"
  onClick={event => { event.stopPropagation(); copy(item.id); }}
  onKeyDown={event => {
    if (event.key === "Enter" || event.key === " ") event.stopPropagation();
  }}
>
  Copy ID
</button>
```

For a module-owned row instead of `StudioDataGrid`, guard the row handler against events whose target is inside an interactive descendant, and activate from the keyboard only when the row itself is the target. Workflow Definitions currently owns that guard in its custom rows; do not assume the shared primitive supplies it. See the [normative UX rules](../../../../../../../docs/ux/design-spec.md#components) for the supported Button variants and disclosure behavior.
