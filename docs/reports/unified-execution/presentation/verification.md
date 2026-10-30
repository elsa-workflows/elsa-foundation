# Presentation verification

## 19 October 2026 completion

The historical research presentation from `4b464e1637483e89a11f632f2c99c5779cb4e0de` now has a verified standalone browser path. This update imports the report bundle into the documentation tree, adds named keyboard scrolling, breadcrumbs, a normal-execution scenario and per-detail evidence links, and records browser evidence. It does not rerun the engine experiments or update their 17 October evidence baseline (`5fa96727f`). Current production status remains in [Program #2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892).

Root ran [verify.cjs](verify.cjs) using Playwright 1.62.1 and installed Google Chrome 155.0.8059.82 in a new isolated headless profile on macOS. The context was offline **before** its first direct `file:` navigation, with reduced motion enabled; no existing signed-in browser profile was used. The prior in-app browser only accepted HTTP/HTTPS URLs. This run uses Chrome's supported local-file navigation without changing that tool's policy or disabling browser security.

Result: **17 check groups passed**, zero HTTP(S) requests, and zero console warnings, errors or page exceptions. [Structured results](evidence/result.json), [accessibility tree](evidence/accessibility-tree.txt), and [offline screenshot](evidence/offline-agent.png) are retained. The validated HTML SHA-256 is `2c686416a343f3bb12cb41eaeccb408fb9108d82bddfcf69a2a85b5b657d184a`.

- Cold direct-file loading and reload work while `navigator.onLine` is false.
- All five model lenses and all nine responsibility controls update their heading, nested-composition text, selected state and details. Each detail labels the architecture as proposed and links either to the recommendation or a pinned, bounded witness. Each model’s nested-composition example also has its own proposed label and source, independent of the selected detail. Breadcrumbs track the current model and detail; the keyboard-accessible overview button restores the Native/kernel view, zoom and scroll position.
- Diagram zoom changes computed CSS zoom, stops at 75%/175%, and resets to 100%.
- Tab navigation reaches the skip link and view controls; Enter activates the Recovery view. The diagram exposes the accessible region name **Scrollable architecture diagram** and ArrowRight scrolls it at narrow width.
- All six execution scenarios (normal execution, crash/reclaim, duplicate result, both cancellation orderings and ETL backpressure) advance through every step. Previous, Restart and terminal disabled states work; the timeline exposes exactly one current step.
- At 320, 768, 1024 and 1440 pixels, body width does not exceed the viewport. Diagram overflow remains inside its own scroll region.
- Reduced-motion emulation matches the media query. Computed animation, transition and smooth-scrolling states are disabled.
- The three local Markdown report links open offline when the accompanying report bundle is present. GitHub evidence links intentionally need a connection; external evidence is not cached or mirrored by this artifact.
- With JavaScript disabled, the native diagram and explicit static overview remain visible.
- Historical research and simulation labels remain visible. Browser interactions do not execute Elsa or establish new engine guarantees.

Independent source review identified the missing explicit role on the focusable diagram container. A browser assertion for its named region failed on the original HTML (`0 != 1`) and passed after adding `role="region"`. Root inspected the rendered screenshot and reran the full focused browser script after the correction. A subsequent audit against the full program brief found that breadcrumbs and an explicit normal-execution path were missing. Both controls failed their baseline presence checks; [the pre-correction result](evidence/acceptance-controls-before.json) retains the previous HTML hash. The final browser run covers the added controls, all six scenarios and proposed-status/source links for all nine details under every model lens. Standards review also found one recovery-panel evidence link pinned to an older research commit; the [failing source-pin check](evidence/source-pin-before.json) is retained, the link now uses the bundle’s source revision, and the browser harness checks all rendered experiment links. The offline claim is now supported by the recorded run, and the footer distinguishes standalone HTML from the accompanying reports.

This is browser-offline verification, not a physically disconnected machine experiment or screen-reader certification. It does not claim cross-browser qualification. The isolated test tooling requires Playwright and Chrome, but the delivered HTML itself has no external assets or runtime dependency.

## Repeat

Use Node.js with Playwright available in its module search path and installed Google Chrome. From the repository root:

```sh
node docs/reports/unified-execution/presentation/verify.cjs /tmp/elsa-presentation-qa
```

If Playwright is installed in a separate tool runtime, set `NODE_PATH` to that runtime's `node_modules`. The output directory receives the structured result, screenshot and accessibility tree. The script derives the report root from its own location, records the HTML hash and browser/tool versions, and replaces local filesystem prefixes in the result with `<report-bundle>`.

## Earlier HTTP verification — 17 October 2026

The in-app browser exercised the locally HTTP-served artifact, all five model lenses/scenarios, keyboard activation, zoom and responsive widths. A 320px body overflow was fixed and retested. Direct `file:` navigation was refused by that browser's URL policy. At that checkpoint, disconnected-browser behavior and actual reduced-motion emulation were unverified; only dependency and stylesheet inspection supported them. The 19 October run above closes those browser evidence gaps without retroactively changing what was tested on 17 October.

## Presenter path

1. Start with Native workflow: inspect the execution model, nested Sequence/Flowchart and kernel. Use the scrollable diagram for pan, zoom controls for scale and breadcrumbs to return to the overview.
2. Switch to ETL: distinguish partition work and record movement from serial coordinator decisions.
3. Switch to BPMN: show the interpreter as semantic owner and the pipeline as a correlated child.
4. Switch to Agent collaboration: distinguish message/round semantics from memory/knowledge/tool bindings.
5. Switch to Independent rule model: show extensibility beyond an activity graph.
6. Walk through normal execution, then crash/reclaim and cancellation in the Recovery lab, then use Evidence & decisions to discuss the historical evidence, its limits and the current program link.
