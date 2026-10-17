# Evidence milestone acceptance audit

Interim audit, 17 October 2026. Source: current GitHub issue bodies for Program #2892, refreshed during root review. This prevents narrower passing experiments from silently replacing the agreed scenarios. It is not final completion certification.

| Task | Requirement | Evidence / disposition |
|---|---|---|
| #2896 | Activity overlap, Sequence ordering, component-owned joins | Passed held-worker and semantic-continuation tests; root rerun and independent review |
| #2896 | Independent partition batches, bounded buffers, checkpoint recovery | Passed one-execution/multiple-partition witness plus explicit aggregate admission, real worker kill and confirmed-exit reclamation |
| #2896 | Dispatch/effect/result crashes, duplicates/conflicts/late results, cancellation races | Passed named assertions and four negative controls; external destination idempotency remains explicit |
| #2896 | Context/version isolation, reproducible evidence, review | Committed evidence with source hashes and process IDs; task closed as bounded research only |
| #2898 | Compatible pinned real interpreter, frozen production contracts | Source pin tested under owner exception; released parity prerequisite is not claimed satisfied; production contracts untouched |
| #2898 | Restart fork/join; both timer/human race orders; late callbacks | Root-verified host witness |
| #2898 | Service task invokes recoverable pipeline; success/business error/fault/cancel mapping | Additional bridge witness assigned; initial host work ledger alone is insufficient |
| #2898 | Human task/incident commands; validated alteration; native/embedded comparison | Task/race/incident and persisted Sequence-style comparison verified. Matching modeled-message delivery is not arbitrary alteration; supported command scope and explicit unsupported behavior must be resolved in final disposition |
| #2900 | Built-in versus Orleans, unchanged definitions, serial orchestration and concurrent workers | Orleans first run alone is insufficient. Direct comparison and held-worker overlap added to assigned scope before root acceptance |
| #2900 | One membership view, capability placement, lost-route recovery, stale commits | Real two-silo witness implemented; canonical actor-key alias rejection added after review; final root rerun pending |
| #2900 | Provider costs and limitations | Qualitative dependency/operational comparison required; no framework default or performance claim from this spike |
| #2901 | Independently packaged different semantics, no existing-engine edits | Finite positive-rule inference to least fixed point; separately packed assembly; unchanged WorkProof APIs |
| #2901 | Pinned artifact/state/package, host capabilities, restart/results/cancellation | Root final 26 assertions passed, including coordinator and worker package-swap rejection. Work cancellation only; terminal model cancellation not implemented |
| #2901 | Independent review and substrate edits recorded | Root reviewed/fixed pin gaps; cross-review pending. Registry/contracts exist inside experiment, no shared substrate edits required |
| #2913 | Real MAF semantics versus conceptual inspiration | Root-verified actual 1.25.0 package; in-memory checkpoint limits recorded |
| #2913 | Typed authored composition, routes/resources, private/shared memory, versioned observations | Candidate proof being expanded to full scenario; routing and conflicting-message gaps found in review |
| #2913 | Enclosing Sequence, parallel Researcher/Specialist, Reviewer barrier, bounded feedback | Full scenario assigned; separate mailbox/round tests alone do not satisfy this criterion |
| #2913 | Persisted decision/config/usage, exact approval race, exhausted budget, effect replay, parent continuation | Full deterministic scenario assigned; no live LLM claim; initial 48-assertion subset not accepted as complete |
| #2904 | Assess all witnesses and accept/revise/reject boundaries | Working recommendation exists; combined review pending |
| #2904 | Reconcile ADRs 0031/0063/0073/0078 and current constitution | Explicit table added: 0031 concurrency ceiling requires amendment; 0063 remains proposed; EF and single membership authority preserved; provisional sections identified |
| #2904 | Define identity/work/security/operator/provider contracts; next production slice | Candidate boundaries documented; final decisions and Speckit-ready slice pending material witness outcomes |
| #2911 | Actor/component/worker/data-path views; recovery controls; linked revision | Not yet implemented; follows combined evidence review |
| #2911 | Keyboard, reduced motion, offline, browser QA, durable HTML | Not yet implemented; final goal cannot complete without these artifacts and checks |

## Research completion versus product completion

The goal is to validate and recommend the foundation, not ship all BPMN/ETL/agent product features. A rejected or unsupported capability can be a useful research outcome only when explicitly tested or source-grounded, assessed in the recommendation and assigned a concrete follow-up. It must not be disguised as a passed implementation requirement. Any change to the research acceptance itself must remain visible in the issue record.
