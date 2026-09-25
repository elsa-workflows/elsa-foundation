# Embedded runtime starting profile

`embedded-runtime@1` is the first Foundation-reviewed starting selection for a runtime in your own process. It expands to [16 explicit feature IDs](../../specs/178-embedded-runtime-profile/contracts/embedded-profile-v1.md), including `FileSystemDistributedLocking` and `WorkflowsRuntimeApi`. The definition is immutable and content-addressed. Your authored composition pins it, and adding/removing individual IDs remains possible; the planner shows the exact result and the reason for every included feature. A later catalog version cannot silently change an existing composition.

For a new local composition, use the bundled catalog:

```bash
dotnet elsa composition init --profile embedded-runtime@1 --output embedded-composition.json
dotnet elsa composition plan --composition embedded-composition.json --format json
```

The plan shows profile provenance, reviewed dependency edges and any supplied host inventory or persistence evidence. Without that evidence, package, provider, schema and migration readiness remain **unverified**. An explicit removal wins over profile membership. If it removes a known required member, generation refuses; CShells may otherwise auto-add that dependency during activation. After editing selection intent, review and update the accepted exact expansion before generating a candidate.

To prepare host files for one selected shell and environment:

```bash
dotnet elsa composition generate --host-dir ./host --shell default --environment Production --composition embedded-composition.json --output-dir ./candidate
```

The command displays a redacted diff and requires typing `generate` before writing a fresh candidate directory. It supports selected-environment object-map activation changes whose merge behavior is proven. It preserves base settings and other environments, then reads the candidate back to confirm that its enabled IDs exactly match the accepted plan. Array-shaped activation edits, base-disabled enablement and edits that would discard selected-overlay settings refuse. You can pass `--catalog` to plan/generate for an explicitly pinned catalog file; omitting it uses the bundled Foundation snapshot.

The profile contains **no** database provider or connection value, lock directory, signing key or migration authorization. Supply those through the host's configuration: a named persistence resource (the demonstrated example uses SQLite), an isolated writable `LocksFolderPath`, required signing values and deliberate migration preparation. The file bridge does not create new setting paths for you. It can patch only reviewed existing setting paths and resource references. A generated candidate is a file artifact, not proof that a deployed shell or its packages are ready.

The [generic-host proof](../../tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/EmbeddedFixtureHostEvidenceTests.cs) activates the exact published set with file locking and a named SQLite resource, then executes, suspends, resumes at a bookmark and completes. That fixture creates no ASP.NET listener. The selected Runtime API feature can expose routes when used in a web host; this profile does not promise an API-free selection. Multi-host locking, other providers, package loadability and live Workbench candidate attestation need separate evidence.
