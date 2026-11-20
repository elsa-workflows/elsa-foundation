# Activity Data Selection: M1 customer proof

**Date:** 5 November 2026. **State:** Research findings for review; no production contract or implementation adopted.
**Owns:** [M1 #3884](https://github.com/elsa-workflows/elsa-foundation/issues/3884), under [Program #3882](https://github.com/elsa-workflows/elsa-foundation/issues/3882).
**Plan:** [Activity Data Selection](../plans/activity-data-selection.md). **Initial qualified source:** `0552460ec56e389c5c1e2d6855ec9d4ab1702d99`.

The requested no-expression customer journey fails in current Studio. Neither Sequence nor linear
Flowchart offers an activity-result/member chooser. A seeded direct reference also loses its object
representation through Code Apply. Existing portable expression bindings can carry the same Customer
through real publication and execution: both structures delivered the expected consumer input.

This is the issue's **precise-failing-seam research exit**, not acceptance of the requested product.
The [M1 issue](https://github.com/elsa-workflows/elsa-foundation/issues/3884) and its PR receipts identify
the final tested head, downloadable evidence and independent inspection. A local path, a synthetic
control or a successful API-authored expression does not replace the failed UI criterion.

## Experiment

The prerequisite establishment PR [#3888](https://github.com/elsa-workflows/elsa-foundation/pull/3888)
merged as the source above. Resulting-main [CI42042243904](https://github.com/elsa-workflows/elsa-foundation/actions/runs/42042243904)
completed successfully before claim or code work. Specific App claim
[6662449028](https://github.com/elsa-workflows/elsa-foundation/issues/3884#issuecomment-6662449028)
won; no assignment or Ready promotion was used.

A private compile import added `Elsa.Workbench.M1CustomerDataProof.GetCustomer` only to the owned
research Workbench build. Its catalog exposes **one Customer object output**, key `Customer`, path
`customer`. Its result contains `customerId: "cust-1042"`, `display_name: "Deterministic customer"`
and `nickname: null`. There is no separately declared scalar ID output. The consumer is the existing
WriteLine activity's Text input. Each graph has a producer followed by a consumer, no workflow
variables and no output captures.

Normal composition and a minimal composition used copied host roots, allocated loopback ports and
disposable SQLite stores. Every owned host stopped after its run. Fixtures, proof drivers, stores and
compiled outputs are not part of shipped catalog delivery or this documentation change. The evidence
snapshot retains the private source and its hashes so the experiment can be reviewed without adopting it.

The minimal composition removes the backend JavaScript/Liquid features and the two optional Studio
expression-editor features. Its actual syntax menu omits JavaScript/Liquid and no optional editor
scripts load. The shared code-editing package is internal infrastructure, not a runtime module
manifest ([Studio ADR 0006](../../Studio/docs/adr/0006-keep-code-editing-as-internal-studio-infrastructure.md));
this experiment does not claim to remove that package from the existing dependency closure.

## Acceptance results

| Criterion | Result | Concrete observation |
|---|---|---|
| Typed Customer and both structures | Passed setup | One object output; actual Sequence and producer → consumer Flowchart canvases render. |
| Browse declared fields before a run | Failed requested UI; supporting metadata passed for Sequence | Normal Sequence context exposes Customer and CLR member shapes before workflow execution. Linear Flowchart context omits the producer. Neither UI offers member browsing. |
| Choose Customer ID without a variable, capture or handwritten expression | Failed | Both normal and minimal input menus omit ActivityResult/Choose data. No picker-authored mapping exists. |
| Readable provenance and canonical save/reopen/Code Apply | Failed direct-reference candidate | The seeded ActivityResult object becomes escaped JSON text after Apply/save/reopen. Display rename preserves node identity; structured portable expression parameters survive the same route. Neither supporting result establishes a readable picker reference. |
| Publish/run and observe consumer input | Failed requested mapping; passed supporting runtime controls | An extra literal path on ActivityResult receives HTTP 400 `VF-COER-001`, Customer → String. Four explicit portable-expression controls complete and expose the expected committed consumer input below. |
| Transformation with selected source and generated parameters | Not run through picker | The chooser/insertion step is absent. An explicitly API-authored `args.customer.customerId.toUpperCase()` with an existing ActivityResult parameter completes in both structures. |
| Basic mapping without optional language/editor modules | Failed basic mapping; passed preservation | Basic selection remains absent. Existing portable JavaScript objects and parameters survive actual unsupported-editor, Code Apply, save and reopen in both structures. JavaScript runtime execution is intentionally not run in this lane. |
| Rename and deleted source | Characterized; publication gate failed | Renames preserve `producer` identity. Deletion preserves the existing expression parameter and shows a specific global validation warning with Go to. Publication nevertheless accepts the missing-producer graph; runtime then requires intervention. Go to/repair interaction is not tested. |
| Nested representation, naming, typing and optionality | Passed bounded semantic comparison | All 15 private real-service controls pass. They establish mechanics, not production compiler/provider adoption. See below. |
| Draft/revision/permission and Flowchart authority | Characterized with gaps | Unsaved deletion still sees saved Sequence sources; saving removes them, old revisions return Stale, and anonymous requests return 401. Flowchart discovery disagrees with valid execution. Role-specific denial/revocation and general graph journeys are not run. |
| Composed evidence and independent inspection | Passed research inspection of baseline runs; final receipt required | Independent reviewers opened actual normal/minimal PNGs and semantic receipts. Primary product acceptance stays failed. Final current-head artifacts and review are recorded on the owning PR. |

The first `normal-001` run is retained as preliminary evidence. Private-driver enum, accessible-name
and dispatch-receipt polling errors were corrected after its hosts stopped, then retested in fresh
stores. Those errors are not product rename/runtime failures. `normal-002` and `normal-003` have no
surviving helper errors. Earlier screenshot-animation and global-warning detection limitations are
superseded by settled captures and independent inspection; the global missing-producer warning exists.

### Actual persisted runtime controls

These representative `normal-003` runs use explicit portable JavaScript parameters authored by the
driver. They are **supporting controls**, not successful no-expression Studio authoring.

| Structure/control | Workflow execution | Committed consumer input |
|---|---|---|
| Sequence, member read | `01a1ac874d6c7cc1b4677b474d9e0c23` | `cust-1042` |
| Sequence, uppercase | `01a1ac878fa77e5f97f2ccdda9141061` | `CUST-1042` |
| Flowchart, member read | `01a1ac87d0c271c28540242f9c7d9eaa` | `cust-1042` |
| Flowchart, uppercase | `01a1ac8812257e4c94e472ebc11a64b1` | `CUST-1042` |

Real publication and version-test-run APIs produced Completed runs in Balanced mode. Consumer
inspection receipts include checkpoint identities and `lastCommittedAt`; separately authorized value
evidence returns untruncated String diagnostic snapshots with those nine-character values. Actual Run
screens show those inputs and no incidents. This is bounded diagnostic evidence, not a claim of full
raw-payload capture or restart durability. An ancillary private SQLite row observer reported zero
matching rows; that zero is not counted as evidence success. The committed API/inspection receipts and
rendered runs are the persisted-run evidence.

### An actually referenced producer is deleted

The consumer's portable parameter remains `ActivityResult`, producer `producer`, projection `Customer`,
`isOptional: false`, after deleting the producer. Code Apply/save/reopen preserves it. The global
validation issue says **“Required result producer 'producer' is not present in the workflow graph.”**
with Go to. The input row has no selected-source repair/provenance UI; the minimal host additionally
shows its generic unavailable-JavaScript-editor message. These are different diagnostic scopes.

Publication returns 201 for `artifact-34a35c4bf4e2`. Actual executable/input-source inspection contains
only root and consumer while retaining the missing-producer parameter. In the normal host, run
`01a1ac86fc29798f92272fb6af854a8e` remains **Running**, consumer **Scheduled**, `completedAt: null`,
with a blocking `InputMaterializationFailed` incident and causal-result-resolution exception. It is
neither terminal Faulted nor Completed. No committed input value or successful substitute is established.

The seam is a validation handoff: [ValueFlowValidator](../../Essentials/src/Workflows/Design/Validations/Elsa.Workflows.Design.Validations/Validators/ValueFlowValidator.cs)
detects missing portable-parameter producers, but
[PublishWorkflowRequestHandler](../../Essentials/src/Workflows/Publishing/Elsa.Workflows.Publishing/Handlers/PublishWorkflowRequestHandler.cs)
gates expression publication through the language semantic validator. That validator checks source
text with an empty-symbol context; it does not apply the general value-flow result. The portable
compiler copies parameters and the result conversion linker processes direct result bindings only.
Runtime still uses the causal resolver and refuses the unavailable required occurrence. M2 must
reconcile publication/discovery authority rather than weaken that runtime behavior.

## Nested representation comparison

The private comparison uses the real projector, compiler, conversion linker, causal resolver and
executable hasher with synthetic occurrences. It does not run a modified production compiler/CLR
activation/persistence pipeline with derived projections.

| Alternative | Observed mechanics | Consequence for M2 |
|---|---|---|
| Derived pinned producer projection | A private String projection at `customer.customerId` resolves `cust-1042`; conversion planning uses String/TextValue with an Identity operation. Projection key/path/type/requiredness/policy/representation affect contract and executable identity. | Existing mechanics are viable, but current compilation creates projections only from top-level `[Output]` properties. Derivation ownership and all provider/template paths require specification and real pipeline proof. |
| Extra literal `path` on existing ActivityResult | The compiler drops the field. The source stays Customer, so String conversion is refused. An object target still receives the whole Customer. An undeclared `Customer.customerId` projection key is refused. | This is intentionally unsupported experimental input, not a wire proposal. Future support needs validated member identity, selected source typing, optionality, policy and hash/capability treatment; silently ignored metadata is insufficient. |
| Explicit expression | Existing portable Customer parameter plus `args.customer.customerId` or uppercase works through persisted execution. | Appropriate for an explicit transformation with an installed adapter; it cannot satisfy language-neutral basic selection or generated picker insertion by itself. |

Declared shapes expose CLR `CustomerId` and `DisplayName`. Actual activity-value serialization emits
`customerId` and explicit `display_name`. The projector accepts the serialized paths and rejects the
CLR/default-camelCase alternatives for the explicitly named property. Discovery therefore needs
authoritative serialized-member metadata; casing guesses are insufficient. Reflection also reports
all three reference-type members nullable, including the non-nullable CustomerId declaration.

Controls distinguish optional missing members (Absent), optional null members (ExplicitNull), required
missing/null projection refusal and optional missing causal producers. Producer optionality does not
stand in for member optionality. A consequential tradeoff remains: the completion projector validates
every pinned projection and combines its policy into the atomic result. Adding a required derived
member can make the **producer** fail; it is not merely a display convenience on the consumer.

Source inspection found no CLR activator that recomputes `[Output]` schema and automatically rejects an
extra pinned projection. That is an inference, not exercised adoption proof. Reusable graph public
outputs and immutable templates also need explicit treatment. Neither candidate is adopted here.

## Authority and graph characterization

[PersistedExpressionAuthoringContextSource](../../Essentials/src/Workflows/Design/Api/Services/PersistedExpressionAuthoringContextSource.cs)
reads a persisted draft. The frontend accepts editor state in its method signature but sends location
and revision identifiers, not that unsaved state. Actual Sequence observations are one source before
deletion, one while deletion is unsaved, then zero after Save; the old context revision returns Stale.
Anonymous requests return 401. More specific permission/revocation behavior is unqualified.

Linear Flowchart returns no activity-result source even though the real compiler/runtime controls
succeed. Tooling special-cases Sequence and delegates other structures to `ChildrenCompletedBefore`;
Flowchart has no implementation there. Publication's value-flow validator separately has dominator,
scope-boundary and cyclic-backedge rules. This is not one proven shared availability authority.

In the minimal composition a JavaScript context request returns Unavailable. This does not establish
the behavior of every other Expression Type; it does establish that the tested language-specific route
cannot simply be assumed to supply a basic non-code picker in that host.

| General case | Evidence boundary |
|---|---|
| Branches/joins and multiple entries | Existing validator computes definite availability over entries/dominators. No actual branch/join journey is qualified by this linear proof. |
| Backedges/iterations | Existing validator has explicit cyclic-backedge rejection; accepted causal result rules remain. No loop, cardinality or iteration proof was run. |
| Structured scope crossings | Existing validator requires explicit returns/collections/merges where applicable. No new scope rule or broad reachability approximation is introduced. |
| Durability/restart | Four completed store-backed controls plus one incident run are retained. Suspend/restart, retry-input semantics and provider portability remain outside this experiment. |

## Handoff to M2

[M2 #3885](https://github.com/elsa-workflows/elsa-foundation/issues/3885) should specify these findings
before production workers diverge. This is proposed contract guidance, not an approved public payload:

1. Use stable producer/output/member identity separately from display labels. Member metadata must name
   serialized fields and carry selected type, nullability, cardinality, policy and honest unknown/truncated shape.
2. Supply current-draft/snapshot, revision- and permission-aware source facts for ordinary inputs without
   requiring an optional language. Validate the same source facts at publication, including portable parameters.
3. Compile a language-neutral typed member plan before conversion planning and hashing. Decide producer-
   versus consumer-owned projection behavior explicitly, including required-member failures, immutable
   templates, provider completeness and durability. Do not turn the synthetic projection key into a wire contract.
4. Reuse installed expression adapters and portable bindings for explicit transformations, preserving
   existing source when modules are absent. Picker insertion/document undo and generated-parameter behavior
   still need actual implementation and acceptance.

Preserve accepted [ADR 0045](../adr/0045-workflow-value-flow-uses-role-owned-bindings-and-immutable-invocation-records.md).
This work changes no causal occurrence semantics, adopts no ambient activity-name/latest-result syntax,
and decides no broader coercion policy. Keep [#3094](https://github.com/elsa-workflows/elsa-foundation/issues/3094)
and its compensation relationship. General branch/loop/restart coverage, restricted inputs, reference
repair/remapping, keyboard/focus and narrow/dark product acceptance remain visible future obligations.
No M2 specification, production implementation, deployment or release was started by this experiment.
