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
Expressions and the background feature dependencies, then resolves its scoped runtime services. It
checks that ordinary startup registered the specialist alias without manually calling product tasks.
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
