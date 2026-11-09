# Home interaction state

No new storage or backend model.

- **HomeWorkflowSummary** retains existing identity/name/update metadata and optional current draftId; absent/blank draftId is not a candidate. PublicationState is independent.
- **Draft discovery** is idle → loading → ready(empty/one/many) or failed. Request results belong to the initiating backend/session/tenant generation. Dismiss/authority change invalidates them; abort alone must not allow late completion to select a draft. The complete candidate count determines one/many before the recent-eight window.
- **Creation intent** is recognized URL request → consumed → delivered once to existing dialog → acknowledged. Cancel/history/re-render never recreate the consumed request. An already selected definition wins over the conflicting recognized intent.
- **Selected definition** remains the exact route identity. Loading, loaded current draft, loaded no draft and failed/denied are distinct; retry never chooses another identity or creates a draft.
- **Layout** remains the existing Home preference document. Task navigation neither rewrites widget entries nor changes their saved options.
- **Affordance** distinguishes feature absent, known denied and available/unknown-server-authority states. Client permissions only explain current known access; the server enforces each operation.
