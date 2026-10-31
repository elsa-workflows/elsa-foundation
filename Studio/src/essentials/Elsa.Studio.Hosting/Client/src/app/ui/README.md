# Elsa Studio UI

The shared UI layer defines the Modular Admin Workbench language for Studio
screens. Host pages and independently bundled modules can consume the host-agnostic
primitives through `@elsa-workflows/studio-ui`. Use the public package exports and
injected SDK contracts; the host's `DialogHost` stays private. See
[package consumption](../../../../../../../README.md#consuming-studio-packages-in-your-own-app)
for styles, peer dependencies, and host examples.

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
- Use the active theme’s surfaces, hairline borders, minimal shadow, and shared
  radius tokens for admin chrome.
- Selection uses the active theme’s accent; statuses use the shared semantic
  green, amber, and red tokens.
- Summary metrics are compact strips, not oversized dashboard cards.

## Worked examples

The examples use current public primitives. Import them from `@elsa-workflows/studio-ui`; use the injected `ElsaStudioModuleApi` for host services. A component being exported does not make every internal host component part of that public surface.

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

`StudioDataGrid` makes rows selectable when `onSelect` is supplied; those rows activate on click and on Enter/Space. Without `onSelect`, it renders a read-only table without row activation. It does not stop events from nested controls. A caller embedding a copy button must stop both click and activation-key propagation:

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

For a module-owned row instead of `StudioDataGrid`, guard the row handler against events whose target is inside an interactive descendant, and activate from the keyboard only when the row itself is the target. Workflow Definitions currently applies the descendant click guard in its custom rows and provides keyboard entry through the workflow-name link; its rows do not implement row-level keyboard activation. Do not assume the shared primitive supplies caller guards. See the [normative UX rules](../../../../../../../docs/ux/design-spec.md#components) for the supported Button variants and disclosure behavior.

### Compose a module page

Use `StudioPage`, `StudioPageHeader`, `StudioCollectionToolbar`, `StudioSearchInput`,
`StudioTableContainer` (native tables), `StudioDataGrid` (selectable rows),
`StudioResultState`, and `StudioCollectionFooter` for page structure. The host owns
outer gutters and the main landmark; modules own data, authorization, counts and
cursor semantics. These primitives have no SDK, routing, query, or dialog provider dependency.

The [module UI guide](../../../../../../../docs/module-ui.md) contains the public
imports, a complete collection example, state/accessibility requirements, and the
standalone stylesheet setup. The [React host example](../../../../../../../examples/react-host/README.md)
exercises the same published styles without copying internal CSS.

Use the injected `api.dialogs.confirm` for confirmation; it resolves `true` for
confirmation and `false` for cancel or dismissal. Only confirmation calls the
mutation. Keep the action disabled while the dialog or mutation is pending.
Modules must not import or mount `DialogHost`: the normal host mounts it once and
owns the modal focus lifecycle.
