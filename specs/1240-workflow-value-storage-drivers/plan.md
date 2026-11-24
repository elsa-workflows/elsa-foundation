# Implementation Plan: Storage drivers for workflow variables, inputs and outputs

**Branch**: `agent/claude/3052-5698-storage-drivers` | **Spec**: [spec.md](spec.md) | **Issue**: #5698, part of #3052

## Summary

Reuse the driver path that reusable workflows already use (`IRuntimeDurableValueStorageDriver` by `DriverKey`) for the
three places that bypass it today: variable frames, workflow start-input seeds and workflow-level outputs. Add the
declared drivers to the executable's storage-driver requirements, and add a publish-time check for missing drivers.
Let a driver declare the types it stores. Show storage for outputs in Studio, and pass Elsa 3 drivers through in the
converter. No new ADR: ADR 0045, 0048 and 0090 decision 6 already require driver-encoded values inside the frame.

## Technical Context

- .NET (essentials), xunit tests, no FluentAssertions. Studio: React/TypeScript, vitest.
- Projects touched: `Elsa.Workflows.Runtime.Core` (driver contract, declaration model), `Elsa.Workflows.Publishing`
  (compilers, readiness), `Elsa.Workflows.Runtime` (seeds, outputs, read paths), `Elsa.Activities.Runtime` (step
  sessions, capture write-back), `Elsa.Activities.Design.*` (capability catalogs), `Elsa.Expressions` (type
  descriptor providers, unchanged list semantics), Studio `Elsa.Studio.Workflows`, `extensions/Elsa3` (converter).
- Constitution gates: unit tests for changed logic-bearing services, extension-point catalog update (the driver
  contract gains a member), dependency map regenerated if references change (none expected).

## Design

### 1. Declaration carries the driver (FR-001, FR-002, FR-012)

- `RuntimeVariableDeclaration` gains `StorageDriverKey` (default `elsa.json`).
- `RuntimeInputBindingCompiler.CompileVariableDeclaration`: the authored `StorageDriverType` is a driver key. The policy
  stays `Inline`/persistable for every driver; the storage-profile mapping and the VF-ACT-005 throw are removed. The
  same mapping in `ExecutableNodeCompiler.IsTransient` is removed. Transient comes only from non-persistable policy.
- `ExecutableNodeCompiler.CompileStructure` / `CompileWorkflowVariables` and `CompileGraphVariables` pass the key
  through. It is part of the serialized declaration, so the behavioral hash covers it.
- `WorkflowExecutableCompiler.BuildInputContract`: `WorkflowDeclaredInput` gains `StorageDriverKey`.
  `WithOutputDeclarations` keeps `Name → (Type, StorageDriverKey)`.

### 2. Requirements and publish check (FR-007, FR-008)

- `WorkflowExecutableCompiler` adds the declared keys of root variables, container variables (all bodies), inputs and
  outputs to `StorageDriverRequirements`, apart from `elsa.json`, which is always present.
- New workflow publication check: each key must be in `IRuntimeDurableValueStorageDriverRegistry`. Otherwise publication
  is refused with `workflow.runtime.storage-driver-missing`, naming the key and the member, raised the way existing
  compiler refusals surface to the publish API.
- Placement (`ExecutionPlacementGate`), preflight and `WorkflowArtifactReconciler` already consume the requirements;
  no change beyond a test.

### 3. Frames hold driver encodings (FR-003)

One helper, `RuntimeVariableValueCodec` (Runtime), is the only place that converts between a value and a frame envelope
for a declared variable:

- `EncodeAsync(declaration, value)` calls the driver. `Inline` gives an inline envelope, and `External` gives an
  envelope with the external reference. The driver key is recorded in the envelope policy metadata as
  `runtime.storageDriverKey`, the key graph rows already use. `Custom` or an empty encoding is refused.
- `DecodeAsync(envelope)` uses the recorded key. An envelope without a key is `elsa.json` and is read as today.
- `elsa.json` short-cuts to today's inline form, so stored bytes are unchanged (FR-012).

Write points that go through it:

- `RuntimeVariableDeclarationProjector.ToEnvelope` (initial defaults, made async at its call sites).
- `StepVariableCommit`: the intents of driver-stored variables are encoded in the commit, which is async. Change
  detection compares the encoded forms, which for `elsa.json` are today's forms. The step session's refusal of
  `External`-policy variables is removed for driver-stored variables.
- Output captures into variables (`RuntimeOutputCaptureCompiler` / `RuntimeOutputCaptureProjector` /
  `RuntimeWorkflowVariableCaptureWriteBack`): the capture encodes with the target variable's driver, not the source
  output's driver. For a graph variable target this is already the case.
- Script assignments and intrinsic variable writes (`WorkflowIntrinsicExecutor`) go through the same codec.

### 4. Reads decode (FR-004)

- `RuntimeVariableReadFallback.LoadAsync` (scripts) and `RuntimeMemberRead` decode envelopes that carry a driver key
  through the codec, alongside today's `IExternalPayloadStore` branch, which remains for engine-external envelopes.
- Input binding materialization of variable reads (`RuntimeInputBindingStateProjection` / variable read paths) decodes
  the same way.
- `StepVariableSessions.CreateAsync` decodes, once, the driver-stored variables the session can read: the bound
  references, or every visible driver-stored variable for free-name or script nodes (D-3 A). The decoded values are
  handed to `StepVariableSession`, which stays synchronous. Its external-read refusal applies only to engine-external
  envelopes.

### 5. Workflow inputs and outputs (FR-005, FR-006)

- `RuntimeWorkflowStateSeed.BuildSeedChanges` encodes each seeded input with its declared driver (from
  `WorkflowDeclaredInput`) and adds `runtime.storageDriverKey`. `RuntimeInputBindingStateProjection.ProjectAllAsync`
  already decodes rows with that metadata. Seeding runs after start-input validation.
- `WorkflowIntrinsicExecutor.NewWorkflowOutputChange` encodes through the output's declared driver, mirroring
  `NewBoundaryOutputChangeAsync`.
- `RuntimeWorkflowOutputStateProjection` gets an async decoding variant. It is used by
  `WorkflowInstanceDetailsService` and by child-workflow completion delivery to the parent. The not-stored-inline
  marker stays only for engine-external envelopes that carry no driver key, as today.

### 6. Driver-declared types (FR-009, D-1 A)

- `IRuntimeDurableValueStorageDriver` gains `IReadOnlyCollection<string>? StorableTypeAliases => null` (default interface
  member). `null` means every type compatible with `elsa.json`.
- `IActivityContractStorageDriverProvider` gains `bool Stores(string driverKey, TypeDescriptor type)`.
  `RuntimeActivityContractStorageDriverProvider` implements it from the registry.
- `ExpressionActivityContractCapabilityCatalog` and `ClrActivityContractCapabilityCatalog` compute compatibility as
  (the type lists the key, or the provider says the driver stores it) ∩ registered. `ActivityAuthoringCapabilitiesReader`
  then reports per-type keys from that, so Studio and validation agree.

### 7. Studio (FR-010)

- `WorkflowPropertiesView.tsx`: show the storage column for outputs (the same options and auto-selection as inputs).
  Vitest for the column.

### 8. Converter (FR-011, D-2 A)

After PR #5696 merges into the initiative branch, merge the initiative branch in, then:

- `Elsa3VariableMapper` and `Elsa3ArgumentDefinitionToInputOutput` map a non-default Elsa 3 driver to the key
  `TypeName` (the part before the first comma, trimmed). The default driver maps to `null` / `elsa.json`, as today.
- Remove the three `*-STORAGE-DRIVER-UNMAPPED` codes, their diagnostics and their tests. Update the migration skill
  inventory.
- Import test: a host registering `Acme.BlobStorageDriver` converts, publishes and runs the spec example.

## Testing

- Test driver `RecordingExternalStorageDriver` (shared test helper). It keeps values in a dictionary and returns
  `External` references, and it records calls.
- Publishing tests: variable, container variable, body container variable, input and output on the test driver
  compile, carry the key and add requirements. A missing key is refused with the new code. An `elsa.json` declaration
  is unchanged.
- Runtime tests: default, step `Set`/`Get`, capture, script read and write, input binding read, workflow input seed,
  workflow output, instance details, child output to parent, and lost-update on a driver-stored variable.
- Capability tests: D-1 rules (no list, explicit list, non-durable type).
- Studio vitest: output storage column.
- Converter tests: mapping and removed refusals. Import end to end.
- Regenerate fixtures and hashes that change (pre-stable).

## Risks

- Making `ToEnvelope` and the capture write-back async touches several call sites. Keep the change mechanical.
- Change detection on non-deterministic drivers (an edge case in the spec) is documented, not prevented.
