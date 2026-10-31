# Building a module UI

Use `@elsa-workflows/studio-ui` for page composition and controls. The package has
no router, query client, SDK provider or host-service requirement. Keep your data,
permissions, navigation and mutations in your module. The visual rules are in the
[UX design spec](ux/design-spec.md); the theme contract is [design tokens](design-tokens.md).

## Imports and styles

Declare `@elsa-workflows/studio-ui` and React as dependencies following the existing
[module development setup](../README.md). Installed modules resolve the public UI
package through the host import map. The Studio host loads its styles once; modules
must not copy host CSS or import a sibling module's private stylesheet.

A standalone host loads the public stylesheet once, before its theme overrides:

```tsx
import "@elsa-workflows/studio-ui/styles.css";
import "./theme.css";
```

`styles.css` includes token defaults, component styles and disclosure styles. Use
`tokens.css` alone only when building your own controls. The published files include
a light-theme fallback; setting a dark attribute alone does not provide a complete
dark palette. Supply your dark theme variables after the defaults, as the
[React host example](../examples/react-host/README.md) does. Code editor and workflow
consumers additionally load those packages' public `style.css` exports.

## Module stylesheet loading

Use the shared `defineModuleConfig` for installed module builds. It emits an ESM
`module.js`, content-hashed CSS and `entry-styles.json` (version 1 with an ordered
`styles` array). The host loads only the entry's static CSS closure; Vite loads
route and renderer CSS with their deferred chunks. Keep a renderer's stylesheet
import at its rendering boundary to avoid downloading it on unrelated list pages.
The build guard requires application-mode ESM output and `cssCodeSplit` so deferred
CSS is loaded automatically. Preserve the generated contract and all emitted
chunks/styles in the module's static web assets.

An explicit empty `styles` array is valid. Invalid contracts disable that module
with a diagnostic. Modules built before this contract retain the host's numbered
`module.css`, `module2.css`, … fallback. New builds also retain these aliases for
older hosts, which continue loading all styles eagerly. Content-hashed styles use
their exact URLs so Vite can reuse entry styles when opening a deferred route.

## A collection page

This example searches an already loaded collection. A server-paged module should
retain its server search, permissions, continuation tokens and truthful counts.
The primitives deliberately do not invent those semantics.

```tsx
import { useState } from "react";
import {
  SkeletonRows, StudioButton, StudioCollectionFooter, StudioCollectionToolbar,
  StudioPage, StudioPageHeader, StudioResultState, StudioSearchInput,
  StudioTableContainer
} from "@elsa-workflows/studio-ui";

type Service = { id: string; name: string; endpoint: string };
type Props = {
  services: Service[];
  loading: boolean;
  error?: string;
  canCreate: boolean;
  onCreate(): void;
  onRefresh(): void;
};

export function ServicesPage(props: Props) {
  const [search, setSearch] = useState("");
  const visible = props.services.filter(service =>
    service.name.toLowerCase().includes(search.trim().toLowerCase()));

  return <StudioPage aria-labelledby="services-title">
    <StudioPageHeader title="Services" titleId="services-title"
      description="Manage configured service connections."
      actions={<>
        {props.canCreate && <StudioButton variant="primary" onClick={props.onCreate}>Create service</StudioButton>}
        <StudioButton onClick={props.onRefresh} disabled={props.loading}>Refresh</StudioButton>
      </>} />
    <StudioCollectionToolbar>
      <StudioSearchInput aria-label="Search loaded services"
        placeholder="Search loaded services" value={search}
        onChange={event => setSearch(event.currentTarget.value)} />
    </StudioCollectionToolbar>
    {props.loading ? <SkeletonRows label="Loading services" /> : props.error ?
      <StudioResultState tone="danger" title="Could not load services"
        description={props.error} actions={<StudioButton onClick={props.onRefresh}>Retry</StudioButton>} /> :
      visible.length === 0 ? <StudioResultState
        title={search ? "No matching services" : "No services yet"}
        description={search ? "Change your search to see other services." : "Create a service to get started."} /> :
      <StudioTableContainer role="region" aria-label="Services table" tabIndex={0}>
        <table className="studio-table" aria-label="Services">
          <thead><tr><th scope="col">Name</th><th scope="col">Endpoint</th></tr></thead>
          <tbody>{visible.map(service => <tr key={service.id}>
            <td>{service.name}</td><td>{service.endpoint}</td>
          </tr>)}</tbody>
        </table>
      </StudioTableContainer>}
    {!props.loading && !props.error && <StudioCollectionFooter>
      {visible.length} of {props.services.length} loaded services
    </StudioCollectionFooter>}
  </StudioPage>;
}
```

The host owns the main landmark and outer content gutter. `StudioPage` is a section,
not a second main landmark or a whole-page card. Use a page title, short description
and at most one filled primary header action. `headingLevel={2}` is available when
the embedding surface already supplies an h1; use `headingLevel={3}` on its
`StudioResultState` to keep feedback below that title. Icons and eyebrows are optional;
they should add information rather than repeat navigation.

`StudioCollectionToolbar` wraps controls and retains native Tab order. Put search
first, followed by domain filters. Label every control. For an exclusive filter, use
`studio-filter-group` on a named `role="group"` container with native buttons and
`aria-pressed` for the current choice. A native table plus
`StudioTableContainer` and `studio-table` is the smallest collection recipe. Give
scrolling regions an accessible name and keyboard focus. `StudioDataGrid` provides
selectable rows; existing ARIA tables can use `studio-collection-table` for the same
visual recipe while retaining their column geometry and interaction code.

## States and behavior

- Use `SkeletonRows` for initial loading, `StudioResultState` for an empty collection,
  no matches or initial request failure, and `StudioAlert` above retained results for
  refresh failures. Do not present stale rows as fresh or actionable if their scope changed.
- Keep denied access, unsupported capability, empty data and failed requests distinct.
  Clear identities and counts when access is lost. Never render a secret value in a list.
- Use `StudioCollectionFooter` for counts and pagination. Say loaded/shown/total
  accurately; preserve opaque cursor semantics and disable unavailable navigation.
- Use `StudioButton`, `StudioField`, `CopyableIdentifier`, status chips and the
  disclosure kit. Use the injected `api.dialogs` for host confirmation and focus management.
  A shared visual primitive does not grant permissions or implement a mutation.
- Keep embedded editors, diagnostic streams, dashboards and graph workspaces shaped
  for their task. Use the same typography, controls, semantic colors and feedback.
  A compact page header is available through `studio-page-header--compact` for docked panels.

## Verify a new module

Render the module independently as well as inside the host. Check light and dark
themes, material themes when supported, 320px width, keyboard-only operation,
visible focus, loading/empty/failure/denied states and long labels or identifiers.
Horizontal overflow belongs inside the table or canvas, never on the page.
Validate actual interactions and rendered styles; checking class names alone does
not establish consistency. The Weather sample uses this public recipe without a
module stylesheet, and the standalone React example uses published package imports.
