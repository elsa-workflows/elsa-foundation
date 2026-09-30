# Embedded runtime starting profile

`embedded-runtime@1` is the first Foundation-reviewed starting selection for a runtime in your own process. It expands to [16 explicit feature IDs](../../specs/178-embedded-runtime-profile/contracts/embedded-profile-v1.md), including `FileSystemDistributedLocking` and `WorkflowsRuntimeApi`. The definition is immutable and content-addressed. Your authored composition pins it, and adding/removing individual IDs remains possible; the planner shows the exact result and the reason for every included feature. A later catalog version cannot silently change an existing composition.

For a new local composition, use the bundled catalog:

```bash
dotnet elsa composition init --profile embedded-runtime@1 --output embedded-composition.json
dotnet elsa composition plan --composition embedded-composition.json --format json
```

The plan shows profile provenance, reviewed dependency edges and any supplied host inventory or persistence evidence. Without that evidence, package, provider, schema and migration readiness remain **unverified**. An explicit removal wins over profile membership. If it removes a known required member, generation refuses; CShells may otherwise auto-add that dependency during activation. A [reviewed diagnostics group](diagnostics-ef-group.md) can be added with `--group diagnostics-ef@1` when creating a new composition.

After editing the profile/group selection or individual additions/removals, inspect the plan and accept its exact expansion to a fresh file:

```bash
dotnet elsa composition plan --composition embedded-composition.json --format json
dotnet elsa composition accept --composition embedded-composition.json --output accepted-composition.json
```

Acceptance displays the exact added/removed IDs and retained/dropped lock IDs, then requires typing `accept`. It preserves authored settings, resources and pins, retaining historical locks only for features still selected. It rechecks every supplied input before writing and never overwrites an existing file. Missing host and persistence evidence stays unverified. Use the resulting `accepted-composition.json` with generation. For a custom workspace profile, pass the same pinned file with `--workspace-profile` to each of plan, accept, and generate; its [generation delivery gate](../reports/runtime-composition/workspace-profile-generation.md) is tracked by [#2166](https://github.com/elsa-workflows/elsa-foundation/issues/2166).

To prepare host files for one selected shell and environment:

```bash
dotnet elsa composition generate --host-dir ./host --shell default --environment Production --composition accepted-composition.json --output-dir ./candidate
```

The command displays a redacted diff and requires typing `generate` before writing a fresh candidate directory. It supports selected-environment object-map activation changes whose merge behavior is proven. It preserves base settings and other environments, then reads the candidate back to confirm that its enabled IDs exactly match the accepted plan. Array-shaped activation edits, base-disabled enablement and edits that would discard selected-overlay settings refuse. You can pass `--catalog` to plan/generate for an explicitly pinned catalog file; omitting it selects the bundled Foundation snapshot matching the composition's catalog pin. Workspace-profile definitions remain selection inputs and are not copied into the candidate host files.

The profile contains **no** database provider or connection value, lock directory, signing key or migration authorization. Supply those through the host's configuration: a named persistence resource (the demonstrated example uses SQLite), an isolated writable `LocksFolderPath`, required signing values and deliberate migration preparation. The file bridge does not create new setting paths for you. It can patch only reviewed existing setting paths and resource references. A generated candidate is a file artifact, not proof that a deployed shell or its packages are ready.

The [generic-host proof](../../tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/EmbeddedFixtureHostEvidenceTests.cs) activates the exact published set with file locking and a named SQLite resource, then executes, suspends, resumes at a bookmark and completes. That fixture creates no ASP.NET listener. The selected Runtime API feature can expose routes when used in a web host; this profile does not promise an API-free selection. Multi-host locking, other providers, package loadability and live Workbench candidate attestation need separate evidence.
