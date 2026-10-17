# Evidence milestone acceptance audit

17 October 2026. Research disposition against Program #2892. Passing an isolated witness does not establish production readiness. Rejected or unimplemented capabilities are recorded explicitly, with follow-up ownership.

| Task | Requirement | Evidence / disposition |
|---|---|---|
| #2895 | Current source and ownership audit | Complete at inspected Foundation SHA; source baseline preserves red-main and parked-migration context without treating them as research blockers |
| #2896 | Activity overlap, Sequence order, component-owned joins | Passed held-worker and continuation tests; 111 assertions, 43 process identities, four negative controls; root rerun and review |
| #2896 | Partition batches, bounded buffers and recovery | Passed one execution/multiple cursors, aggregate record admission, real worker kill and confirmed-exit reservation release; byte/multi-host bounds deferred to #2908 |
| #2896 | Dispatch/effect/result crashes, duplicates, cancellation, scope and pins | Passed named local tests. Destination idempotency remains necessary; work cancellation is not terminal model cancellation |
| #2898 | Real pinned interpreter, restart and event races | Root final host verification passed; source-pin exception used, released production parity prerequisite is not claimed satisfied |
| #2898 | Recoverable ETL service task and outcomes | Success, business error, fault and interrupting cancellation paths passed; partial provisioning and both result-delivery crash windows recovered. Separate stores are non-atomic; fault/cancel leave experimental ETL Running with cancelled work |
| #2898 | Embedded/direct parity, tasks/incidents/operator actions | Persisted Sequence-style child traces match; handles and modeled events validated. Arbitrary alteration is rejected by the interpreter host. Research disposition: defer supported alteration/migration matrix to #2907, align retry with #2747; no full BPMN conformance claim |
| #2900 | Built-in/Orleans comparison and worker concurrency | Same policies/definitions produce matching normalized semantics and effect payloads; held Flowchart workers overlap; one coordinator grain per execution |
| #2900 | Membership, capability placement, failover and fencing | Two-silo kill/recovery passed, stale writes rejected. Length-prefixed key plus CR/LF scope rejection addresses review collision. Cleanup and 21 source/48 archived log hashes verified. Network partition/multi-host storage remain untested |
| #2900 | Provider cost and default recommendation | Qualitative operations/dependency comparison only. Keep built-in compatibility baseline; qualify Orleans optional adapter. Proto.Actor untested; no performance winner claimed |
| #2901 | Independent packaged semantics and generic registration | Actual packed NuGet DLL consumed without runtime engine project reference; finite positive-rule fixed point on unchanged WorkProof; root 29 assertions passed |
| #2901 | Pins, recovery, invalid states and cancellation | Package swap, schema/host mismatch, fabricated trace and invalid status rejected. Work cancellation tested; model cancellation explicitly requires a revised contract under #2905 |
| #2913 | Actual MAF behavior versus native candidate | Released 1.25.0 package probe and separate deterministic native proof; source/package provenance and in-memory checkpoint limit recorded |
| #2913 | Sequence/composite, parallel roles, review barrier, feedback | Root final 142-assertion run passed. Private/shared/versioned resources, accepted decisions/configuration, approval race and effect-before-ack replay covered |
| #2913 | Budget and route boundary review | Budget admission corrected before dispatch; 0–7 boundaries and durable budget-4 zero-pending-result check passed. Exact tool invocation negative test passed. Fixed units are not live token/cost bounds |
| #2904 | Reviewed recommendation, dispositions and governance | Independent review completed; model/composition/provider boundaries and cancellation revision explicit. ADR 0031 amendment, proposed ADR 0063, EF and one membership authority preserved. BPMN kernel convergence remains an integration hypothesis |
| #2904 | Next actionable production slice | Lifecycle/offload contract spec scenario, seams, acceptance, rollout and rollback defined under #2905; production implementation is not part of research completion |
| #2911 | Interactive architecture and recovery explanation | Five model lenses, nested composition, actor/worker/data boundaries, zoom and five labeled simulations implemented; evidence links/revision retained |
| #2911 | Accessibility, responsive and standalone behavior | Real-browser keyboard controls and 320/768/1024/1440 checks passed. No external assets/network APIs/motion loops. Direct-file/offline browser test blocked by URL policy; static dependency audit supports portability, not a claimed disconnected run |

## Remaining product obligations

The research recommends the unifying direction with explicit revisions; it does not freeze experiment DTOs as production contracts. #2905 owns lifecycle/outcome and compatible EF/runtime integration; #2907 owns the BPMN product/alteration support matrix; #2908 owns connector/data/schema and distributed byte-admission work. Existing #2735 and #2747 retain their migration/retry ownership. Agent production work should extend the current Agent infrastructure with durable decision/approval and resource contracts rather than create a parallel stack.
