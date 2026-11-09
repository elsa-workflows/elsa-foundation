# Data and presentation state — Studio secret creation

This is a UI-only design. It adds no persisted entity, endpoint, SDK model, store type, or public contract. Keep the existing create request fields, values, timing, and optional-field omissions.

## Existing creation data

| Existing value | Presentation | Behavior to preserve |
|---|---|---|
| Technical name | Primary region, before Type and required source | Editable; preserve current trim/lowercase normalization and validation. Do not include it in disclosure summary or count. |
| Type | Primary region | Preserve current default and Type-change behavior. Type continues to determine the source control and available Store choices; add no new eligibility rule. |
| Value | Primary required source for value-backed Stores | Preserve entered text and existing Show/Hide behavior. If the control is recreated, it starts masked while the parent-held text remains. Never put the Value in the count, summary, or metadata response. |
| Configuration key | Primary required source for the Configuration Store | Preserve existing requiredness, parent-held text across switching, and trim-on-submit behavior. Never put it in the count, summary, or metadata response. |
| Expires | Primary region, outside More options | Preserve current input, conversion, and policy. Do not count it or add constraints. |
| Type and Store descriptions | Outside More options, after the source and Expires | Preserve available descriptor text without allowing it to displace the required source. Missing descriptions do not imply storage guarantees. |
| Selected-Store context | Compact context outside More options | Use available display name, descriptor name, or current Store name. Do not infer storage guarantees from the label. |
| Display name | More options | Preserve existing default/fallback and trimming behavior. |
| Description | More options | Preserve trim behavior and omission when empty. |
| Tags | More options | Preserve current comma-separated parsing and submitted values. |
| Store | More options while the current Store state is non-empty; directly reachable when it is empty | Preserve current defaults and eligibility. An empty state remains recoverable through the existing Store and Type controls. Do not add descriptor-completeness, read-only, permission, or other validation. |
| Scope | More options; canonical matching/access hint outside it | Preserve picker behavior and exact custom text. Keep the existing Scope input accessibility association with the single canonical hint. Suggestions and catalog failure do not change eligibility. |

## Create-dialog presentation state

| State | Required behavior |
|---|---|
| More options initial state | Start collapsed when optional values are unconfigured and the existing default Store is selected. |
| More options count | Count only authored optional metadata and a Store changed from its existing default. Exclude the internal default Store, Technical name, Value, Configuration key, and Expires. Use a generic count; never expose field contents. |
| Manual collapse/reopen | Permit collapse when configured. Keep authored metadata, Store choice, and exact custom Scope unchanged when the user reopens the section. |
| Scope suggestion failure | Treat as advisory. Open the disclosure for Retry, preserve focused/authored Scope, and do not change creation eligibility solely because suggestions failed. Preserve the shared disclosure's existing recovery-open and dismissal behavior. |
| Value reveal state | Keep current Show/Hide behavior. A recreated Value control is masked by default without clearing the parent-held source text. |
| Save error/cancel | Preserve existing field retention, error focus, Cancel/Escape behavior, scroll behavior, and focus return to the opener. |

## Request and confidentiality boundary

The dialog remains a new-Secret flow. It has no existing-Secret selector and performs no read of a previously stored Secret value. Preserve the current request construction:

- Technical name keeps current normalization/validation; Display name keeps current default, fallback, and trim behavior.
- Description remains trimmed and omitted when empty; Tags retain current parsing.
- A non-empty custom Scope is sent exactly as authored, including case and whitespace; existing matching/access policy does not change.
- Value is passed as entered for value-backed Stores. Configuration key uses its current trim-on-submit behavior. Switching Types or Stores retains current parent-held source text.
- Expires retains existing date-to-request conversion and policy.
- Keep current optional-field presence or omission and all existing creation defaults and eligibility.

The Secrets page may continue its metadata-only reads. Do not add any request that retrieves a stored value, and do not place entered Value or Configuration key in disclosure content, summaries, or fixture metadata responses.
