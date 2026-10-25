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
- Use true white or cool neutral surfaces, hairline borders, minimal shadow, and
  5-8px radii for admin chrome.
- Selection uses the shared blue/cyan accent; statuses use the shared semantic
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

### Compose a page, list, and confirmation dialog

This example filters an already loaded collection. For a paged inventory, use the module's server search and distinguish shown, loaded, and total counts. The data hook and mutation remain module-owned; the example does not define a service endpoint.

```tsx
import { useState } from "react";
import {
  EmptyState,
  SkeletonRows,
  StudioAlert,
  StudioButton,
  StudioDataGrid,
  StudioField,
  StudioSearchInput,
  StudioToolbar,
  type StudioDataGridColumn
} from "@elsa-workflows/studio-ui";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";

type Service = { id: string; name: string; endpoint: string };
type Props = {
  api: ElsaStudioModuleApi;
  services: Service[];
  loading: boolean;
  loadError?: string;
  onCreate(): void;
  onRetry(): void;
  onDelete(id: string): Promise<void>;
};

const columns: StudioDataGridColumn<Service>[] = [
  { id: "name", header: "Name", render: service => service.name },
  { id: "endpoint", header: "Endpoint", render: service => service.endpoint }
];

export function ServicesPage({ api, services, loading, loadError, onCreate, onRetry, onDelete }: Props) {
  const [query, setQuery] = useState("");
  const [selectedKey, setSelectedKey] = useState<string>();
  const [mutationError, setMutationError] = useState<string>();
  const [busy, setBusy] = useState(false);
  const needle = query.trim().toLowerCase();
  const visible = services.filter(service => service.name.toLowerCase().includes(needle));
  const selected = visible.find(service => service.id === selectedKey);

  async function deleteSelected() {
    if (!selected || busy || loading || loadError) return;
    setBusy(true);
    setMutationError(undefined);
    try {
      const confirmed = await api.dialogs.confirm({
        title: "Delete service",
        message: `Remove ${selected.name}?`,
        confirmLabel: "Delete service",
        tone: "danger"
      });
      if (!confirmed) return;
      await onDelete(selected.id);
      setSelectedKey(undefined);
      setMutationError(undefined);
    } catch {
      setMutationError("Could not delete the service. Try again.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <section>
      <header>
        <div>
          <h2>Services</h2>
          <p>Manage configured service connections.</p>
        </div>
        <StudioButton variant="primary" onClick={onCreate}>Create service</StudioButton>
      </header>
      <StudioToolbar>
        <StudioField label="Search loaded services">
          <StudioSearchInput value={query} onChange={event => setQuery(event.currentTarget.value)} />
        </StudioField>
        <StudioButton disabled={!selected || busy || loading || Boolean(loadError)} onClick={() => void deleteSelected()}>
          Delete selected
        </StudioButton>
      </StudioToolbar>

      {mutationError ? <StudioAlert tone="danger">{mutationError}</StudioAlert> : null}
      {loading ? (
        <SkeletonRows label="Loading services" />
      ) : loadError ? (
        <>
          <StudioAlert tone="danger">{loadError}</StudioAlert>
          <StudioButton onClick={onRetry}>Retry</StudioButton>
        </>
      ) : visible.length === 0 ? (
        <EmptyState>{needle ? "No services match this search." : "No services yet."}</EmptyState>
      ) : (
        <StudioDataGrid
          columns={columns}
          items={visible}
          getKey={service => service.id}
          selectedKey={selected?.id}
          onSelect={service => setSelectedKey(service.id)}
        />
      )}
    </section>
  );
}
```

The injected `api.dialogs.confirm` resolves `true` for confirmation and `false` for cancel or dismissal. Only confirmation calls the mutation, and the button stays disabled while the dialog or mutation is pending. Modules must not import or mount `DialogHost`: the normal host mounts it once and owns the modal focus lifecycle. Use the existing host's dialog service instead of implementing another global dialog controller.
