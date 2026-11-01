# Sketchbook Studio appearance

**Status**: Implemented
**Issue**: [#3609](https://github.com/elsa-workflows/elsa-foundation/issues/3609)
**Program bucket**: none/free-flow

## User scenarios

1. Choose Sketchbook as a complete appearance and reopen Studio with the same selection.
2. Keep Sketchbook geometry and drawing treatment while choosing another palette, mode, or navigation arrangement.
3. Author and inspect workflows with warm paper, graphite outlines and restrained watercolor, while retaining clear ports, keyboard focus, runtime health and selection.
4. Use collections, dialogs, inspectors and optional modules with the same public appearance roles.
5. Configure a matching identity-owned login page with readable fields, safe custom branding and local artwork.

## Requirements

- FR001: Register a built-in Sketchbook palette/preset and independently selectable Sketchbook workspace style in the existing appearance controls and host configuration.
- FR002: Supply Light, Dark, Dim and High contrast palettes. High contrast removes decorative grain/washes and preserves explicit focus, control and graph boundaries.
- FR003: Use the existing typography, shape and public token contracts. Add no drawing dependency, model state or persisted definition data.
- FR004: Apply paper, fine graphite edges and restrained watercolor consistently to the shell and shared controls; provide precise authoring and runtime graph treatment across flowchart, sequence, BPMN and composite views.
- FR005: Preserve custom host branding, source-owned/read-only behavior, keyboard interaction, undo/redo, runtime identity and the independent appearance axes.
- FR006: Preserve existing themes and defaults. Textures are decorative and do not alter hit targets, layout measurements or pointer events. No perpetual decorative animation.
- FR007: Provide matching host-owned login layout, palette and bundled artwork through existing Identity:Login options. Preserve custom presentation, branding, safe URLs, encoding, antiforgery, shell paths and form behavior.

## Verification

- SC001: Appearance selection and custom-theme duplication retain Sketchbook metadata; preferences survive remount/reload.
- SC002: Palette/mode/navigation changes preserve style and an unsaved edit.
- SC003: Representative authoring, runtime, lists and dialogs remain usable in all four modes, including alternate palettes; semantic confirmations and keyboard focus retain visible contrast.
- SC004: Narrow layouts have no page overflow; High contrast has no decorative texture; reduced-motion settings produce no added animation.
- SC005: Actual rendered Studio components demonstrate the intended handcrafted appearance. Screenshots and the exact reviewed source are recorded with the PR.

- SC006: Sketchbook login retains antiforgery, safe encoding and shell-prefix asset routing; light/dark pages remain readable at 320, 800 and 1440 pixels, with no overlapping illustration or horizontal overflow.

## Scope boundaries

Appearance only. Existing graph primitives and logical handles remain authoritative. The shared theme/token and workspace seams own the implementation. Illustration-generation prompts and brochure hosting are separate artifacts.

## Specification review

Reviewed against the owner-approved design and current appearance structure: implementable without new product concepts or runtime contracts; no unresolved scope decision.
