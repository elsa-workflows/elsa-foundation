# Research: current turn context

Baseline: reviewed main `ab46bc22a52aa6e456bca6ac0e803c80904de081`.

## Reuse module admission for current disclosure

**Decision**: Reuse installed context sources, native identity/authorization and host policy when qualifying retained context before provider use. Preserve accepted receipts and operation targets. Let the native authority experiments determine whether re-admission, an explicit refusal or a minimal additional owner hook is necessary.

**Rationale**: `AgentApi` admits context at acceptance. `AgentTurnServices.PrepareAsync` later uses `latest.Context` or `paused.Context` directly. Tool preflight already checks current permissions; that does not establish current context/result disclosure. These are source observations, not yet runtime failures.

**Alternatives**: Trusting stored context forever or client permission claims fails current authority. Provider-specific domain filters violate the Program. A universal resource authorization schema duplicates owning modules.

## Bound individual collection without changing global events

**Decision**: Qualify required/optional failure and noncompletion at existing contribution boundaries. Keep the named Sequential fan-in and one aggregator. Use explicit typed outcomes, deadlines/cancellation and isolated contribution buffers where needed; do not swallow global event failures.

**Rationale**: Client collection awaits sources sequentially and checks abort only before each call; its existing `required` descriptor is unused for unexpected failure. Backend aggregation also awaits sources directly. A noncooperating asynchronous contributor needs a bounded outcome, and late mutations must not reach admitted state.

**Alternatives**: An aggregate timeout alone loses which required evidence is missing. Globally changing event failure delivery exceeds scope and the constitution. Merely passing a cancellation token does not bound an uncooperative promise/task.

## Show admitted evidence, preserve opaque payloads

**Decision**: Use backend admission metadata for displayed context; preserve correlation/source and refresh the per-request active surface. Generic chips may show declared provenance, target/revision and limitations, while domain details remain module-owned.

**Rationale**: The UI currently sets locally collected attachments before backend acceptance; client response mapping ignores admitted message context. Generic chips inspect workflow-shaped selection/catalog fields. Session surface is only sent at creation. Component experiments must distinguish actual gaps from standard host remount behavior.

**Alternatives**: New domain-specific label cases deepen coupling. Displaying local content as admitted misrepresents exclusions. Updating an outstanding invocation's target when a route changes would violate its original binding.

## Preserve lifetime and delivery boundaries

**Decision**: Exercise same-tick shell/direct sends and late completion before selecting a queue or explicit rejection. Each request identity needs an observable disposition. Preserve existing generation guards and client receipt acknowledgements.

**Rationale**: The shell has one incoming-prompt state slot and the hook reads render-captured chat state. Standard sign-out and module reconciliation already unmount the shell; retained custom editor lifetimes need separate evidence rather than an assumed defect.

**Alternatives**: A second orchestration engine or speculative durable prompt queue exceeds this leaf. Silently replacing an incoming prompt fails explicit correlation.

## Qualification boundary

**Decision**: Fresh package-only consumers use verified M1 archives and fresh native state for baseline probes; integrated fixes rebuild supported packages and rerun the independent witness. Capture actual Copilot/Anthropic adapter input through the existing deterministic protocol fixture.

**Rationale**: This proves the actual transport/ownership path without credentials or live-provider charges. Completed proof roots remain immutable; native IAM changes require fresh normal authentication because an older token retains its permission claims.

**Alternatives**: In-process-only assertions cannot prove composed behavior. Reusing mutable published directories contaminates accepted evidence. A provider-protocol pass is not a live-provider observation.

## Observed baseline and selected implementation

Fresh native package consumers on ab46bc22a reproduced both ordinary and receipt disclosure after fresh native owner403 through both actual adapters (protocol evidence, no live provider). Final four-case summary SHA256 `7a5d833c3f053d594426bc07b3037915df1c9fc0fe1a9760240a7e0ece8e5e51`; native-cases `55cdc8872d120ff86e5ccc932f64b48b572a1ef170ac543f16ec312d1101fa61`. Exact owner readback confirmed one effect and no replay. Earlier private-driver Failed attempts remain Failed. [Owning issue baseline](https://github.com/elsa-workflows/elsa-foundation/issues/3439#issuecomment-6539864507).

Client baseline51unique cases yielded36passed/15failed, preserving all34 existing passes. Backend cancellation baseline7passed/2failed plus a separately failing inter-source snapshot race. Fixed focused collection26/26 and client90/90 are bounded evidence pending integrated qualification. Per-message backend surface independently failed on empty scope and passed after current-surface propagation.

Selected disclosure shape: optional fail-closed `IAgentContextProvider.AdmitDisclosureAsync` uses the existing named context fan-in and source identity. The host stamps `ContributorId`; current module/descriptor/schema must match that original source. Successful admission authorizes supplied historical evidence/results unchanged, while returning the current permitted context projection. Receipt truth is never rewritten; unsupported/unknown ownership, current denial or failed disclosure blocks provider entry, including Copilot remote-session reuse. Optional collection failures cannot downgrade this security gate. Native tool identity/version/target for retained result authority is server-stamped from real preparation, not inferred from a reused tool name. Current historical resource provenance is retained with assistant messages.

Collection options add a finite positive per-source deadline (default10seconds), with `Required=true` by default on backend source descriptors. Optional faults/deadlines are visible outcomes; required unexpected faults remain domain-wrapped failures. Named Sequential publisher semantics remain unchanged. Returned source data is snapshotted before another source can run.

This decision remains under implementation and independent review. Full affected tests, native package extension proof and main verification are still required before #3439 completion.


## Integrated behavior evidence before package qualification

All316Agent cases passed with zero skips after the result boundary, exact-source routing, policy projection and provenance repairs. Thirteen boundary cases had a12failure/1control baseline and passed after repair; two legacy/custom-store cases failed before repair and passed after. Two extension-contract cases also failed before repair: the review digest omitted the disclosure source, and a generic same-resource restriction rejected a module-owned collection-to-new-item receipt. The digest now binds that source; generic identity validation preserves the existing receipt contract and lets the module own resource transitions. Original historical labels/routes are not identity.

The integrated frontend at main a5d4 plus this feature passed710Hosting and2803Workflows cases with exact discovery reconciliation, supported Studio `pnpm typecheck`, frozen install and scoped lint (zero errors; six preexisting App warnings). Earlier raw standalone typecheck failures remain distinct invocations; the supported check is the current evidence.

The package run must retain all38M1 cases and additionally pass four native authority-withdrawal cases through both actual adapters. No native fixed-source result is claimed by these component results. The branch is still under implementation/qualification; independent review and resulting-main gates remain mandatory.

## First integrated package run and independent review

The immutable `dd5d6d1a4` package run failed in the initial Copilot resource journey after 120 stages; it never completed the original 38 or additional 4 cases. The real backend trace showed `AgentSseJsonContext` lacked `AgentContextContribution` metadata, resetting the SSE stream after Started. A focused test reproduced the same serialization failure; registering the payload then passed the full 317-test Agent suite. This is a component repair, not native completion. The failed report/root is retained unchanged, both owned hosts exited 0 and their private runtime caches were empty.

Independent round 1 identified three concrete findings: delegated callbacks reused captured scopes after a successful narrower module projection; ContextAdmitted erased current-request client limitations; and the compatibility note omitted the public workflow provider constructor break. Repairs require causal checks, full affected suites and a fresh immutable native run. Review/qualification remain open; no terminal status is claimed.

The current-tool regression reproduced five failures and four passing controls across nine cases: Copilot and a host batch reused a withdrawn scope; all three provider shapes followed a changed target/revision. Rechecking module admission, the current tool set and the accepted resource binding before dispatch passed all nine, including the three same-target/edit-retained positive controls. These are deterministic actual-adapter/host-loop tests; package runtime qualification remains open.
