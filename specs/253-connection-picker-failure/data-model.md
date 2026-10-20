# Data Model: Connection picker load-failure explanation

This work introduces no persisted entity, field, credential value, or service data shape.

| Existing value | Meaning | Change |
|---|---|---|
| Connection reference | Opaque workflow value identifying the selected Connection | Preserve it while loading fails or retries; do not rewrite it unless the author chooses another Connection. |
| Connection metadata | Descriptive options returned for the picker, such as name, display name, type, and status | Reuse the current metadata response. No stored credential value is requested or shown. |
| Picker view state | Loading, available, or failed presentation of the metadata list | Add user-facing explanation to the existing failed presentation; keep the existing loading and available behavior. |

No schema migration or API contract change is planned.
