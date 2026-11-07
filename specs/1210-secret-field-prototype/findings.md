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
- Capture preparation needs a bound. A hosted evaluation waiting for fonts and document-wide animation completion stalled without recording which readiness condition remained pending. Paused or nonfinishing finite animations must not deadlock evidence collection; native screenshot animation handling supplies settled frames, while bounded font readiness and overflow checks before and after capture keep failures visible. Adding a journey must preserve the current named baseline and its required capture set rather than letting a generated test fixture silently shrink with its configuration.

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

## Local validation result

Tested UI source: `6732623e2d9294a19f8ad5493f142ea7dacf6d7b`, clean. Secrets 73/73, browser 76/76 (48 appearance combinations, no skips/retries), owned composed 12/12, architecture 616/616, generated-map check, build/typecheck and touched-file lint passed. A focused mutation removing aria-invalid failed the new regression and passed again after restoration. The architecture discovery inventory has 601 entries; two deferred MemberData theories expand from 2 entries to 11 domain-feature and 6 collectible-metadata cases at execution, accounting for 616 executed cases. Independent review receipts and the pending shared CI artifact gate are recorded on PR #3857. Failed preliminary runs are retained separately from final evidence.

## Filled / Outlined comparison

The preview now compares both treatments independently of theme choice. Native radio controls in the toolbar and dialog header preserve entered values and validation when switching, with URL/local preview persistence. Outlined labels sit on the border even when empty; keeping them in flow lets long/enlarged names wrap and keeps multiline scrolling below the label. The notch consumes the same surface role as its surrounding dialog/optional section/state sheet.

The comparison implementation remains confined to browser preview files. It adds no shared API, product setting or new dependency. The prior real-host proof covers the unchanged product implementation; the two-style comparison has its own isolated matrix and independent visual review recorded on PR #3857. Following the owner's visual comparison, Outlined is the prototype default. Explicit URL choices and saved preview preferences still take precedence; shared-component and product defaults remain a later decision.

Comparison review also established two details for a future shared field: state galleries must use natural item heights so adjacent help text cannot inflate border-painted fields, and focus scrolling must include the complete labeled field rather than only the native input. The preview and regressions now cover both, including reverse keyboard navigation through native date/time editors.

Comparison qualification: 134/134 local browser checks passed at UI source `1becff2c915df35468996a3930b61000e378c13c`, including all 96 style/theme/appearance combinations. CI now retains the comparison PNGs, complete per-shard browser JSON inventories and source/head provenance for independent review, without adding permissions or a new required context. The Outlined option remains isolated to the preview; the composed host continues to exercise the unchanged product implementation. Current-head CI/download receipt and final independent review are recorded on PR #3857.
