# Composition inspect environment lane v1

This is the public command contract for the additive explicit-input lane. Existing `composition inspect` without `--environment-input` remains the candidate-v1/file-only operation.

## Command

```text
dotnet elsa composition inspect
  --host <installed-framework-dependent-host-output>
  --host-dir <configuration-source-directory>
  --shell <id> --environment <name>
  --composition <accepted-composition.json>
  --environment-input <private-environment.json>
  [--catalog <catalog.json>]
  [--workspace-profile <profile.json>]...
  [--setting-review <review.json>]
  [--packages <installed-package-root>]...
  --trust-host-code
  [--timeout-seconds <1..300>] [--format <text|json>]
```

`--environment-input` is one private regular JSON file and is valid only for `composition inspect`. It is captured once with the existing source/input ownership boundary and rechecked before launch and after the bounded worker exchange. Omission selects the old file-only lane. There is no inline-value option, restore, stdin ambiguity, ambient provider import, command-line import, custom provider, connection-value option, output directory, save, automatic-accept, service-prefix expansion, or unknown portable export.

`--host` selects an installed host closure, while `--host-dir` selects the source files used for the inspection. They may differ. The CLI requires `--trust-host-code` because selected host code executes in the owned child. The child is trusted host code and is not generally sandboxed.

## Capture and source policy

The public wrapper captures the accepted composition, selected host source files, explicit input file, optional catalog, workspace profiles, and setting review under existing regular-file/snapshot rules. It preserves unused supplied inputs for drift checks. User-provided file locations may be used by existing regular-file verification and loader metadata; the captured environment path is not sent to the child request, and no private input path/value is included in public diagnostics or results.

The candidate is built once from the captured bytes. The explicit overlay is associated with the same selected host, shell, environment, invocation, and candidate capture ID. A changed file, mixed capture, stale binding, disposed/reused capture, or changed accepted context refuses. The original input files are never overwritten or published, and no additional private artifact is created.

The EF-free worker loads the actual selected host through `HostClosure.LoadHostAssemblyForInspection` and validates only its name/location. It resolves the selected persistence assembly from the installed closure, binds the exact new capability before making an enrollment decision, and then invokes metadata-only `ToolingEntryPoint.ValidateCandidateEnvironmentEnrollment(hostAssembly, persistenceAssembly)`. The host repeats capability and enrollment metadata validation with its actual assemblies immediately before configuration/composer creation. Capability-unavailable takes precedence over host-unenrolled; there is no legacy fallback.

The enrolled Workbench policy interprets sources in this order:

```text
appsettings.json
appsettings.<environment>.json
shells.json
shells.<environment>.json
explicit environment overlay
```

This represents the supported configuration overlay only. It does not install or alter the worker process environment, import command-line configuration, observe deployed bootstrap state, establish physical readiness, or claim runtime parity.

## Public result

Human and JSON output are rendered from one validated result. JSON contains the existing safe composition plan plus `configurationResolution`. It does not contain the private worker/host envelope, environment values, raw configuration, private input path, exact file/key/provider provenance, exception excerpts, or any fingerprint derived from private input.

The projection uses:

```text
source: captured-workbench-json-explicit-environment-v1
externalInputs: supplied-intended
```

It may show safe feature/module/resource/provider/reference identities and truthful evidence states. It must preserve unavailable/not-performed/unobserved states and must not promote intended input to deployed attestation, physical readiness, connectivity, schema readiness, migration authorization, package reachability, activation, or save completion.

## Selection and recovery

The host reconciles accepted, requested, effective, disabled, implicit, and required-edge selections before `EfPersistencePreparation`. Valid authored removals remain removed. Divergence, a stale accepted identity, a removed feature remaining active, or a required dependency conflict refuses before preparation and does not rewrite accepted files.

Recovery is:

1. Edit the authored selection intent.
2. Run the existing interactive `composition accept` workflow to publish a fresh accepted file.
3. Start a fresh inspection capture with the explicit input.

The command does not silently accept an external environment-driven selection change, and it does not assume an acceptance command can observe ambient changes automatically.

## Exit and refusal behavior

| Outcome | Exit | Public result |
|---|---:|---|
| Valid bounded inspection, including truthful unavailable/unverified states | 0 | One safe human or JSON preview. |
| Invalid request/capture, malformed/null/tombstone entry, key collision, unsupported service prefix, raw environment bound, selection/EF conflict, missing trust, or cancellation with successful cleanup | 2 | No preview; fixed local refusal. |
| Shared serialized request too large (`candidate-request-too-large`) | 2 | No preview; existing shared worker refusal. |
| Stale response, closure drift, supplied-file drift, unenrolled/unavailable host or capability/package, malformed/oversized response, process failure, timeout, or cleanup failure | 3 | No preview; fixed local refusal after owned cleanup. A valid correlated unenrolled host response uses `candidate-environment-host-unenrolled` with exit 3. |
| Cancellation whose bounded cleanup fails | 3 | No preview; cleanup-failure classification. |

Admitted fixed local codes include `candidate-trust-required`, `candidate-environment-input-invalid`, `candidate-environment-input-too-large`, `candidate-environment-key-collision`, `candidate-environment-prefix-unsupported`, `candidate-environment-host-unenrolled`, `candidate-selection-conflict`, `candidate-request-too-large`, `candidate-capability-unavailable`, `candidate-response-invalid`, `candidate-response-too-large`, `candidate-inspection-timeout`, `candidate-inspection-cancelled`, `candidate-host-unavailable`, `candidate-closure-changed`, `candidate-package-unavailable`, `candidate-inspection-failed`, and `candidate-cleanup-failed`. Existing safe bridge/source-drift and EF refusal codes remain where they already own the boundary; the new lane validator owns the correlated unenrolled-host exit-3 response and does not reuse an old invalid-request writer that always returns exit 2.

The CLI prints only fixed local messages. It never prints raw peer messages, values, keys, entry indexes, source paths, private-input fingerprints, or captured configuration excerpts. Existing explicit CLI locations and finite assembly-loader/deps/package-root metadata may be used internally for file checks and host loading; they are not public result fields and are not a channel for private values.

## Compatibility

The old no-`--environment-input` command continues to produce candidate-v1/file-only behavior and uses the existing v1 response validator. The new flag negotiates the separate capability and lane-aware response validator. Unsupported hosts refuse rather than fall back to ambient input or change old candidate semantics. A fixture-only run is not sufficient public compatibility evidence; acceptance must exercise the built actual Workbench closure through the public CLI wrapper.
