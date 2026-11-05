# Studio product journeys and working-state contract

**Status**: Draft ownership amendment under [ADR 0094](../../../docs/adr/0094-agent-definitions-own-authored-content-and-providers-publish-activities.md); design-only. Historical wireframes below retain their original activity-backed illustration until the future Studio task updates it. [Interactive wireframes](../design/studio-wireframes.html)
use illustrative local data, not a real backend/model. FR-001–FR-008/FR-020–FR-022/FR-027 and Program
A1–A5/A10/A12 own the production journeys.

## Navigation and composition

The Agent module owns `/agents` discovery, typed source/public-contract editing, draft revision/conflict/history and publication-intent APIs and permissions. It queries Agent source records, including unpublished drafts, with server-side authorization/filter/count/paging. It can reuse generic controls and shared publication preflight/receipt mechanics without keeping a second editable activity source. Weaver remains the assistant experience.

The native/BPMN picker remains the one activity catalog. Generated Agent activity pages show read-only content and the exact Agent owner/version link. Generic activity APIs cannot independently save Agent-owned content. Agent content authority controls edit, history, fork and configuration-source presentation; activity visibility alone grants no source rights.

Playground/evaluation/inspection identify exact Agent candidates/versions plus derived publication/native identities. Use the existing admitted candidate execution and journal path, with explicit pending/unknown publication recovery. The user-facing inspector leads with public inputs/results and meaningful state; internal model-turn handles remain technical drill-down. Reuse delivered inspection contributions where they fit; no new scheduler, speculative universal tab framework or direct UI-to-model call.

## Finite journeys

| Journey | Required flow and evidence |
|---|---|
| Discover/create | `/agents` → create specialist → name/purpose → supported model connection → public inputs/results → save. Provider jargon is not a required user choice |
| Configure | Instructions, model/capability-aware supported options, public interface and permitted resources. Continuity is a separate explicit capability; memory/knowledge do not imply it |
| Test | Save/identify exact candidate → enter typed/JSON business inputs → fresh default or explicit conversation/revision → run/stop → inspect permitted output, contract checks, usage and status |
| Publish/history | Preflight exact saved candidate → impact/diagnostics/minimum version → review → publish/reconcile receipt → immutable version/history. Editing requires new draft/copy; existing bindings stay pinned |
| Use | Normal activity catalog/native picker or BPMN bound-child inspector → select exact version → bind ports → save/reload → run. Generated ports and expected parent continuation are visible |
| Evaluate | Save input/assertions/rubric case → version the set → choose exact baseline/candidate → run/reconcile/cancel → compare deterministic contract checks separately from model-quality assessment |
| Inspect | Workflow run/activity occurrence → specialist invocation → exact definition/version/model/operation/conversation → permitted messages/calls/approvals/result/usage → parent occurrence/incident/history |

The playground uses production runtime, not a direct UI-to-model client. It reuses generic draft
test-run dispatch/status/reconcile/cancel identity. A draft candidate is immutable for that run;
editing to another revision cannot silently continue its old conversation. Published and candidate
conversation identities are explicit, version/hash-bound and scoped to their admitted content.

## Editor and form states

| State | Behavior |
|---|---|
| Loading/unavailable editor | Stable metadata/authority remains visible; explain missing capability and provide a safe retry/navigation action; do not show an editable unknown payload |
| Valid dirty state | Working changes are local, with explicit save status and exact accepted revision. Revision-sensitive test/publish actions first settle the declared save boundary |
| Invalid local JSON/field | Retain local text, mark the field, show its actionable constraint, and prevent test/publication; never say saved when invalid text was not accepted |
| Save in flight/ambiguous outcome | Prevent conflicting revision actions; reconcile the idempotent operation before retrying a write |
| Revision conflict | Preserve local content, identify the server revision and offer compare/reload or a permitted parallel recovery draft; no silent last-writer overwrite |
| Historical/source-owned/read-only | Disable editing consistently, show exact authority/version, offer new draft/copy/fork only when permitted. Read permission does not imply payload/test/edit permission |
| Connection revoked/unavailable | Retain the saved reference, explain why test/admission is unavailable and offer authorized replacement/revalidation. Do not silently select another connection/model |
| Unsupported/stale capability | Show only valid options/ranges. Saved unsupported values remain inspectable with diagnostics and an explicit repair action; no hidden option stripping |
| Permission denied | Preserve permitted metadata/navigation; hide protected content/actions. Failed backend authorization remains authoritative despite UI state |

Instructions and business data are distinct. Public member keys remain stable independently of labels. The Agent contract is canonical; activity ports and model schema preview derive from it.
Schema preview derives from the Agent contract plus compatible field constraints. Model credentials
are managed in Connections; neither forms nor source/history views expose them. Unsupported sampling/
effort controls are absent or explained, not rendered as universal temperature knobs.

## Invocation and uncertainty states

Fresh test is default. Explicit continuation selects an authorized conversation with exact candidate/
specialist version and expected revision; expose the relevant workspace/owner and history choice.
Overlap conflicts offer refresh/new fresh conversation, not a hidden queue against different history.
Show stage and cancellation availability without claiming that an already dispatched remote request
was stopped. Separate observable streaming from accepted final result; partial tokens are not success.

| Turn/process state | User-visible meaning/action |
|---|---|
| Reserved/dispatching | Exact operation/candidate admitted; stop requests cancellation under declared policy |
| Streaming | Partial permitted output, not accepted; usage may be provisional/unknown |
| Turn accepted and applied | Validated output/session accepted and process checkpoint continued normally |
| Turn accepted, not applied | Accepted conversation history remains; process cancelled/failed before application. Link both decisions and settlement evidence |
| Journal cancelled | No later completion may replace accepted history for this turn; in-flight provider/effect uncertainty is still explicit |
| Outcome unknown | Provider may have received work but no authoritative result was accepted. Block automatic fresh retry; offer exact supported continuation/reconciliation or an authorized fresh/recovery action |
| Invalid/refused/exhausted | Explicit domain failure/incident and safe diagnostics; no successful business result. Link native incident/handler action |

Secret/protected fields and private model reasoning never appear in the timeline. Opaque continuation
tokens are not explained as reasoning or copyable credentials. Copyable technical references use the
existing Studio UI controls. Result access is reauthorized, even when a parent occurrence is visible.

## Evaluations

Sets/cases and runs have independent immutable/revision identities. A run records exact candidate and
case set, connection/model/capability profile, inputs, assertions/rubric, observed output, usage and
execution links. Run the same production specialist path with declared budgets/resource policy.
The initial assertion set covers JSON contract validity, required/member/value checks and explicit
expected refusal; quality rubrics retain evaluator/model/version and observed assessment separately.

Reports keep passed, failed, skipped, cancelled, unknown and pending distinct. Never color an incomplete
case as green or aggregate a rubric score into contract correctness. Comparison does not silently choose
latest versions. Saved cases redact/deny credential or disallowed protected data; content access and
retention use the specialist state policy. Model-based assessment consumes a declared separate budget.

## Team configuration and human review

The seventh wireframe view configures the finite [team contract](teams.md): exact participant versions,
typed causal inputs, result aggregation/completion predicate, limits and failure policy. Sequential,
concurrent-cohort and refinement show their effective order/barrier and maximum attempts.

M4 supplies the generic [human review page](human-review.md) for one exact wait, addressed by its workflow and
activity execution IDs. It displays the pinned content (text or formatted JSON), title, notes and status, and
reconciles pending/resolved/conflicting receipts across reload/restart. It offers Approve and Deny on every pending
review; the server enforces `agent-reviews:decide` and the page reports `Forbidden` without deciding. In M4 the page
is reachable by URL only; the occurrence link from run inspection is T034 (M5). Any model assessment of the content
and the human decision remain separate visible steps.

## Save, history and undo/redo

The Agent editor/API own domain edit permission and commands while composing shared working-copy revision/save and snapshot history. Reused field controls report through its
exact revision callbacks and respect readOnly/historyResetKey. Historical editor receipts remain evidence of their original implementation. Local undo affects working state at that
resource/revision only; it cannot replay across a conflict, selected version or backend connection.
Accepted server writes/publication are not silently undone. The module's input editor and evaluation
case editor declare the same local/save boundary. Native and BPMN workflow edits keep their existing
undo/save behavior, including specialist binding/property edits and reload.

## Accessibility and narrow layouts

Use existing Studio UI controls, labels, dialog focus/escape behavior, tab/arrow navigation, error
association and live status announcements. Every primary journey works with keyboard. At 320px the
page has no horizontal overflow; tab strips/tables may have deliberate local scrolling, forms stack
and actions remain reachable. Focus is retained or moved deliberately after save/error/navigation.
Permissions/status are communicated in text, with restrained color as secondary reinforcement.

The wireframe demonstrates seven views and representative normal/read-only/conflict/unavailable/denied/
unknown/cancellation-race scenarios. Manual prototype checks covered all seven 320px views without page
overflow, read-only field disabling, arrow-key tab selection and the completed/not-applied timeline.
The team pattern selector changes causal mappings, aggregation/barrier and effective limits; it is
illustrative validation rather than a production compiler.
That is design verification only. Real product browser, persistence, undo, authorization and provider
qualification remains required by M5/M7.
