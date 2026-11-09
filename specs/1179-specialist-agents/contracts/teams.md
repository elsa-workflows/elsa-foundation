# Team configuration and completion contract

**Status**: Proposed logical schema and finite initial patterns. Public C# signatures remain provisional.
FR-018–FR-019, US5 and Program A9/A11 own implementation; [execution](execution.md) owns turn/effect
boundaries and [the state model](../data-model.md) owns acceptance/receipt authority.

## Authored and compiled shape

| Authored field | Meaning and validation |
|---|---|
| Pattern | Sequential, concurrent cohort or bounded refinement; no arbitrary runtime graph |
| Participants | Unique stable role/member key and an exact published activityVersionId. Publishing resolves/pins contract, template/hash and runtime requirements; recommendation changes do not retarget it |
| Input mappings | Explicit typed JSON-pointer sources from team inputs, declared prior/previous-round results or literals. Validate existence/type/nullability and causal availability; no unrestricted implicit transcript |
| Result mapping | Declared team public members mapped from exact terminal participant results or an explicit cohort object/array aggregation. Required outputs cannot be invented from failed/missing members |
| Continuity/resource policy | Fresh member calls by default; explicit per-member private conversation and versioned resource bindings only where authorized. Team message channels are not SDK sessions or shared memory |
| Completion predicate | For refinement, one declared validated reviewer field equals an allowed completion value. For sequential/cohort, all required terminal steps/members have accepted results |
| Limits | Required finite maximum rounds/cohort membership plus parent call/tool/time/token limits. Admission must reserve an entire intended cohort before dispatch |
| Failure policy | Initial policy: fail the team on a required member fault/timeout/cancellation; cancel remaining responsibility and retain exact accepted/unknown dispositions. No implicit partial-success aggregate |

Publishing uses the normal dependency discovery/compiler: every participant is an exact dependency
occurrence, and its required inputs/result projections must satisfy mappings. Cycles/future-result
reads, duplicate role keys, mismatched versions, unavailable readers and insufficient minimum-cohort
budget refuse publication. Reusing one participant in several rounds creates distinct round invocation
identities; changing roles, mappings, predicates or limits changes the team artifact hash.

The compiled descriptor includes the pattern, exact dependency closure, typed mappings, cohort/round
membership and completion/failure policy. Native structural execution owns its state, scheduling,
barriers and parent completion. It uses the existing checkpoint participant for a typed final result.

## Sequential pattern

Steps run in declared order. Each step receives only mapped input and explicitly permitted resource/
message context. The next step begins after the prior required result is accepted; a failed step does
not pass an empty object as if it succeeded. Result mapping may pass through the last step or construct
a declared object from named accepted earlier results. Parent continuation occurs once after the final
team result checkpoint. Native/BPMN parents share the same team activity contract.

## Concurrent complete-cohort pattern

Declare the full required cohort before dispatch. Allocate one stable round ID and one member invocation
per role before any worker starts. Inputs are team/literal/prior-round sources, not a sibling's unfinished
result. Reserve capacity for the whole cohort; do not start a subset that cannot reach its barrier.

The join waits for every intended member's accepted result, keyed by role + round + invocation/receipt.
Duplicate identical receipts are idempotent; conflicting receipts fault; late prior-round receipts do
not enter the next cohort. Arrival order cannot change output ordering or membership.

Aggregation is deterministic: object-by-role, or an array in declared role order. Preserve source identity
internally and expose only declared public fields. Initial failure mode is fail-whole-team: no normal
business result if one required member fails/times out/cancels. Cancel outstanding native responsibility;
accepted journal turns remain accepted with their true delivery disposition. Potentially dispatched
unknown members retain an uncertainty incident requiring reconciliation, never “failed with no effect.”

Worked cohort:

```json
{
  "pattern": "concurrent-cohort",
  "participants": [
    { "key": "facts", "activityVersionId": "fact-review-v1" },
    { "key": "style", "activityVersionId": "style-review-v2" }
  ],
  "inputs": {
    "facts.Article": { "kind": "teamInput", "path": "/Article" },
    "style.Article": { "kind": "teamInput", "path": "/Article" }
  },
  "aggregation": { "kind": "object-by-role", "roles": ["facts", "style"] },
  "join": "all-required",
  "failurePolicy": "fail-whole-team",
  "limits": { "maxRounds": 1, "maxModelCalls": 2 }
}
```

The example cannot complete after only the facts reviewer. If style times out, there is no successful
two-review object; the parent receives the declared fault/incident path and the exact partial/unknown
evidence remains inspectable under permission.

## Bounded refinement pattern

Two roles are initially supported: writer and reviewer. Each round runs writer, then reviewer on that
round's accepted article. A reviewer result must satisfy its exact schema, e.g. verdict is `approved`
or `changes_requested`, with comments. A declared equality predicate decides completion; model prose
outside that field cannot redirect execution.

Round1 receives Brief, with no previous article/comments. A rejected review maps that accepted article
and comments into the next writer call. Maximum rounds includes the initial attempt: MaxRounds3 permits
at most two rewrites. Stop earlier on approved predicate, fault/unknown/cancel or an exhausted parent
budget. Reserve each full writer+reviewer round before dispatch. Exhaustion does not publish a success
that omits required output; emit the explicit exhausted/manual-review domain path.

Worked writer/reviewer mapping:

```json
{
  "pattern": "bounded-refinement",
  "participants": [
    { "key": "writer", "activityVersionId": "copywriter-v1" },
    { "key": "reviewer", "activityVersionId": "proofreader-v1" }
  ],
  "writerInput": {
    "Brief": { "kind": "teamInput", "path": "/Brief" },
    "PreviousArticle": { "kind": "previousRoundResult", "participant": "writer", "path": "/article", "initial": null },
    "ReviewComments": { "kind": "previousRoundResult", "participant": "reviewer", "path": "/comments", "initial": null }
  },
  "reviewerInput": {
    "Article": { "kind": "currentRoundResult", "participant": "writer", "path": "/article" }
  },
  "completeWhen": { "participant": "reviewer", "path": "/verdict", "equals": "approved" },
  "result": { "article": { "participant": "writer", "path": "/article", "round": "accepted-final" } },
  "limits": { "maxRounds": 3, "maxModelCalls": 6 },
  "failurePolicy": "fail-whole-team"
}
```

This is a logical example, not a frozen JSON API. Both participants must declare the mapped optional/
required fields; publication resolves their real immutable identities/hashes. Fresh member calls plus
explicit prior outputs are sufficient; sharing a single SDK session between different specialist
versions is not permitted. Human editorial approval is separate from the model completion predicate.

## Recovery, visibility and editor

Persist current pattern/round/cohort, required member identities, accepted receipt references, input/
resource observations, budget reservations and completion decision before issuing dependent work.
Replay does not ask accepted members again or re-query changed knowledge. Preserve native/source
fences, partial unknown outcomes and one logical parent result through cancellation and restart.
Coordinate production decision/effect/budget interfaces with #2912; no second generic ledger/scheduler.

The editor is a form: choose pattern; select exact participant versions; bind compatible inputs;
choose declared result aggregation/completion field; set limits; review failure policy/resources;
test/publish normally. Invalid/future/hidden-context mappings are not offered as valid choices. Show
the resolved cohort/order and the maximum attempts, with a text explanation of failure/unknown behavior.
The [team wireframe](../design/studio-wireframes.html) illustrates those fields. No free-form graph is
needed to express these initial patterns.
