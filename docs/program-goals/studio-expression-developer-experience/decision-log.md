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
