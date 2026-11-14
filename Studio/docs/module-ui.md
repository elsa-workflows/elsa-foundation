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

The public stylesheet includes the opt-in Minimal treatment. A standalone host can set
`data-workspace-style="minimal"` on `<html>` and supply its complete palette. Public controls,
collections and status chips then share the same density and fine dividers as Studio. Module-owned
tables should consume the [collection and action roles](design-tokens.md#collection-and-action-roles)
instead of overriding row borders, selection fills or primary actions with literal recipes.
Keep owner-specific graph geometry, runtime state and permissions in the module; a presentation
change must preserve those behaviors. Do not select behavior using palette IDs.

## Outlined form fields

Scalar form editors use `StudioField`. Its caption stays on the top border in every theme;
help and validation stay below the control and out of its accessible name. Keep native value,
change, blur, ref, required and disabled properties on the control:

```tsx
<StudioField label="Endpoint" description="Use an absolute URL" error={error}>
  <input value={endpoint} onChange={event => setEndpoint(event.target.value)} disabled={saving} />
</StudioField>
```

A single native child is associated automatically, including one inside a native wrapper.
Caller IDs and existing descriptions are preserved. Checkboxes/radios, search controls, table
cells, file pickers and rich editors retain their own semantic presentation. Multiple individually
labelled controls form a real group; do not wrap several controls in a single HTML label.

Use `StudioFormField` for an explicit native-control contract or adornments:

```tsx
<StudioFormField label="Credential" trailing={<button type="button" onClick={retry}>Retry</button>}>
  {attributes => <input {...attributes} value={credential} onChange={updateCredential} />}
</StudioFormField>
```

Inline errors are described by the focused control. Use `errorRole="alert"` when the editor owns the error announcement; omit it when a form summary already announces the same error.

The shell does not disable adjacent actions. Set their disabled state explicitly. For opaque
contributed editors, pass `controlId` to `StudioField` and forward that ID plus
`data-studio-field-control=""` to the actual native control. The shared component does not clone
inside arbitrary React components. Setting, property and expression editor contributions may opt into
`presentsField: true` when they own the shared field and descriptor caption; omitted values retain
legacy host captions. Opt in only when every inline state retains that field and caption. Keep
editor metadata, validation reporting and domain behaviour intact.

When a host supplies actions around an opaque scalar editor that owns one shared field,
`StudioFieldAdornmentProvider trailing={actions}` places those actions in the field's existing
adornment area. This keeps them beside the value when captions wrap or validation appears.
Editor-owned trailing actions are retained; the supplied actions do not propagate into nested
fields or popovers. Collection editors and contributors without field ownership retain their
existing host action placement.
Registered host editors also import their field from `@elsa-workflows/studio-ui`, externalized
through the host import map, so host and module editors share the same action context at runtime.

Do not override field borders with module card styles. The Outlined width and gap roles are
`--studio-outlined-field-width` and `--studio-outlined-field-gap`; colour, radius and typography
continue to use semantic theme tokens. A real legend gap supports transparent surfaces without
painting an opaque patch behind the label.

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
older hosts, which continue loading all styles eagerly. The generated manifest explicitly sets `stylesHaveContentHashes` so the loader keeps
those exact URLs and Vite can reuse entry styles when opening a deferred route.
Legacy and imperative custom manifests omit that optional flag and retain module-version
cache busting regardless of their stylesheet names. Only opt in when every listed
stylesheet URL contains an immutable content hash; names alone do not establish that guarantee.

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

Collection layouts should follow Connections and Workflow Definitions: keep the header,
filter toolbar, bordered results and footer separated by the shared page gap. Size the
results to their content rather than stretching a short table to the viewport height.
Use `--studio-control-height` for toolbar disclosures so their summaries align with
buttons, search and selects. Empty collections use `StudioResultState` inside the
results surface, with the create action repeated when permitted; filtered emptiness
instead offers a way to clear the filters. Keep creation in a dialog or a separate
view so the collection and creation form do not compete on the same screen. After
creating a connection, close creation and refresh the collection; open details only
when the user requests them.
Keep resource detail and edit views separate from the collection, following Workflow
Definitions, Templates and Agents; do not render them side by side with the list.

Settings should explain policy differences when they affect the user's selection.
Avoid duplicate summaries of an already visible selection, reserve warning colors
for actionable problems, and let effective-value labels wrap instead of truncating
them while space is available. Add help only where the label and nearby explanation
leave a meaningful question unanswered.

Decide per feedback surface whether it becomes a notification:

| Existing feedback | Becomes |
|---|---|
| Result of a user action (moved, deleted, restored, saved, favorited) | `notify({ level: "success" })` |
| Failed action, validation or field error | stays inline with retry/recovery context |
| Operation progress or a detailed execution/import/publication receipt | stays near the operation; toast a brief completion only when useful |
| Reversible action with existing reversal logic | toast with an `action` |
| Load failure, stale data, required restart or other ongoing condition | stays inline until resolved |
| Autosave or local editing state | quiet inline indicator |
| Loading placeholders (`Suspense` fallbacks) | stays inline |

Publish transient confirmations through the injected `api.notifications` at action completion, not from a render or an effect that can replay on navigation. Do not duplicate them in local success banners. Preserve inline errors, ongoing conditions, detailed receipts and quiet editor indicators.

## Verify a new module

Render the module independently as well as inside the host. Check light and dark
themes, material themes when supported, 320px width, keyboard-only operation,
visible focus, loading/empty/failure/denied states and long labels or identifiers.
Horizontal overflow belongs inside the table or canvas, never on the page.
Validate actual interactions and rendered styles; checking class names alone does
not establish consistency. The Weather sample uses this public recipe without a
module stylesheet, and the standalone React example uses published package imports.

## Delivery acceptance

For visible or interaction changes, use the repository's [Studio UX acceptance](../../docs/contributing/studio-ux-acceptance.md) workflow. Choose an existing reference and observable states before implementation; verify the actual composed host and independently inspect its screenshots. Reuse shared controls and fix recurring defects in their owning primitive. A component fixture or successful build alone does not establish product acceptance.

## Navigation and focused editors

Set `featureAreas.nav.icon` to the module's icon component; the feature-area registry
forwards it to the shell, including child entries. Use a recognizable domain icon
rather than relying on the shell's fallback icon.

In form editors, lead with required authoring work and group optional settings in an
advanced disclosure. Reveal that disclosure when a saved or local field needs
correction. Represent provider-fixed values as readable facts while retaining exact
focus targets for diagnostics. Validation surfaces should have one heading and one
severity summary; keep server rejection distinct from the severity of its messages,
and place background explanations behind a help disclosure.

Reusable activity editors put Designer, Inputs, Outputs and Outcomes in the main tab
row, with collection counts. Show only the active collection and its provider
mappings; retain mounted default editors so tab changes never discard unapplied
text. Diagnostic links select the matching collection before focusing the exact
member or field. Provider boundary editors can use the optional `contractSection`
prop to show the corresponding output or outcome mappings.

## Navigation

Declare a purposeful `placement` for every top-level destination: `build`, `operate` or `integrations`. `main` is reserved for direct shell destinations such as Home. There is no default catch-all: an undeclared top-level workspace contribution is route-only. Children may omit placement to inherit their parent; explicit placement in a different group lifts the child into that group. Feature-area navigation forwards `parentId`, `childDisplay` and `viewLabel`.

Migrating from earlier Studio builds: the `more` placement and `defaultStudioNavigationPlacement` are removed. A contribution that still declares `placement: "more"`, or none, is route-only and no longer appears in navigation; give it `build`, `operate` or `integrations`.

Use `parentId` to join an existing resource surface. A parent with `childDisplay: "views"` presents its children as local route links instead of nested sidebar entries. Its optional `viewLabel` includes the parent's own route, such as Diagnostics' Overview. A parent without `viewLabel` links to its first accessible child and disappears when it has none. Modules still own their routes, components and backend requirements; navigation is discoverability, not an authorization boundary.

The built-in homes are:

- Build: Workflows (Definitions, Executables and available BPMN authoring), Agents, Decisions, Library (Activity definitions, Templates), and installed authoring modules such as ETL.
- Operate: Runs, User tasks, Diagnostics (Overview, Console, Structured logs, OpenTelemetry), and installed operational modules such as saved Dashboards and Questions when their Dashboard API is available.
- Integrations: Connections and Secrets.
- Settings: `settings` for Studio preferences, `authoring` for Workflow authoring, `runtime` for Runtime, and `admin` for Administration. Only `admin` adds the host-control read gate; preserve each module's existing permission/backend gates.

Sidebar groups start open and remember explicit choices in `elsa-studio.ui.nav.groups`; Appearance Reset clears them. Active destinations and search results reveal their group. Search can expose local view links without permanently duplicating them in the sidebar. Icon rail and top navigation use the same authorized contributions; top menus close on Escape, focus leaving, outside clicks and navigation. Keep links backend-qualified and preserve native/workspace new-tab intent.

Use the shared page spacing tokens and restrained navigation surfaces in every theme. The default page gap is 24px; collection row density and domain workbench canvases retain their existing layout contracts. Do not introduce competing palette literals or module-local chrome scales.
