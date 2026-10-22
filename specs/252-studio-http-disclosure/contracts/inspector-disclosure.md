# Inspector presentation contract

The design catalog remains authoritative. Consume existing per-input tier and isRequired JSON; extend SDK declarations additively rather than introducing another wire contract.

| Input state | First view |
|---|---|
| Required, any tier | Directly visible |
| Essential / Setting | Directly visible |
| Missing, null or unknown tier | Directly visible |
| Optional, no stored state/error | Under one shared More options section |
| Optional, explicitly stored default/nondefault/binding/conversion/extras | Initially open, then collapsible with a configured badge; values remain mounted and unchanged |
| Current host-owned validation error | Visible/error indicated; section opens |
| Current private error from a reporting-capable property editor | Visible/error indicated; section opens |
| Selected property editor without reporting capability, or selected inline expression editor | Directly visible conservative fallback; a separate diagnostics provider is not a completeness guarantee |
| Dynamic-options source failure | Existing alert/Retry visible, including retry loading |
| Initial loading, empty options, warning/info, unavailable tooling | Existing informational meaning; not labeled a validation error |

HTTP direct inputs: URL, Method, Content, Connection. Secondary: ContentType, RequestHeaders, ExpectedStatusCodes, Timeout. Preserve descriptor order within tiers/categories. Content remains available for GET/HEAD; no visibility or method transition clears content/type or any unrelated input.

All wrappers, opaque reference keys, structured Connection references, conversion requests and unknown ArgumentState fields retain their existing meaning and wire form. Rendering/collapsing emits no authored change. Method edits affect Method only. Requiredness does not arise from a tier.

Keyboard and ARIA use the public disclosure primitive. Hidden rows remain mounted; removed rows and changed activity scopes release local visibility signals. Custom-editor private validation is not guessed from DOM; preserve access conservatively when no host signal exists.

The metadata partition stays stable. A conservative editor fallback can hold the existing More options section open without an Error pill, so inputs are directly visible without relocating/remounting their editor subtree or discarding local invalid text.

## Additive property-editor reporting contract

Root-approved refinement, 2026-10-20: `StudioActivityPropertyEditorProps.onValidationChange?: (hasError: boolean) => void` reports only current local blocking-error presence. `StudioActivityPropertyEditorContribution.supportsValidationReporting?: true` promises all private blocking errors are reported (or the editor has none). Omission keeps an optional row directly visible. All members are optional, retain compatibility with existing contributions, and carry no values or diagnostic messages.

Built-in duration/number editors report invalid state and recovery. Each mounted scalar/collection/dictionary editor instance has independent error state: a valid sibling cannot clear another's error. Removal or replacement releases only that instance; callbacks from obsolete instances/scopes are ignored. Draft, activity version, node and input identity delimit the row scope. Errors clear after recovery and actual removal, not merely after collapse. Nothing is serialized or authored by reporting.
