# Decision log: Studio expression developer experience

## 2026-10-02 — Retain the existing editor and module boundaries

**Decision:** Keep CodeMirror behind Studio's engine-neutral editor contract. Workflows continues to own authoring scope; each Expression Module owns syntax and language projection; Foundation providers own runtime-compatible metadata and authoritative diagnostics.

**Rationale:** The baseline already implements the required extension points and session model. Current gaps are composition, runtime parity, assistance depth and presentation, not a missing editor engine.

**Consequences:** Improvements extend Studio spec 094, Foundation spec 143 and ADRs 0002/0006. Monaco replacement is out of scope.

**Revisit:** Only if a future accepted requirement cannot reasonably be delivered behind the current editor contract.

## 2026-10-02 — Treat normal-host proof as the first milestone

**Decision:** Verify one real persisted workflow through rebuilt matching Studio and Foundation hosts before deepening language services.

**Rationale:** Studio #546 fixed a real discovery defect that component fixtures could not expose. Producer and consumer correctness must be proven together.

**Consequences:** Historical fixture results remain useful regression evidence but do not close milestone 1.

**Revisit:** No planned revisit.

## 2026-10-02 — Make installed text-syntax readiness explicit

**Decision:** Resolve runtime Expression Type, Studio editor Contribution and Foundation tooling provider as separate capabilities and show exact degraded states. Specialized non-text editors remain distinct.

**Rationale:** Installation of one layer does not prove the other layers are present or compatible.

**Consequences:** Conformance covers independent composition, missing adapters/providers, permissions and version incompatibility.

**Revisit:** When a new text Expression Type adopts the contract.

## 2026-10-02 — Runtime profiles own language metadata

**Decision:** JavaScript grammar/globals and Liquid filters/tags come from immutable metadata owned by their runtime modules. Authoring never receives runtime values or evaluation contexts.

**Rationale:** The editor must not advertise syntax or callable capabilities the same host cannot execute.

**Consequences:** Default Fluid catalogs and broad browser-side JavaScript grammar are insufficient authorities.

**Revisit:** When a runtime profile deliberately expands its supported syntax or extensions.

## 2026-10-02 — Defer deeper JavaScript language-service adoption

**Decision:** Merge baseline local completions and align runtime grammar now. Evaluate a worker-based deeper service in a bounded spike before adopting a dependency.

**Rationale:** The spike can test inference quality, runtime declaration control, load cost and accessibility without blocking baseline proof.

**Consequences:** Milestone 3 may adopt or reject the service based on evidence; either outcome must still meet the product acceptance criteria.

**Revisit:** Before milestone 3 implementation begins.

## 2026-10-06 — Remove native VoiceOver verification from delivery acceptance

**Decision:** The product owner explicitly removes actual current-head Chrome+VoiceOver and Safari+VoiceOver observation from Program #2310 acceptance. Record both as **SKIPPED BY OWNER DECISION — NEVER PASSED**. This is a scope amendment, not a test pass, automated substitute or retrospective approval.

**Rationale:** The owner chooses to deliver the remaining scope without waiting for hands-on native announcement observation after repeated observation limitations. The existing automated and native-preparation checkpoints retain their original evidence boundaries.

**Consequences:** Current-head screen-reader behavior remains unverified and must be a final delivery limitation. Historical July baseline evidence is not current program acceptance. Keyboard, automated scoped accessibility, source/cursor/undo, runtime parity, technical/review/integration and required post-merge main gates remain mandatory. Final explicit owner acceptance precedes Ready and dependency-ordered merging; no deployment or package publication is authorized.

**Revisit:** A later owner-approved native accessibility verification task. Do not relabel the skipped gate as passed when closing this program.

## 2026-10-06 — Authorize bounded preview artifact publication

**Decision:** The owner explicitly authorizes preview NuGet/npm package publication only to feedz.io. The owner's subsequent clarification separately authorizes Docker image publication to any endpoint, including Docker Hub, GHCR or Azure ACR in the Skywalker ISP subscription. This delivery retains the existing Docker Hub pipelines rather than introducing a registry migration or provisioning Azure resources.

**Rationale:** Required main merges normally trigger preview package and image publication. The clarification removes the prior publication-scope blocker without changing technical gates or requiring Feedz to provide a Docker/OCI endpoint.

**Consequences:** This amends the earlier no-publication boundary for preview artifacts only. Stable releases, deployments, manual publisher bootstrap/forceadvance and Azure provisioning are not being performed. Final explicit owner acceptance still precedes Ready and dependency-ordered merging; required resulting-main gates and public completion records remain mandatory. Native VoiceOver stays skipped by owner decision and never passed.

**Revisit:** A separately scoped registry migration or stable release/deployment requires its own approval and verification.

## 2026-10-23 — Restart the program and ship the permission-hash fix first

**Decision:** The owner restarts Program #2310. The plan (#2367) merges with current `main`, and the
expression-tooling permission-hash fix from #2373 (commit `f81be4be`) ships on its own as a small PR on `main`.
The rest of #2373 and milestones 2 and 3 are restarted separately.

**Rationale:** The program's decisions are the part that cannot be rebuilt from code, so they are preserved first.
On `main`, the permission hash includes per-token claims such as `jti`, `iat`, `exp` and `oi_tkn_id`, so it changes
on every Studio request and the authoring context never stays valid. That is a real defect that does not need to
wait for the rest of the stack.

**Consequences:** The program is active again under the 23 October amendment to
[ADR 0080 D0](../../adr/0080-elsa-4-simplification-decisions.md#d0--focus). #2373, #2378 and #2380 stay open as
the source for later rebuilds; their history predates the move into `Essentials/` and `Studio/`.

**Revisit:** When milestone 1 is rebased onto the new layout.

## 2026-10-23 — Compatibility is not a cost; native strict, converted Elsa 3 keeps its rules

**Decision:** Elsa 4 is unreleased and has no existing data, so compatibility with earlier Elsa 4 behaviour or
stored data is not weighed as a cost in this program. Native Elsa 4 definitions stay strict. Converted Elsa 3
definitions keep Elsa 3's null and undefined behaviour: a `null` sent to a non-nullable input uses the type's default
([#3172](https://github.com/elsa-workflows/elsa-foundation/issues/3172)), and an `undefined` value in a JavaScript
object stays as a `null` key ([#3170](https://github.com/elsa-workflows/elsa-foundation/issues/3170)).

**Rationale:** Owner decisions of 23 October. Strict typing is the safer contract for Elsa 4 authors, while
imported Elsa 3 workflows must keep working without failing at run time.

**Consequences:** Editor help and diagnostics describe the strict native rules by default and must not claim that a
converted definition is wrong for relying on the Elsa 3 rule. Contracts in this program may change freely before
general availability.

**Revisit:** At general availability, when compatibility starts to matter.

## 2026-10-23 — Statement bodies and the Elsa 3 converter are part of the runtime surface

**Decision:** [#3010](https://github.com/elsa-workflows/elsa-foundation/pull/3010) has merged: JavaScript bindings
accept strict statement bodies (explicit `return` and final completion values) as well as expressions, as
[ADR 0083](../../adr/0083-javascript-binding-grammar-is-pinned-at-publish.md) records. The Elsa 3 converter specs
[247](../../../specs/247-elsa3-js-host-functions/spec.md) and [262](../../../specs/262-elsa3-undefined-strict/spec.md)
are implemented. The program's JavaScript grammar, help and diagnostics follow that surface.

**Rationale:** The editor must describe what the runtime executes. The earlier plan assumed expression-only
bindings, and spec 143 FR-024 listed statement-only capabilities as absent.

**Consequences:** "Statement-only capabilities stay absent" no longer applies to statement bodies. Milestone 2's
JavaScript conformance work in #2378 must be rebuilt against this surface instead of rebased as it is.

**Revisit:** If the runtime adds a setting that limits scripts to expressions (#3204 US1).

## 2026-10-23 — Gate milestones 2 and 3 on the #3204 read-model ADR

**Decision:** Milestones 2 and 3 (#2378 JavaScript tooling and #2380 Liquid help) are not rebuilt until the ADR
requested by [#3204](https://github.com/elsa-workflows/elsa-foundation/issues/3204) is accepted. It decides how
scripts read variables: declared parameters (`args.X`) or ambient variables (`variables.X`, `getVariable()`), and
whether the runtime keeps the generated `get<Name>()` accessors.

**Rationale:** #2378 preserves generated-getter naming and builds completion around the ambient model. If the ADR
picks declared parameters or drops `get<Name>()`, that work would advertise accessors the runtime no longer
offers.

**Consequences:** The gate is met only when the ADR is accepted. Until then, milestone work is limited to
milestone 1 and fixes such as the permission hash that do not depend on the read model.

**Revisit:** When the #3204 ADR is accepted.
