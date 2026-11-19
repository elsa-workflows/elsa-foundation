# Data model and state transitions

Task identity is a deterministic hash over persistence scope, tenant, workflow execution and concrete activity invocation. Persist the full identities as well as indexed hashes and verify them on read. Keep OperationKey for lineage, never use it to silently move an accepted response to a successor wait. Same-invocation replay is idempotent; explicit boundary retries create a distinct successor task after retiring old work.

Task aggregate: ID, tenant, workflow/invocation/node/operation IDs; title/description; one assignee or group ID; claimant; frozen action names/required-field policy; provider/version/form snapshot and case-preserved prefill; status; revision; exact committed bookmark recipient, including physical generation; accepted response and stable completion ID/request ID; created/updated UTC; ordered history. History, response and delivery responsibility are part of one CAS write.

| From | Operation | To | Authority |
|---|---|---|---|
| Absent | Stage | Staged | Owning activity, idempotent exact identity |
| Staged | Observe exact committed wait | Open/Assigned | Fresh runtime bookmark and invocation |
| Open group | Claim | Claimed | Current active eligible member, expected revision |
| Claimed group | Release | Open | Current claimant and eligibility, expected revision |
| Open/Assigned/Claimed | Reassign | Open/Assigned | Explicit manager permission, current valid target |
| Assigned/Claimed | Accept response | CompletionPending | Current assignee/claimant, valid action/form, revision and request ID |
| CompletionPending | Queue exact response | CompletionPending | Journal delivery responsibility; queue receipt alone is insufficient |
| CompletionPending | Prove canonical committed result | Completed | Exact invocation/outcome/result matches accepted response |
| Nonterminal | Prove runtime retirement | Cancelled | Explicit terminal/cancelled/superseded state or sticky deletion tombstone, after completion check |

Completed/Cancelled are read-only. Accepted responses never change. Repeated accepted request IDs return the existing receipt only for the same actor/action/payload; conflicting replay is a conflict. Missing rows/bookmarks are uncertainty and preserve owed work. Invalid public stimuli fail before wait consumption. Fresh reconciliation is bounded and partition scoped with fair continuation so one unavailable row cannot starve later work.

Form version: tenant, stable form ID, immutable version ID, display name, provider ID/version and resolved JSON. Updates create a new version; existing versions stay readable while referenced. Basic fields: exact case-sensitive key, label, kind, required/read-only, optional min/max length or number, choices and help text. Reject duplicate keys/choices, unsupported kinds and malformed constraints. Prefill/response must be objects. Required waiver affects absence only; supplied types/ranges/choices and read-only rules always apply. Plain text descriptions render escaped.

Task queries apply scope, tenant and authorized assignment/queue predicates before bounded ordering/paging. Cursors bind query, scope, tenant and actor; each page rechecks current group eligibility, so changed membership cannot disclose stale-page records. Replicas share an explicit cursor key or a domain-separated durable recovery key. No global counts before authorization. Current principal/user/membership/role and permission checks happen per operation; persisted history records prior authority, not ongoing access grants.

## Reviewed durability refinements

The staged-to-actionable transition captures `StimulusResumeRecipient.From(IBookmarkStateStore.ObserveAsync(...), execution, scope)` once, including physical replacement generation. ID-only matching is insufficient. Dispatch carries this expected recipient through `BookmarkResumeDispatchRequest` into `RuntimeResumeBookmarkCommandPayload.ExpectedBookmarkResume`; the selected provider atomically compares generation at initial consumption. Require `SupportsExactBookmarkResume` and `CanCommitExactBookmarkResume`; unsupported compositions refuse actionability. The resume-validation guard checks immutable journal response identity in addition to this transactional condition.

While a nonterminal task needs canonical activity/result evidence, the provider enforces a workflow deletion/retention fence atomically against task creation. Service-layer checks alone are insufficient. Deletion returns the existing DependencyRetained conflict. Test a crash after activity completion, before journal acknowledgement, followed by terminal deletion and recovery: completion evidence must survive and the task must become Completed. A deletion tombstone cannot substitute for erased unacknowledged completion evidence.
