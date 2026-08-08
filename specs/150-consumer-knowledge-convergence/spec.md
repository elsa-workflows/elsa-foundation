# Feature Specification: Consumer-Knowledge Convergence

**Feature Branch**: `1165-consumer-contract-fragments` *(continues the spec-149 delivery branch and PR #1192 rather than forking a new one, so the maintainer's consumer-workspace validation loop stays on a single branch; the Speckit branch hook was deliberately skipped for that reason)*

**Created**: 2026-08-08

**Status**: Draft — converges RFC #1191 into a shippable consumer-knowledge product. Supersedes the "steps 1–2 only" scope fence of [spec 149](../149-consumer-contract-fragments/spec.md).

**Input**: Maintainer specification derived from three rounds of a six-session, three-arm benchmark (github-only / contracts-only / contracts+skills). The benchmark is the specification input and the acceptance bar; requirements below are transcribed from measured gaps, not re-derived.

## Why this is not another increment

Three benchmark rounds produced:

| Arm | Mean tokens |
|---|---|
| github-only | 163,422 |
| contracts-only | **209,190** (28% *more* than having no contracts) |
| contracts+skills | 171,350 |

Correctness improved with each round (`authoredVia` produced first-try intrinsic placement; nobody guessed `ResponseMode` wrongly after enum members shipped) but **cost did not move**, because the residual expense is *exploration for what the contracts do not cover* — not failure loops. A half-published contract is worse than none: it answers the structurally hard questions authoritatively, induces reliance, then goes silent on the load-bearing behavioural ones. Closing the enumerated gap list is therefore the whole job, and partial closure does not move the metric.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Prove a workflow works without a source clone (Priority: P1)

A consumer agent with no source checkout, given only `docs/contracts/` and `docs/consumer-guide/`, authors a workflow (HttpEndpoint → SetVariable → JS expression → WriteLine → WriteHttpResponse), publishes it, executes it, and asserts on the result — without probing undocumented endpoints and without reading `elsa-foundation` source.

**Why this priority**: this is the acceptance bar. Every other story is a component of it.

**Independent Test**: re-run the three-arm benchmark; contracts-only mean at or below the 163,422 github-only baseline.

**Acceptance Scenarios**:

1. **Given** only the published artifacts, **When** the consumer needs the publish/execute/instances/activity-executions/value-evidence endpoints, **Then** it finds them published (route, verb, status codes, permission, request/response shape) rather than by probing.
2. **Given** a published schema, **When** the consumer validates a submission body against it, **Then** a body the schema accepts is a body the server accepts (no schema-valid, server-rejected bodies).
3. **Given** the published artifacts, **When** the consumer authors a variable, a Variable expression, or a JavaScript expression, **Then** the required value spaces and wire formats are published, not guessed.

---

### User Story 2 - Learn behaviour that no descriptor field can express (Priority: P1)

A consumer reads one-sentence, test-pinned claims for the engine behaviour that contracts structurally cannot carry, instead of discovering it by publishing throwaway probe workflows.

**Why this priority**: three independent sessions hit the same JavaScript single-expression 422; one published a probe workflow purely to learn how `ParsedContent` projects into Jint. These are pure, repeatable, avoidable cost.

**Independent Test**: every claim in `docs/consumer-guide/claims.json` names tests that exist and pass; every `[ConsumerContract]`-marked test names a claim that exists.

**Acceptance Scenarios**:

1. **Given** a claim, **When** its pinning test is deleted or renamed, **Then** CI fails until the claim is re-pinned or retired (Gate A).
2. **Given** a test marked `[ConsumerContract("id")]`, **When** the claims file has no such id, **Then** CI fails (Gate B).

---

### User Story 3 - Trust the published availability rule (Priority: P1)

A consumer applying the documented availability rule gets the true answer for every published capability, including ones no feature gates.

**Why this priority**: A1 below shows the previous fix published `Literal` but left it unreachable *through the documented rule* — the defect moved rather than closed. A rule that is right for most entries is a trap.

**Acceptance Scenarios**:

1. **Given** `hosts.json`, **When** the consumer asks which expression types a host serves, **Then** the answer is published directly, not derived from a feature-keyed intersection that silently drops un-gated entries.

---

### Edge Cases

- A published contract gains fields without a version signal → a consumer cannot tell whether its cached copy is stale (Part D).
- A feature is enabled in `shells.json` but its assembly is absent → silent no-op; already answered by `hosts.json` (spec 149) and unchanged here.
- An endpoint exists but is undocumented → indistinguishable to a consumer from an endpoint that does not exist; the entire probing cost comes from this.

## Requirements *(mandatory)*

### Part A — verified defects (fix first)

- **FR-A1**: Expression types not gated by any feature (`Literal`, `Object`, `Input`) MUST be reachable through the documented availability rule. Publishing them with a null/absent `featureId` while the documented rule intersects on feature id makes them invisible to a conforming consumer. *(Verified: the artifact serializes `featureId: null` — the maintainer report says empty string; the representation differs, the exclusion is identical.)*
- **FR-A2**: The submit schema MUST NOT accept a body the server rejects. Specifically, `intrinsic.valueType` is required for every kind except `control` and `finish`, and `intrinsic.variable` is required for `set`/`merge`/`reduce` and forbidden otherwise — conditions the runtime enforces but the schema does not express.

### Part B — remaining RFC layers

- **FR-B1**: `[ConsumerNote]` attributes + gate G4, and spec-kit analyze findings F1–F3. Advisory only: a finding is surfaced and may be accepted or declined; notes and waivers MUST NOT be forced by a build gate.
- **FR-B2**: `docs/consumer-guide/claims.json` — typed envelopes (`id`, `scope`, `kind`, `stability`, `tests[]`, `since`) around one-sentence statements — with Gate A (referenced tests exist and pass) and Gate B (bidirectional `[ConsumerContract]` ↔ claims-file reference).

### Part C — measured knowledge gaps

Data (fragments/schema):

- **FR-C1**: Publish the `type.alias` value space for variables (a required field, typed as a bare string, enumerated nowhere; agents probed four endpoints, all 404, then guessed).
- **FR-C2**: State that an intrinsic's `descriptorId` (`elsa.intrinsic.set@1`) *is* the node's required `activityVersionId`.
- **FR-C3**: Document which endpoint enumerates what — intrinsics appear in `/design/activities/catalog` but not `/design/activities/definitions`.
- **FR-C4**: Publish the wire format of a `Variable` expression payload, and whether `variables`/`getVariable` key on `name` or `referenceKey`.
- **FR-C5**: Publish which `expressionType` carries a JSON array for collection-typed inputs.
- **FR-C6**: Enumerate the `authoredVia` vocabulary (load-bearing, currently undefined).

Behaviour (consumer-guide claims):

- **FR-C7**: JavaScript activity inputs are parsed as a single expression, not a script — no `var`, no `return`. The published `MaxStatements` option actively implies the opposite.
- **FR-C8**: How a captured `ParsedContent` (`JsonElement?`) projects into Jint.
- **FR-C9**: Submit stores without validating; publish is the validation gate.
- **FR-C10**: Submit with an existing name creates a new definition, not a new version — which is what makes route exclusivity bite on re-runs.
- **FR-C11**: An HTTP `(route template, method)` pair is exclusive across definitions; republishing the same definition into its own slot is exempt.
- **FR-C12**: Artifact ids are content-addressed, so two identical definitions share one.
- **FR-C13**: The value-evidence model — capture is boundary-level (a bare root captures nothing); intrinsics capture nothing; previews cap at 256 chars (equality assertions need a `truncated == false` guard); snapshot `name` is not unique within an execution, so filters must match name **and** subject.

API surface (largest measured gap):

- **FR-C14**: Publish the API surface — route, verb, status codes, permission, and request/response shapes — for the endpoints a consumer needs to author, publish, execute and assert. `/swagger` and `/openapi` both 404 today, so every session obtained these by blind probing or by reading an existing consumer suite.
- **FR-C15**: `POST /publishing/workflows/{versionId}/publish` returns **201 Created**, not 200; every session asserted 200 and failed.
- **FR-C16**: `GET /runtime/workflows/instances` silently ignores `?definitionVersionId=` while `artifactId`, `definitionId` and `status` filter correctly — either wire it up or reject unknown filters. *(Investigate: defect vs. documented limitation.)*
- **FR-C17**: `DELETE /design/workflows/definitions/{id}` returns 204 and empties the definition list, but the publication stays live and keeps serving its route. Publish the retire/unpublish API if one exists; if not, record the operational gap. *(Investigate.)*
- **FR-C18**: `GET /design/activities/definitions?search=` is a fuzzy substring match (`JTest-Http-Greeting` matched `Http-Greeting`, causing a wrong-workflow deletion; `WriteLine` returns `WriteLines` first). Document that consumers must match on exact `activityTypeKey`, or tighten the endpoint. *(Investigate.)*

### Part D — versioning signal

- **FR-D1**: Decide and document whether `schemaVersion` bumps on additive change. Observed: submit-schema fingerprint moved `ca01ff0d… → 45ad34de… → e755046f…` across three builds while `schemaVersion` stayed `"1"` — fields were added to a published contract twice with no version signal.

### Part E — workbench packaging

- **FR-E1**: Decide explicitly which of the 14 requested-but-unavailable features are intentional omissions and add the rest. `ActivitiesScripting` (RunJavaScript) and `Liquid` have projects that are simply not referenced; alternative persistence providers are a legitimate exception.

## Success Criteria *(mandatory)*

- **SC-001**: A consumer agent with no source clone, given only `docs/contracts/` + `docs/consumer-guide/`, completes author → publish → execute → test for the benchmark workflow without probing undocumented endpoints or reading repository source.
- **SC-002**: Contracts-only benchmark mean ≤ 163,422 tokens (the github-only baseline) — i.e. the published knowledge pays for itself.
- **SC-003**: Zero schema-valid-but-server-rejected submission bodies.
- **SC-004**: Every consumer-guide claim is pinned by a passing test, and every `[ConsumerContract]` test names a real claim (Gates A/B).
- **SC-005**: No published capability is unreachable through the documented availability rule.

## Assumptions

- The benchmark, not this document, is the acceptance authority; requirements here are transcribed from it.
- Investigate-marked items (FR-C16/17/18) may resolve as "documented limitation" rather than code change; that outcome is acceptable if the limitation is published.
- Behavioural claims are published only where a pinning test exists or is added — an unpinnable claim is not published (Gate A would fail).
- This spec continues the spec-149 delivery branch; nothing here is validated as merged until the consumer workspace signs off.
