# Editorial composition and human review

**Status**: Proposed finite M4 product example, FR-007–FR-008/US2, Program A4/A5/A14.
This example precedes M6 reusable [team patterns](teams.md) and protected tool approval.

## Bounded revision in both process models

Copywriter accepts Brief and optional PreviousArticle/ReviewComments, returning article.
Proofreader accepts Article, returning verdict (`approved` or `changes_requested`) and comments.
Publish exact contracts/versions; native Sequence/Flowchart and BPMN bind the same activities and ports.
Ordinary Elsa control flow owns the loop. No model prose, hidden session or specialist runtime chooses
the next branch.

Round1 writes from Brief. A changes_requested verdict passes that round's accepted article and comments
back to Copywriter. The next review receives only the new accepted article. MaxRounds3 includes the
initial attempt: at most two rewrites, six specialist calls. Check remaining budget/deadline before
each call; no inferred budget reset per rewrite. At the third rejection, take an explicit Exhausted
process branch with the latest article/review available to a permitted person; do not report approval.
Invalid reviewer output faults before routing. Cancellation, model failure and unknown outcome retain
their exact native incident/disposition rather than becoming a favorable verdict.

## Exact human content decision

An approved model verdict reaches the finite `ReviewEditorialContent` native stateful activity, also
usable as a BPMN bound child. It suspends through existing native bookmark/checkpoint machinery with
the exact tenant/owner, execution/activity occurrence, round, article hash and wait identity persisted.
This feature supplies one content-review page and permission-gated decision endpoint; no general task
inbox, user-task platform or second approval database is implied.

The page resolves an exact wait under existing execution/content permissions. It displays the pinned
article, review comments, round and pending state. A permitted reviewer chooses Approve or Deny and
may add a comment. Deny takes the explicit Rejected branch; it does not publish the article or silently
start a new model turn. An author may later start a distinct explicitly requested revision process.
Approve takes the approved terminal branch; the example itself performs no external publication effect.

The decision endpoint rechecks the current review permission and exact pending wait/article hash.
Caller-supplied actor IDs, requestedBy metadata or arbitrary Event payloads confer no authority.
It creates an authenticated encrypted envelope through existing Secrets `ISecretValueProtector`,
with an explicit codec/type discriminator, tenant/owner, execution/occurrence/wait, round/article hash,
server-derived actor, decision/comment, request ID and expiry. Envelope validation checks every bound
field after decrypt; generic protected secret strings with another purpose are refused. Retain codec
readers and keys through the maximum pending-delivery/recovery horizon. Never expose the envelope or
accept caller-authored envelopes through the content decision API.

Dispatch targets the exact saved bookmark using existing `IBookmarkResumeDispatcher` and native
idempotency/source receipts. Always supply explicit PayloadType and ProviderId so native TriggerDelivery
identity exists; do not rely on undeclared-delivery fallback. Register `SpecialistEditorialResumeGuard` as an inline
`IEventHandler<ActivityResumeValidating>` following the existing DispatchWorkflow guard. This event
runs before the initial trigger claim. It verifies envelope purpose/scope/content and current actor
permission/expiry before the wait can be consumed. An unavailable dependency leaves resume owed;
invalid/stale/foreign decisions must not complete or destroy the valid pending review. Validation
only inside `ResumeAsync` is too late: native initial claim already deletes the bookmark.

Native admission atomically consumes the exact wait and retains its immutable claimed decision input
and attempt. Native completion later checkpoints the typed decision result and continuation; these
are distinct commits. Recovery verifies the exact claimed delivery identity and protected content,
then finishes the admitted decision without a new caller authorization or another model call. Expiry
or later permission revocation cannot reinterpret an already admitted historical decision. The guard
runs on claimed replay too, so its replay path must distinguish that exact durable claim from fresh
admission and must refuse substituted/conflicting input. Match the running state/claim metadata to the
sole open attempt, its TriggerDeliveryId to the typed payload delivery, and the exact Consumed retained
delivery provider/type/dedup/payload to the incoming input. The runtime separately fences the owning
work item before entering this replay path. No generic Event activity or new generic
checkpoint API is needed. Prove both claim-before-crash and completion-before-ack boundaries.

Same request ID and same decision replays the native accepted receipt. Conflicting content under
one request ID is a conflict; a different late decision after consumption is already resolved, not
approval of another wait. A crash after decision dispatch but before HTTP acknowledgement is reconciled
from the native receipt; it cannot resume twice. A changed article/new round requires a new wait.
Revoked authority before admission refuses approval; an already admitted decision remains historical
fact. A concurrent workflow cancellation is settled by native checkpoint fencing, never resurrected.

This is content approval, separate from #2912's production tool/effect authorization. No content-review
decision grants tool permission or proves an external effect's idempotency.

## Required implementation receipts

Both native and BPMN save/reload/publish/run journeys must force one rejected model review, carry the
exact article/comments to a successful rewrite, and exercise three-round exhaustion. Pause human
review, restart the host, then approve and deny in separate runs. Force acknowledgement loss, duplicate
delivery, changed article, foreign scope, expired/tampered envelope, revoked authority, late decision
and cancellation races. Assert one logical parent continuation and zero approved terminal result on
denial/refusal. Browser evidence includes exact content/round, keyboard decision, pending/conflict/
resolved state and refresh/restart; M5 may refine presentation but M4 owns the functioning review path.
