# Prototype findings and rollout guidance

## Current result

The Create secret prototype uses one filled native-control surface with persistent inset labels. Ordinary text/password labels float on focus, content and native autofill; select/date/multiline labels remain raised. Secret value guidance, validation, Store help and Scope help sit with their controls. More options retains existing optional-value counting and recovery behavior. Editing is disabled during pending creation and restored after failure.

At the same 1280×800 Classic Dark preview, the original collapsed dialog measured 625px high with a 488px scroll body; the final compact Classic Dark capture measured 521px. Final captures and geometry are retained with the validation run. The reduction includes relocating optional guidance; label nesting alone does not account for the saving.

## Findings fixed during review

- Shared native select arrows were cleared by module background shorthand. Compact selects retain a token-colored chevron and are checked for a rendered background indicator.
- Scope refresh/retry actions use StudioButton in compact mode rather than browser-default small buttons.
- Root-only font enlargement did not change pixel-based form text. The proof now doubles the actual field typography tokens, asserts computed enlargement and verifies label/value separation before checking narrow scrolling and actions. Field metrics scale with text.
- Theme transitions must settle before contrast inspection. After settling, small error text in some Dim palettes still needed stronger contrast; the prototype mixes the existing danger foreground with primary text. The Create action similarly retains the accent hue with stronger foreground separation. These adjustments remain local to the prototype.
- Long labels and fields in narrow dialogs use a wrapping, permanently raised layout rather than truncating the field name. Multiline labels use a flush painted strip so scrolled content does not appear above the name; the browser proof explicitly scrolls the writing area and inspects the covering band. Narrow dialogs keep labels in normal flow so they can wrap, and fields do not shrink when content grows.

## Candidate shared component contract

Promote only after this prototype is reviewed. A future shared field should own the persistent label, native-control ID, supporting/error associations, focus/invalid/disabled/read-only treatment, optional trailing actions and density. Native controls retain their behavior. Separate the control layout from theme paint; keep semantic tokens authoritative. A configurable inset/floating label mode is useful; forcing complex editors into floating labels is not.

Selects, date/time fields, multiline inputs and long labels need an explicit permanently raised mode. Read-only text remains selectable and distinguishable from disabled controls. Autofill must be detected through the native state, not only React's current value. Secret reveal remains a separate named action and defaults to concealed.

## Wider rollout sequence

1. Review the actual Create secret journey and theme/state captures; record any remaining findings on #3851 and its PR.
2. Extract the proven field shell and metrics into Studio UI with a small documented contract and representative tests.
3. Migrate a second contrasting form (Connections) to challenge that contract before broad conversion.
4. Migrate simple text/select/date forms, then activity-property editors. Leave code editors, grouped checkboxes and other composite editors with appropriate external labels.
5. Inspect each migrated composed journey in theme/narrow states. Avoid a global CSS conversion that presumes all field markup is equivalent.

## Validation boundary

The theme gallery is isolated actual-component evidence. Read-only is a representative contract state, not a change to Create secret authorization. Arbitrary custom theme palettes and real password-manager integrations are not claimed as inspected; the prototype covers native autofill-compatible layout and inherits custom theme tokens. Product acceptance requires the separate owned composed-host journey and independent screenshot inspection described in the acceptance record. Exact revisions, run artifacts, counts and review receipts belong on the PR.
