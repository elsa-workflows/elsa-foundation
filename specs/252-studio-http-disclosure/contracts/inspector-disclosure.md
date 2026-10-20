# Inspector presentation contract

The design catalog remains authoritative. Consume existing per-input tier and isRequired JSON; extend SDK declarations additively rather than introducing another wire contract.

| Input state | First view |
|---|---|
| Required, any tier | Directly visible |
| Essential / Setting | Directly visible |
| Missing, null or unknown tier | Directly visible |
| Optional, no stored state/error | Under one shared More options section |
| Optional, explicitly stored default/nondefault/binding/conversion/extras | Visible or clearly flagged; existing shared component may force open |
| Current host-owned validation error | Visible/error indicated; section opens |
| Dynamic-options source failure | Existing alert/Retry visible, including retry loading |
| Initial loading, empty options, warning/info, unavailable tooling | Existing informational meaning; not labeled a validation error |

HTTP direct inputs: URL, Method, Content, Connection. Secondary: ContentType, RequestHeaders, ExpectedStatusCodes, Timeout. Preserve descriptor order within tiers/categories. Content remains available for GET/HEAD; no visibility or method transition clears content/type or any unrelated input.

All wrappers, opaque reference keys, structured Connection references, conversion requests and unknown ArgumentState fields retain their existing meaning and wire form. Rendering/collapsing emits no authored change. Method edits affect Method only. Requiredness does not arise from a tier.

Keyboard and ARIA use the public disclosure primitive. Hidden rows remain mounted; removed rows and changed activity scopes release local visibility signals. Custom-editor private validation is not guessed from DOM; preserve access conservatively when no host signal exists.
