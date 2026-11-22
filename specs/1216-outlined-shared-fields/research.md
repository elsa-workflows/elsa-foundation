# Shared-field decisions

## Native outline and persistent label
Use a native fieldset/legend border gap with an explicit label for single native controls. This preserves wrapping and transparent/custom surfaces without masking the underlying paint. Fieldsets are decorative single-control shells (presentation role); genuine composite groups retain group semantics. Source research: shared Field.tsx, approved SecretFormField.tsx, host selectors, Minimal/UserTasks overrides, and the HTML/ARIA standards. No text-width measurement or duplicate hidden label.

## Existing APIs and native behaviour
Keep StudioField's label/description/meta/children API. Add the explicit render-function StudioFormField contract for composed controls and adjacent actions. A narrow native-child bridge preserves existing IDs, refs, handlers, values and description tokens. Do not recursively clone arbitrary contributed components. Composite and rich editors use a named group or their own existing accessible control contract; individual choices keep their native labels.

## Scope census
Read-only source research found316 production TSX files,412 native source sites across122 files:279 scalar Outlined targets plus1 conditional text/checkbox branch,77 choices,26 compact table/grid,19 searches,4 rich editors/composers,6 file controls.326 label/shared/helper sites and63 standalone htmlFor sites were inspected. The full raw census and independent theme report are retained in the external outlined-rollout-4219 research evidence; rollout-inventory.md records adoption and semantic exceptions. This is a source census, not executed acceptance.

## Theme policy
Use existing semantic font, edge, surface, danger and focus tokens. Add dedicated outline geometry tokens that cannot be erased by older card-style StudioField overrides in Minimal/UserTasks. Scope resets to the new shared field, preserve textareas and native select affordances, and test broad Agent/UserTasks selectors. Twelve palettes expose four appearances; workspace styles independently affect geometry. Material recipes remain compatible but no currently selectable material theme exists.

## Delivery
Use the existing independent source/UX review loop, actual composed-host evidence and required CI. Do not revive the closed prototype branch or old Azure operators. Current main-health4177 remains a separate live report; do not infer healthy main or waive its gate.
