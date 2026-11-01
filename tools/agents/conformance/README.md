# Specialist kernel package qualification

This is the bounded M2 consumer proof for [Program #3519](https://github.com/elsa-workflows/elsa-foundation/issues/3519).
It builds actual committed product packages, an independent deterministic model package, an authoring
process and a Runtime process. All consumer references are package references. Nothing is pushed or
published remotely, and no model inference is purchased or sent over a provider network.

Run with the bundled Python 3.12+ runtime and the normal build-slot-wrapped .NET command:

```bash
python3 tools/agents/conformance/qualify.py \
  --source HEAD --version 4.0.0-specialist3548.kernel1 \
  --output /private/tmp/elsa-specialist-kernel-proof
```

The new output directory must be outside the checkout. Producer source, including every fixture,
comes from the specified Git commit. The author reuses the committed public-API publication fixture
under a different assembly name, without product InternalsVisibleTo access. It qualifies real reviewed
publication/placement, immutable prompt behavior, schema compatibility, stale review and missing
runtime/storage. Its SQLite databases are disposed and deleted before the separately started consumer.
Only serialized placement material crosses that process boundary.

The Runtime consumer activates the real CShells lifecycle, including Tasks, Events, Serialization,
and the background feature dependencies, then resolves its scoped runtime services. It
checks that ordinary startup registered the specialist alias without manually calling product tasks.
Native activity startup also registers its declared business types, and the shared chat assembly and
resolved shared dependency pins are checked. The optional Expressions implementation is unnecessary
for this literal-input profile and would import Design packages, which the closure guard refuses.
It composes the real facade/background worker and the separately packed model
contribution. It checks the exact model/schema/instructions, native typed result, duplicate delivery,
invalid-result refusal and disposal. Runtime package graphs and loaded assemblies must contain zero
Design/Publishing dependencies. Restores use isolated caches and locked mode; retained candidate
archives and extracted DLL bytes are verified against the consumed packages.

`report.json` distinguishes each stage and records source/archive/package/fixture evidence. The invalid
model-result case must produce a Faulted workflow with no successful completion. A passed bounded
profile is not the final Program acceptance: the journal remains explicitly process-local; real
providers, durable recovery, native/BPMN authoring journeys, Studio and the complete matrix remain
subsequent qualification work. The executable is seeded from the real placed specialist; this does
not claim M4's ordinary workflow/BPMN publication journey.


## BPMN package boundary profile

Run `qualify.py --profile bpmn-boundary --source HEAD --version <unique-local-version>
--output <new-absolute-directory>` with the same Python and .NET prerequisites.
This profile builds the committed BPMN Core, Design and runtime packages alongside the specialist
packages. After real specialist publication/placement, an independent authoring process activates
the Design feature, projects the exact specialist child and lowers/remaps its BPMN graph through
the public structure handler. Only the compiled graph and immutable placement enter the runtime.
The authoring package closure and loaded assemblies exclude the concrete BPMN runtime/interpreter.

A separate runtime-only package consumer activates the normal CShells lifecycle and executes that
graph with the real specialist facade/background worker and deterministic MAF contribution. Its
resolved packages and loaded assemblies must contain zero Design/Publishing assemblies. Another
consumer activates the combined runtime/Design features and proves the same execution retains one
authoring handler and one publication pin contribution. Every candidate archive/extracted DLL is
checked against the isolated consumer cache. `PassedBpmnBoundaryProfile` is this finite package
boundary receipt; it does not qualify the editorial revision loop, human review, Studio or a live model.

## File-backed native recovery profile

Run the separate profile with the bundled Python runtime:

```bash
python3 tools/agents/conformance/qualify.py \
  --profile durable-recovery --source HEAD --version 4.0.0-specialist3523.durable \
  --output /private/tmp/elsa-specialist-durable-proof
```

This also runs the kernel consumers, then builds an independent Runtime consumer using actual
Specialist EF and complete Runtime EF packages. Normal CShell activation applies/validates module
migrations. The host composes `AddEfSchemaReadability` once on its root container and checks observed
`RuntimePostCommitOutbox` schema2 readiness before work. Without normal host readability the EF gate
retains schema1 and refuses required-source data; this is a refusal, not permission to force a version.

Each Balanced/Durable case uses a fresh file-backed SQLite database and five distinct processes:

1. Start the native facade and commit its protected turn/background-child responsibility, without a model contribution.
2. Claim the actual durable work, run the independent deterministic MAF model once, then exit197 after journal acceptance and before native receipt recording.
3. Reclaim the same work after real native visibility expiry with a newer fence. New admission is disabled and no model provider is registered. Accepted replay records the receipt; normal bounded resumption drives native completion. Exit198 after the real application handler commits its journal metadata, before the processor acknowledges the physical source.
4. Reopen with no model provider, wait for actual source claim visibility to expire, and retry through the normal resumption service. The application remains singular and its new physical claim is acknowledged.
5. Reopen independently and verify typed facade/workflow completion, the accepted conversation/revision/reference, one accepted turn/application, its physically Delivered matching source and one dispatch counter file across the processes.

The first causal crash uses the public activation lease boundary and preserves the actual pinned leaf type.
It reads the accepted journal and still-Claimed work before exiting. The second uses a public dispatcher
replacement that delegates the real handler and all source requirements, then checks actual applied
metadata and the current Delivering source before exiting. Persistence stores, native fences
and settlement sources are never replaced or fabricated. Synthetic durable keys and an independent
GUID file per model dispatch cross the process boundary; no credentials or business payloads enter
counter files. The expected nonzero worker exits are labelled `ExpectedCrash` in the receipt.

`PassedDurableRecoveryProfile` requires every named check and package/archive/DLL closure check to pass.
The profile proves these two SQLite process-loss boundaries in both persistence modes, including one
continuity-enabled accepted turn. It does not execute a second model turn from that conversation. It does not establish
power-loss durability, older module binary migration/rollback, the full provider/host matrix, streaming,
BPMN/Studio authoring or live-model acceptance. Failed run receipts remain failed and separate.

The durable profile also accepts `--stream-responses`. Its external deterministic model contribution
declares streaming support and emits two visible chunks plus terminal/usage updates through MAF. The
dispatch receipt identifies the selected atomic/stream path. Recovery/settlement still run without a
model contribution. This is packaged streaming/runtime proof, distinct from the concrete SDK socket
fixtures and live-provider qualification.

For two retained successful durable receipts, the unchanged-format binary check runs without rebuilding
either consumer:

```bash
python3 tools/agents/conformance/compatibility.py --older /absolute/older-atomic-witness --newer /absolute/newer-streaming-witness --output /absolute/new-binary-proof
```

It rechecks package archives and actual consumer product DLLs, then crosses five distinct processes per
case in both directions and both persistence modes. The writer starts/accepts using its own package
closure; the other revision performs model-free recovery, application/ack recovery and final verification
through normal CShell activation/readiness. Reports retain both source/version/receipt identities and
the actually used atomic/stream path. This proves this named unchanged-format pair only; it grants no
arbitrary downgrade, reverse migration, missing-key recovery or live-model support. Input receipts and
publications remain unchanged.

When the newer ordinary runtime has finalized `RuntimeWorkflowExecution` at 2.0.0 and the older binary
reads only 1.0.0, use `--expect-runtime-schema-rollback-refusal`. This profile still requires successful
old-to-new recovery in both modes. For new writes it requires the old binary's exact schema activation
refusal, read-only proof that accepted specialist/native/source state and the finalized version remain
unchanged, then successful recovery/settlement with the compatible new binary and one model dispatch.
The resulting status is `PassedUpgradeAndRollbackRefusal`, never bidirectional recovery. A refusal for
another cause, a successful incompatible rollback, changed state or repeated inference fails the driver.
This does not perform a reverse migration or test restoring a backup.
