# Feature Specification: Storage drivers for workflow variables, inputs and outputs

**Feature Branch**: `agent/claude/3052-5698-storage-drivers`

**Status**: Draft

**Issue**: #5698 (ga-blocker), part of initiative #3052 (Elsa 3 to Elsa 4 conversion)

**Input**: A workflow's variables, inputs and outputs, and a container activity's variables, can be stored through any
registered durable-value storage driver, as a reusable workflow's top-level values already can. A converted Elsa 3
workflow whose driver was ported to Elsa 4 then publishes and runs with its values stored where the author chose.

## Problem

Elsa 4 has durable-value storage drivers: `IRuntimeDurableValueStorageDriver`, found by `DriverKey` in
`IRuntimeDurableValueStorageDriverRegistry`. The engine ships only `elsa.json`, which stores the value inline. Drivers are
used for a reusable workflow's (graph activity's) top-level inputs, outputs and graph variables
(`GraphActivityScope`), for activity-scoped `SetOutput`, and for output captures. Everywhere else they are not:

- **Variables are refused at publish.** `RuntimeInputBindingCompiler.CompileVariableDeclaration` reads any
  `StorageDriverType` other than `elsa.json` as an *external storage profile* (an `IExternalPayloadStore` routing key, a
  different concept from a driver key). It then throws `VF-ACT-005 … canonical variable-frame externalization is not
  implemented`. This applies to workflow variables, to variables on a container (Sequence, Flowchart, …) in any
  workflow, and to container variables inside a reusable workflow's body.
- **Inputs and outputs ignore the driver.** `WorkflowExecutableCompiler` keeps only the name and type of the workflow's
  own `InputDefinition`s and `OutputDefinition`s. Start inputs are seeded as inline JSON rows, and workflow-level
  `SetOutput` writes inline JSON rows. `StorageDriverType` is never read, and nothing says so.
- **Nothing checks the driver for a workflow.** A workflow's `StorageDriverRequirements` cover only output-capture and
  placed-template drivers, so publication and placement never refuse a workflow whose variable or input names a driver
  the engine lacks.
- **A driver cannot be chosen for built-in types.** A type is compatible with a driver only when its descriptor lists
  the driver in `CompatibleStorageDriverKeys`. Every built-in type descriptor lists only `elsa.json`, and a driver
  cannot declare which types it stores. Even where drivers work, a reusable workflow's `String` input on a custom
  driver is refused with `activity.contract.storage-driver-unavailable`, and Studio offers no choice.

**Example.** A host ports its Elsa 3 blob driver and registers it under the key `Acme.BlobStorageDriver`, the Elsa 3
full type name, as the converter convention requires. Workflow W declares input `document` (String), variable
`draft` (String) and output `report` (Object) on that driver.

- Today: Studio offers no storage choice for these types. In JSON, W's publication refuses `draft` with VF-ACT-005.
  Without `draft`, W publishes and runs, but `document` and `report` are stored inline in the instance, which is what
  the author chose to avoid. The Elsa 3 converter therefore refuses such workflows (`ELS3-VARIABLE-STORAGE-DRIVER-UNMAPPED`,
  and the input and output codes from #5683 / PR #5696).
- After this spec: W publishes. The start input `document`, every committed value of `draft`, and the output `report`
  are encoded by `Acme.BlobStorageDriver`. Every activity, script and caller that reads them gets the decoded value.
  A node without the driver is not chosen to run W, and an engine without it refuses W at publication, naming the
  driver and the member.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Workflow and container variables use a registered driver (Priority: P1)

An author declares a workflow variable, or a variable on a Sequence or Flowchart, on a registered driver other than
`elsa.json`. The workflow publishes. Each committed value of the variable is encoded by that driver, and every read
returns the decoded value.

**Why this priority**: This is the case the engine refuses today. Converted Elsa 3 workflows use it most.

**Independent Test**: Register a test driver that records encode and decode calls and stores values in its own store
(an external reference in the frame). Publish a workflow whose root variable and Sequence variable use it. Run steps that
write the variable (step `Set`, output capture, script assignment) and read it (input binding, step `Get<T>`, script read).
Each write is encoded by the driver, the frame holds only the driver's encoding, and each read gets the original value.

**Acceptance Scenarios**:

1. **Given** a workflow variable `draft` on driver D with a literal default, **When** the workflow starts, **Then** the
   root frame holds D's encoding of the default, recorded with D's key, and no other inline copy exists.
2. **Given** `draft` on D, **When** a step assigns it (step session `Set`, output capture or script assignment),
   **Then** the step's checkpoint commit stores D's encoding of the new value. Whole-value write intents, versions and
   lost-update checks behave as for an `elsa.json` variable (ADR 0090, 0092).
3. **Given** `draft` holds a value encoded by D, **When** a later activity reads it through an input binding, a step
   `Get<T>` or a script, **Then** it gets the decoded value, and D's `DecodeAsync` was called.
4. **Given** a Sequence variable on D inside a reusable workflow's body, **When** the reusable workflow publishes and
   runs, **Then** the same holds; it is no longer refused with VF-ACT-005.
5. **Given** a variable on `elsa.json`, **When** it is written and read, **Then** its stored form is unchanged from today.

### User Story 2 - Workflow inputs and outputs use a registered driver (Priority: P1)

An author declares a workflow input and output on a registered driver. The start input is stored through the driver
before the first activity reads it, and the output is stored through the driver when set. Callers get decoded values.

**Why this priority**: Today the driver is silently ignored, so the author's choice is lost without any signal.

**Independent Test**: Start a workflow whose input and output use the recording test driver. The seeded input row and
the output row carry the driver's encoding and key. An activity bound to the input reads the original value. Instance
details and a parent workflow that called it as a child get the original output value.

**Acceptance Scenarios**:

1. **Given** input `document` on D, **When** the workflow starts with `document = "abc"`, **Then** the seeded input
   row holds D's encoding with D's key, and an activity binding `document` receives `"abc"`.
2. **Given** output `report` on D, **When** an activity sets it, **Then** the output row holds D's encoding, and
   instance details show the decoded value, whether D stored it inline or externally.
3. **Given** workflow C with output `report` on D is run as a child of workflow P, **When** C completes, **Then** P
   receives the decoded value.
4. **Given** input validation rejects the start input, **When** the workflow is started, **Then** D's `EncodeAsync` is
   not called.

### User Story 3 - A missing driver refuses publication and placement (Priority: P1)

An engine without the driver that a workflow's variable, input or output names refuses to publish the workflow, and a
cluster never places it on a node without that driver.

**Why this priority**: A value must never silently fall back to inline storage, and a run must never fail midway
because a node lacks a driver.

**Independent Test**: Publish a workflow whose variable names an unregistered key. Publication is refused with a
diagnostic naming the key and the variable. Check the executable's runtime requirements for a registered key: they
include it.

**Acceptance Scenarios**:

1. **Given** variable `draft` on key K that no driver has, **When** the workflow publishes, **Then** publication is
   refused with `workflow.runtime.storage-driver-missing`, naming K and `draft`. The same applies to an input, an output
   and a container variable.
2. **Given** a published workflow whose values use D, **When** placement chooses a node, **Then** D is among the
   executable's storage-driver requirements, and a node without D is refused with the existing `HasStorageDriver`
   reason. Preflight and artifact reconciliation report the same.

### User Story 4 - A ported driver can be chosen for the types it stores (Priority: P1)

A host registers a driver. Without replacing any type descriptor, authors can choose it in Studio and in JSON for every
type the driver can store, for workflow variables, inputs and outputs and for reusable workflow contracts.

**Why this priority**: Without it no ported driver can be used for `String`, `Object` or any other built-in type, so
stories 1 to 3 would only work for custom types.

**Independent Test**: Register a driver that declares no type list. The authoring capabilities list it for every type
that `elsa.json` can store and not for a type that `elsa.json` cannot store. A reusable workflow's `String` input on it
publishes.

**Acceptance Scenarios**:

1. **Given** driver D registered with no type list, **When** Studio loads authoring capabilities, **Then** D is offered
   for every type compatible with `elsa.json`, and not for a type with no durable driver (a connection type, for example).
2. **Given** driver E that declares it stores only `String`, **When** an author chooses E for an `Object` variable,
   **Then** publication is refused with `activity.contract.storage-driver-unavailable`, naming E and the type.
3. **Given** the workflow properties in Studio, **When** the author edits an output, **Then** the storage column is shown
   for outputs, as it already is for variables and inputs.

### User Story 5 - Converted Elsa 3 workflows keep their driver (Priority: P1)

The Elsa 3 converter passes a variable's, input's or output's non-default storage driver through as the Elsa 4 driver
key, the Elsa 3 full type name without its assembly, instead of refusing the workflow.

**Why this priority**: This is why #5698 blocks GA.

**Independent Test**: Import an Elsa 3 workflow whose variable, input and output use `Acme.BlobStorageDriver, Acme`
into a host that registers a driver with key `Acme.BlobStorageDriver`. It converts, publishes and runs with values
encoded by that driver. Into a host without it, the import is refused at publication with
`workflow.runtime.storage-driver-missing`, not at conversion.

**Acceptance Scenarios**:

1. **Given** an Elsa 3 variable with `StorageDriverTypeName = "Acme.BlobStorageDriver, Acme"`, **When** it converts,
   **Then** the Elsa 4 variable's `StorageDriverType` is `Acme.BlobStorageDriver`, and no
   `ELS3-VARIABLE-STORAGE-DRIVER-UNMAPPED` is reported. The same applies to inputs and outputs (#5683 codes).
2. **Given** the Elsa 3 default driver (`WorkflowInstanceStorageDriver`), **When** it converts, **Then** the Elsa 4 value
   uses `elsa.json`, as today.

### Edge Cases

- A driver returns an encoding a frame cannot hold (`DurableValueStorage.Custom`, or neither an inline value nor an
  external reference): the write faults the step with a diagnostic naming the driver and the variable. Nothing is
  committed.
- A driver throws on encode: the step faults like any failed write, naming the driver. On decode, only the read that
  needs the value fails. A script that does not read the variable is not blocked, as with external values today.
- Lost-update checks (ADR 0092) compare versions and digests of the stored encoding. A driver that encodes the same value
  differently on each call makes a no-op assignment a write. That is allowed and stated; it never loses an update.
- Transient variables (live, non-persistable values) are never encoded and are not changed by this spec. A variable
  whose policy requires encryption keeps today's rules. A driver does not lift them.
- `SetCorrelationId` and other identity values are not authored members and are not affected.
- A reusable workflow's top-level values keep `GraphActivityScope`'s existing driver path unchanged.
- Background activities (ADR 0093) read the last durable value, decoded through the driver, like any other read.
- The authored value `elsa.json` and an empty value mean the same thing, as today.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: An authored `StorageDriverType` on a variable, input or output MUST name a durable-value storage driver key.
  It is no longer read as an external storage profile. Empty means `elsa.json`.
- **FR-002**: Publication MUST accept a workflow variable or container variable (in a workflow or in a reusable
  workflow's body) on any driver key that is registered and compatible with its type, instead of refusing it with
  VF-ACT-005. The compiled variable declaration MUST carry the driver key.
- **FR-003**: Every value committed to a variable frame for a variable on driver D MUST be D's encoding of that value,
  recorded with D's key. This covers initial defaults, step session writes, output captures, script assignments and
  intrinsic writes. Encoding happens before or in the step's checkpoint commit, and the frame holds no other copy.
  The frame remains the only store of the variable (ADR 0048), and change detection uses the driver's serialized form
  (ADR 0090 decision 6).
- **FR-004**: Every read of such a variable MUST return D's decoding of the stored encoding. This covers input binding
  materialization, member reads, script reads, step session `Get<T>`/`GetReadOnly<T>`, and background reads. Reads that
  ADR 0090 defines as synchronous get values decoded when the session opens: the node's bound variable references, and
  every visible driver-stored variable for a free-name or script node (D-3).
- **FR-005**: A workflow's own inputs MUST be seeded through their declared driver, with the driver key recorded as the
  existing `runtime.storageDriverKey` metadata, so the existing decoding input projection applies. Encoding happens
  after start-input validation.
- **FR-006**: Workflow-level `SetOutput` MUST encode through the output's declared driver. Instance details, child
  workflow completion and every other reader of a workflow output MUST get the decoded value. The existing
  not-stored-inline marker stays only for engine-external values that carry no driver key.
- **FR-007**: The executable's storage-driver requirements MUST include the drivers of all workflow variables,
  container variables, inputs and outputs. Placement, preflight and artifact reconciliation then apply unchanged.
- **FR-008**: Workflow publication MUST refuse a variable, input or output whose driver is not registered, with
  `workflow.runtime.storage-driver-missing` naming the key and the member.
- **FR-009**: A durable-value storage driver MAY declare the type aliases it stores. A driver that declares none stores
  every type compatible with `elsa.json`. A type is compatible with a driver when its descriptor lists the driver or the
  driver stores the type. Authoring capabilities, contract validation and publication use this one rule (D-1).
- **FR-010**: Studio's workflow properties MUST show the storage column for outputs as well as variables and inputs,
  offering the drivers compatible with the output's type.
- **FR-011**: The Elsa 3 converter MUST map a non-default Elsa 3 storage driver of a variable, input or output to the
  Elsa 4 driver key equal to the Elsa 3 full type name without its assembly, and MUST NOT refuse it. The codes
  `ELS3-VARIABLE-STORAGE-DRIVER-UNMAPPED`, `ELS3-INPUT-STORAGE-DRIVER-UNMAPPED` and `ELS3-OUTPUT-STORAGE-DRIVER-UNMAPPED`
  are removed, and the migration skill inventory is updated (D-2).
- **FR-012**: Values on `elsa.json` MUST keep today's stored form, apart from the driver key now recorded in the
  compiled declaration. Hashes and fixtures that change are regenerated in place.

### Key Entities

- **Durable-value storage driver**: encodes and decodes one value. Gains an optional list of storable type aliases.
- **Variable declaration** (`RuntimeVariableDeclaration`): gains the driver key.
- **Variable frame value** (`ValueEnvelope` in a `VariableFrameState`): holds the driver's encoding, inline or as an
  external reference, with the driver key recorded.
- **Workflow input and output rows** (`durable-input:<name>`, `durable-output:<name>`): encoded by the declared driver.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A workflow whose root variable, container variable, input and output all use a registered test driver
  publishes and runs to completion. 100% of stored values of those members are that driver's encodings, and every
  read in the run returns the original value.
- **SC-002**: No workflow member on a driver other than `elsa.json` is stored inline unless the driver produced that
  inline encoding. The silent fallback is gone.
- **SC-003**: A workflow that names an unregistered driver is refused at publication 100% of the time, with a
  diagnostic naming the driver and the member.
- **SC-004**: The Elsa 3 example in this spec converts, publishes and runs against a host that registers
  `Acme.BlobStorageDriver`, with no converter refusal.
- **SC-005**: Existing workflows on `elsa.json` behave and store exactly as before; only regenerated hashes and fixtures
  change.

## Decisions for approval

**D-1. How a driver becomes usable for a type.** Problem: `Acme.BlobStorageDriver` is registered, but `String`'s
built-in descriptor lists only `elsa.json`. Studio offers nothing, and publication refuses with
`activity.contract.storage-driver-unavailable`. Today the host would have to replace the built-in type descriptor
providers to use its driver.

| Option | What it means | Cost |
|---|---|---|
| A (recommended) | A driver may list the type aliases it stores. Without a list it stores every type `elsa.json` stores. Compatible = the type lists the driver, or the driver stores the type. | One optional member on the driver contract. Non-durable types (connections) stay excluded, because `elsa.json` cannot store them. |
| B | Every registered driver is compatible with every type. `CompatibleStorageDriverKeys` is removed. | Simplest, but offers drivers for types that cannot be stored durably. |
| C | Keep it as it is. Hosts replace type descriptor providers. | No engine change. Every ported driver needs descriptor surgery, and converted workflows fail at publish anyway. |

Recommendation: **A**.

**D-2. Converter in this PR.** PR #5696 (#5683) adds the input and output refusals and is still open. Option A
(recommended): once #5696 merges into the initiative branch, this PR maps drivers through (FR-011) and removes all
three refusal codes, so a missing driver surfaces at publication (FR-008), where the engine knows its registry. Option
B: keep the refusals and lift them in a follow-up. Converted workflows would stay blocked after this PR, so the
ga-blocker would remain open.

**D-3. Synchronous step reads of driver-stored variables.** ADR 0090's `Get<T>` is synchronous, and a driver's
`DecodeAsync` is not. Option A (recommended): decode when the step session opens. This covers the node's bound
references, and for free-name or script nodes every visible driver-stored variable, which is what the script read
fallback already does for external values. Option B: refuse driver-stored variables in step sessions, as external
variables are refused today ("bind it to an input instead"). Converted scripts that read such a variable would then
fault. A costs extra decodes for unused variables in free-name nodes only.

## Assumptions

- No new ADR is needed. ADR 0045 (durable-value persistence is a seam beneath variable records), ADR 0048
  ("externalized encoded values" in frames, no shadow store) and ADR 0090 decision 6 (variables serialized with their
  storage driver) already describe this. This spec implements them for frames, inputs and outputs.
- `IExternalPayloadStore` and storage profiles remain an engine seam, for example for result projections and for a
  driver's own use. They are no longer reachable from an authored `StorageDriverType`.
- The engine still ships only `elsa.json`. Tests use test drivers. Porting a host's Elsa 3 driver is the host's work.
- Driver key matching is ordinal and exact. There is no alias mapping (the converter convention makes keys match).
- Pre-stable: fixtures and hashes are regenerated in place, with no compatibility shims.
