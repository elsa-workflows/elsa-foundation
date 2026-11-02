# Editorial sample: bounded revision with human review

**Status**: M4 sample, FR-007–FR-008/US2, Program A4/A5/A14. The product contract is [human review](human-review.md).
This example precedes M6 reusable [team patterns](teams.md) and protected tool approval.

**Owner clarification (2 Nov 2026):** specialists are user-defined agents. Authors define them through Studio or
configuration, in the spirit of Azure AI Foundry agents. Copywriter and Proofreader are only an example of what a
user might define. The product therefore ships no editorial-specific activity, permission, endpoint or page. It
ships a generic native `HumanReview` wait for any content, typically an agent's output, plus its permission-gated
decision path. The revision loop below is a sample, built from user-authored specialists and ordinary control flow;
any routing helper it uses is sample code. The sections below keep their editorial wording as the sample's
acceptance scenario. Read "article" as the reviewed content, and "round/article hash" as the content hash of the
exact pinned wait.

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

## Human decision

An approved model verdict reaches the generic `HumanReview` wait defined by the
[human review contract](human-review.md). The sample passes the latest article as content and the Proofreader's
comments as notes. Deny takes the explicit Rejected branch; it neither publishes the article nor silently starts a
new model turn. An author may later start a distinct, explicitly requested revision process.

## Required implementation receipts

In M4 the native and BPMN runs are composed executables over really published specialists, and the BPMN form also
crosses export, an import that refuses unbound work, and exact rebinding. Saving, reloading and publishing the
process as a workflow definition is proved in M5 (T036); binding-preserving interchange is T063. Both forms must force one rejected model review, carry the
exact article/comments to a successful rewrite, and exercise three-round exhaustion. Pause human
review, restart the host, then approve and deny in separate runs. Force acknowledgement loss, duplicate
delivery, changed article, foreign scope, expired/tampered envelope, revoked authority, late decision
and cancellation races. Assert one logical parent continuation and zero approved terminal result on
denial/refusal. Browser evidence includes exact content/round, keyboard decision, pending/conflict/
resolved state and refresh/restart; M5 may refine presentation but M4 owns the functioning review path.
