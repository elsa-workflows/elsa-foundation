# Presentation verification

Browser verification, 17 October 2026. Executable evidence baseline: `5fa96727f`; recommendation reviewed separately.

- Real in-app browser loaded the local HTTP-served HTML successfully; no console errors or warnings were returned after exercising controls.
- The model selector updates the execution model, composition, worker, resource and nested-composition descriptions. Agent view explicitly places the collaboration inside Sequence and keeps turns out of separate actors.
- Zoom applies actual CSS zoom (observed 1.25 with a 125% label), with reset and bounded controls.
- Enter activates the Recovery view; labeled native controls expose the scenarios and transition actions to keyboard/accessibility APIs.
- All five scenarios were advanced to their final state: crash/reclaim, duplicate receipt, cancellation-first, success-first and ETL admission. They remain explicitly labeled simulations.
- Responsive widths 320, 768, 1024 and 1440 were tested. A 320px overflow was found and fixed by constraining grid children; rerun observed body width equal to viewport width at all four sizes. Horizontal diagram scrolling stays inside its labeled focusable region where needed.
- Source inspection confirms inline CSS/JavaScript, no external assets, no network API calls and no animation/timer loops. A reduced-motion stylesheet disables animation/transition. This is static motion/dependency inspection, not emulated operating-system preference testing.
- Direct `file:` browser verification was refused by browser URL policy (only HTTP/HTTPS permitted); no bypass was attempted. Standalone/offline behavior is supported by the dependency audit, but a disconnected browser or direct-file run was not verified. GitHub source links intentionally require a connection; neighboring Markdown links require the accompanying report bundle.

Evidence labels were refreshed after combined review. The distinction between executed proofs and explanatory simulations remains visible. Repeat focused checks after material UI changes.
