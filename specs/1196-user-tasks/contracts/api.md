# User Tasks API and integration contract

Expose capability `elsa.api.user-tasks` with relations for inbox/detail/claim/release/complete/reassign/forms/targets. Clients discover routes from capability metadata. All operations use the current normalized canonical Elsa user and tenant; a request cannot supply either authority. Resource-level denial is indistinguishable from missing. Missing identity is 401; permission denial 403; stale/terminal/conflicting request 409; schema/response validation 400 with safe field errors; unavailability before durable acceptance returns a retryable error. Once the response is durably accepted, runtime unavailability preserves its receipt and returns pending (202), never completed.

- GET tasks: view `mine`, `queue`, `history`, or manager-authorized `all`; bounded cursor and search, authorized rows before paging.
- GET task by ID: permitted content, pinned form/prefill, allowed operations, current revision and safe history.
- Task responses (inbox rows, task by ID and every mutation result) name the people they store: `assigneeName`, `groupName`, `claimantName` and each history entry's `actorName`, read from the task tenant's directory for any reader, or `null` when the directory has no match (#4198).
- POST claim/release: task ID + expected revision + stable request ID. Claim requires current eligible group; release requires claimant.
- POST complete: task ID + expected revision + stable request ID + action + exact case-preserved response object. Result includes accepted/pending/completed receipt; 202 is pending, never a false completed result.
- POST reassign: task ID + expected revision + request ID + exactly one assignee/group ID. Explicit manage authority; current target validation.
- GET/POST immutable form versions: read/manage permission; writes create new versions, never mutate an existing referenced version.
- GET assignment targets: task-author/manage scope, finite active tenant user/role choices without exposing credentials or broad identity-admin data.

Host integration contracts: conditional task journal with scope-safe authorized paging/recovery; lifecycle operations; explicit current eligibility resolver; provider-identified form materialization/validation. Provider ownership is unique. A custom app uses the same HTTP lifecycle, and an external form provider supplies a safe structured display/response contract plus Studio renderer contribution. No arbitrary external HTML/script execution. Published/native task results contain task ID, completion ID, selected action and submitted response; validation before resume binds all fields to the exact committed wait.
