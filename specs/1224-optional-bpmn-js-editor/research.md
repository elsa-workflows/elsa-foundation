# bpmn-js upstream feasibility inputs for #3597

Historical research, observed 5 November 2026. Version/latest comparisons below describe that date; they are not current release recommendations. Subsequent prototype results are recorded separately in the [evidence ledger](evidence/prototype-ledger.md). The 12 November [adoption decision and extraction plan](delivery-slices.md) uses the same pinned candidate and requires fresh production qualification.

Observed 5 November 2026 for [#3597](https://github.com/elsa-workflows/elsa-foundation/issues/3597). Research only, not composed-host feasibility or delivery acceptance. The [specification](spec.md), [plan](plan.md) and [tasks](tasks.md) retain the delivery boundary. The owner selected the shared Elsa inspector; the synchronization design remains provisional until the embedded prototype demonstrates preservation.

## Pinned package

- bpmn-js 18.31.1, released 21 October 2026. npm registry latest matches GitHub v18.31.1; toolkit landing page still advertises 18.16.0.
- npm tarball: https://registry.npmjs.org/bpmn-js/-/bpmn-js-18.31.1.tgz
- integrity: sha512-Hc7LzrgwfcBRUAdI/380Q2v1MJ115atGEVGadawLfDbHdo+r+IkmNj8fuYPFmWglUYt1KYIaC46OeX16HGTKQw==
- Package contains 560 files, 5,290,435 unpacked bytes. Eight direct runtime dependencies. Isolated install resolves 20 installed packages including bpmn-js; no Camunda execution runtime/property panel package.
- Direct dependencies: bpmn-moddle ^10.3.1, diagram-js ^15.28.0, diagram-js-direct-editing ^3.6.0, ids ^3.0.2, inherits-browser ^0.1.0, min-dash ^5.1.0, min-dom ^5.3.0, tiny-svg ^4.1.4. Exact top-level pin alone does not freeze transitive versions: retain lockfile.

## License and attribution

Pinned LICENSE is custom bpmn.io license, not plain MIT: https://raw.githubusercontent.com/bpmn-io/bpmn-js/v18.31.1/LICENSE

Retain copyright and permission notice with distributed copies/substantial portions. Keep watermark source intact. Keep the rendered bpmn.io hyperlink fully visible and unobstructed. Preserve original protected project-logo code; Studio panels, bars and overlays must not cover it, including narrow/mobile/theme states. The install's remaining package declarations are MIT, except inherits-browser ISC and htm Apache-2.0; bundled source also carries an Apache-2.0 adapted-source comment. Generate/ship applicable notices rather than labelling the whole dependency MIT.

## Footprint observation

Prebuilt modeler production min JS: 571,729 bytes raw / 162,180 bytes Node zlib gzip default. Viewer: 186,117 / 52,225. Required packaged diagram-js.css 28,471 / 5,517; bpmn-js.css 3,673 / 1,245; bpmn.css 9,781 / 2,136; bpmn.woff2 13,204 raw. These are upstream asset observations, not the actual Studio bundle or network budget. Lazy ES module import permits optional loading, but measure composed host requests before/after first editor open and cached reopen.

## APIs and boundaries

- Modeler import: bpmn-js/lib/Modeler; asynchronous importXML/saveXML; elementRegistry and businessObject give stable BPMN IDs and XML model.
- additionalModules supplies/overrides injected services; moddleExtensions accepts JSON schemas for typed Elsa attributes/elements. Custom RulesProvider/palette/context pad can constrain execution profile.
- When modifying the modeler's object tree, modeling.updateProperties/updateModdleProperties provide undoable commands; raw businessObject mutation bypasses its history. This does not require moving Elsa inspector authority into the modeler: the prototype must prove how the shared inspector's canonical operations and projected canvas participate in one history. selection.changed/element.click can map BPMN IDs to Elsa panel selection; commandStack.changed includes native undo/redo.
- Import/open clears diagram and native CommandStack. Reimporting after every external property change or remounting on switching loses native history. This is a material integration issue to prove explicitly. Prefer one Studio-owned history with complete canonical before/after changes and guarded projection, or route property edits through retained native modeler history while reconciling canonical history at boundaries. Avoid undocumented _stack internals.
- Keyboard binding is implicit to canvas SVG in diagram-js15; legacy keyboard.bindTo/document is removed. Decide toolbar/hotkey ownership and prevent competing Ctrl+Z on Elsa fields.
- Current assets expose --bio-* semantic colors and renderer default colors. Map Studio tokens at the integration boundary; do not alter protected logo source. Diagram serialization can persist explicit element color independent of display theme; do not change semantics/layout simply when theme changes.
- Toolkit validates BPMN modeling/XML schema conventions, not Elsa executable semantics. Drawing a Camunda/Zeebe construct is not execution compatibility. Restrict authoring and validate publication against actual Elsa support.

## Narrow preservation probe performed

An isolated Node probe ran against bpmn-moddle10.3.1. A task's unknown elsa:taskBinding JSON attribute plus a nested foreign elsa:authoring/elsa:listener subtree inside extensionElements survived parse/write with zero warnings. Gateway default ref and flow refs survived. This does not prove modeler editing/deletion/type-replacement, Elsa exporter preservation, save/reload, publication or runtime behavior. Typed descriptors are a candidate for editable/reference-aware extension data; opaque projection is another candidate to assess. Test paste/replace/delete identity remapping. Unknown opaque data retention alone cannot preserve references automatically.

The input was a BPMN definitions document containing process `P`, task `Task1` with a namespaced JSON binding attribute, an `extensionElements` subtree with a nested listener, exclusive gateway `Gateway1` defaulting to `Flow1`, and that flow targeting `Task1`. `new BpmnModdle().fromXML(input)` followed by `toXML(rootElement, { format: true })` retained those values; output escaped JSON quotes differently. This is a serialization observation, not a supported Elsa binding or listener example. The probe used `import { BpmnModdle } from "bpmn-moddle"`; version10 no longer provides a default export.

## Owning-code hazards for the prototype

The initial [plan](plan.md#actual-hazards-to-disprove) identifies existing boundaries that the optional editor must respect. Source inspection confirms:

- [`BpmnDocumentExporter`](../../Essentials/src/Activities/Bpmn/Interchange/Services/BpmnDocumentExporter.cs) intentionally carries only catalog-resolved task bindings with portable literal inputs. Structured child activities, output captures, connection/sensitive inputs, expressions and special input settings generate omissions. `CollectTaskBindings` targets task types; a Decision child on a gateway is outside that task collection. Public XML export/import is therefore insufficient as an internal switching transport for the selected requirement.
- [`BpmnDocumentImporter`](../../Essentials/src/Activities/Bpmn/Interchange/Services/BpmnDocumentImporter.cs) materializes new authoring nodes and explicitly reports typed/defaulted variables as degraded when imported as `Object` without defaults. Preserving the full canonical authoring tree avoids inferring that these conversions preserve untouched Elsa configuration. That preservation still requires behavioral proof.
- [`syncBpmnCanvasToScope`](../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/bpmn/bpmnAdapter.ts) keeps authored element/flow fields, but trims the activities inventory using only `childNodeId`. Listener activities referenced by `listenerNodeId` need explicit retention coverage before reusing this path for switching.
- [`bpmnInterchange.ts`](../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/api/bpmnInterchange.ts) projects BPMNDI shape positions to design metadata and back. It does not establish complete size, waypoint, label or nested-scope layout preservation. The prototype must inspect and retain relevant BPMNDI fields beyond x/y.
- [`BpmnAuthoredDirectPublisher`](../../Essentials/src/Activities/Bpmn/Interchange/Services/BpmnAuthoredDirectPublisher.cs) projects an immutable authored version through its own authorized direct-publication path. A successful native child-bound journey does not qualify direct human/service publication. Coordinate [#2736](https://github.com/elsa-workflows/elsa-foundation/issues/2736) without claiming that editor integration completes general import/publication safeguards.

These are investigation inputs, not frozen production contracts or permission to reinterpret unsupported models. Preserve the host-agnostic semantic ownership in [ADR0063](../../docs/adr/0063-bpmn-moves-to-a-host-agnostic-library.md).

## Feasibility evidence status

| Evidence | Status and limit |
|---|---|
| Released package, manifest, exact license and API inspection | Observed from primary upstream sources and the isolated npm tarball. |
| Foreign XML attribute/subtree and default-flow retention | Passed isolated moddle parse/write probe only. |
| Actual Studio optional asset/network budget | Not run; upstream distribution sizes are preliminary inputs. |
| Embedded canvas, shared inspector and coherent history | Not run; require composed-host prototype. |
| Both-direction save/reload, same-process publication, execution and inspection | Not run; no feasibility success asserted. |
| Themes, narrow layout, keyboard/focus, accessibility, attribution and independent UX acceptance | Not run. |

No adoption recommendation is final until the representative real-host proof and its negative controls establish the selected same-process behavior. A materially incompatible result returns to the owner with concrete alternatives rather than weakening preservation silently.

## Upgrade surface and cost

Maintain a narrow adapter around Modeler services/events and moddle schema. Freeze package/lock; inspect upstream changelog for modeler/diagram-js/moddle changes; repeat semantic+configuration round trips, undo/redo, paste/delete/replace, nested drilldown, keyboard/focus, themes, narrow layout, watermark and asset requests after upgrades. Retain current renderer/run inspection initially unless actual proof identifies a need. Highest costs are canonical state/history reconciliation, complete Elsa configuration retention, executable-profile restrictions and nested/third-party authoring parity, not initial canvas embedding.

## Primary sources

- Release: https://github.com/bpmn-io/bpmn-js/releases/tag/v18.31.1
- Package: https://raw.githubusercontent.com/bpmn-io/bpmn-js/v18.31.1/package.json
- Toolkit: https://bpmn.io/toolkit/bpmn-js/
- Walkthrough: https://bpmn.io/toolkit/bpmn-js/walkthrough/
- Pinned APIs: https://raw.githubusercontent.com/bpmn-io/bpmn-js/v18.31.1/lib/BaseViewer.js
- Modeling commands: https://raw.githubusercontent.com/bpmn-io/bpmn-js/v18.31.1/lib/features/modeling/Modeling.js
- Command stack: https://raw.githubusercontent.com/bpmn-io/diagram-js/v15.28.1/lib/command/CommandStack.js
- Keyboard: https://raw.githubusercontent.com/bpmn-io/diagram-js/v15.28.1/lib/features/keyboard/Keyboard.js
- Metadata schema example: https://raw.githubusercontent.com/bpmn-io/bpmn-js-examples/main/custom-meta-model/README.md
- Modeling rules example: https://raw.githubusercontent.com/bpmn-io/bpmn-js-examples/main/custom-modeling-rules/README.md
- Theme example: https://raw.githubusercontent.com/bpmn-io/bpmn-js-examples/main/theming/README.md
- Moddle named import/API: https://raw.githubusercontent.com/bpmn-io/bpmn-moddle/v10.3.1/README.md
