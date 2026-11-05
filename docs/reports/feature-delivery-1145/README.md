# Remote feature delivery acceptance (#1145)

This fixture drives the published `Elsa.Foundation.Host` image through actual remote package acquisition,
file-based workflow reconciliation, authenticated Design/Publishing/Runtime APIs, and package compatibility
refusal. The host is never rebuilt or source-composed. See [issue #1145](https://github.com/elsa-workflows/elsa-foundation/issues/1145)
for the agreed scope and acceptance criteria.

## Verified result, 5 November 2026

The complete fresh-state replay exited zero. [Summary](evidence/summary.json) and
[environment](evidence/environment.json) pin the image, Linux/arm64 platform, SDK, fixture hashes and serving
process. The host acquired 69 packages from public remote feeds before the live addition. The final active
set contained 72 packages: the original remote set, the remotely added JSON reconciliation package and the
two compatible local witnesses. The incompatible witness was refused and never became active or loaded.

The final execution `01a1ade1b905729db04d87699be53999` was a persisted `PublishedRun`, `Completed` with
zero incidents and output `proof = remote-feed-execution-passed`. It ran after the incompatibility control,
on the same serving process used for cold boot and the live remote addition. The container is stopped;
its owned state and database are retained locally.

| Criterion | Public evidence |
| --- | --- |
| Pin and isolate | [Summary](evidence/summary.json), [environment](evidence/environment.json), [launch](evidence/attempt-11-launch.json), [image dependencies](evidence/host.deps.json), [production declarations](evidence/host.production.json), [initial feed configuration](evidence/initial-appsettings.json) |
| Remote acquisition and activation | [Exact remote-pin validation](evidence/remote-pin-validation.json), [declared pins](evidence/declared-remote-pins.json), [cold inventory](evidence/cold-inventory.json), [initial readiness](evidence/readiness.json), [live remote addition](evidence/live-remote-reconcile.json), [final discovery](evidence/final-directory-witness.json) |
| File reconciliation, publication and execution | [Authored catalog](evidence/workflow-catalog.json), [definition read](evidence/workflow-definition.json), [publication](evidence/workflow-publish.json), [execution](evidence/workflow-execute.json), [completed instance and output](evidence/workflow-instance.json) |
| Directory coexistence | [Directory witness](evidence/directory-witness.json), [compatible witness](evidence/compatible-witness.json), [final package inventory](evidence/final-inventory.json), workflow execution after the controls |
| Compatibility refusal and positive control | [Exact diagnostic and remedy](evidence/compatibility-diagnostic.json), [before/after loaded-assembly control](evidence/incompatible-control.json), [active state and failure](evidence/incompatible-state.json), [compatible activation](evidence/compatible-witness.json) |
| Reproducible running-host path | The runner below, [live manifest](evidence/live-manifest.json), [live reconciliation response](evidence/live-remote-reconcile.json), [shell reload](evidence/live-feature-reload.json), and the same-process assertion in the summary |

The [compressed sanitized log](evidence/host.log.gz) retains the full final-run timeline.
[Published SHA-256 values](evidence/published-sha256.json) cover the committed artifacts;
[original receipt SHA-256 values](evidence/sha256.json) cover the runner's original files, with `host.log`
verified after decompressing `host.log.gz`. Every original receipt and fixture-source hash was checked
before publication, and the public receipts were scanned against the generated private values.
This is execution evidence for review; issue closure remains subject to acceptance review.

## Replay

Prerequisites: Docker, .NET SDK 10, Python 3 and OpenSSL. The runner uses the machine's ordinary `dotnet`
command, including any build-slot wrapper. Run from any directory, using a new output directory and an
unused attempt number (the task-owned container is named `fr2-1145-host-<number>`):

```bash
python3 /path/to/elsa-foundation/docs/reports/feature-delivery-1145/run.py \
  /absolute/path/to/new-empty-fr2-run --attempt 1
```

`pins.json` freezes the root package versions from the recorded publish state. `runtime-pins.json` adds
the exact dependency required by Nuplane’s nuspec walk but pruned by the framework-aware restore. `prepare.py` pins the image
by its OCI index digest. The runner pulls that image, extracts its actual dependency metadata and CShells
contracts, and restores the package roots in a separate folder to compute their closure and origins.
Neither that restore cache nor the fixture build output is mounted into the serving host. The host receives
empty package state, public feed URLs, exact package pins, shell configuration and an initially empty catalog.

The run uses Linux containers, a single-host SQLite database, HTTPS on an automatically assigned loopback
port, Production configuration and an explicitly seeded administrator. It generates fresh credentials,
signing material and a self-signed fixture certificate. Those private files stay in the output directory and
must not be uploaded. The Docker probe is overridden to use the configured HTTPS readiness endpoint.
The fixture permits `AutoMigrate` only against its fresh, owned database. Existing hosts and stores are unused.

The runner stops only its own serving container in `finally` and retains the container, database, package
state, configuration and receipts for inspection. It never removes another host or prunes Docker resources.

## Running-host operator path

The owner selected Nuplane's existing opt-in desired manifest for the live-addition leg and explicitly
selected `Nuplane:Loading:DefaultLoadMode=HostIntegrated` for the NativeEndpoints API composition. Endpoint
metadata validation stays enabled. This proof makes no claim about unloading assemblies.

1. Configure the feed service indexes and initial exact include pins. Enable
   `Nuplane:Convergence:Manifest` with `Enabled=true`, `SchemaVersion=1.0` and a mounted `Path`.
   The initial manifest has an empty `Packages` array. The JSON reconciliation package is absent from the
   initial remote include pins and active package inventory.
2. Write an exact manifest entry for `Elsa.Workflows.Design.Reconciliation.Json`, using `Id`, `Version`
   and `SourceHint=elsa-4`. Manifest versions are plain exact versions, without NuGet range brackets.
3. Call `POST /_module-management/reconcile` with `X-Elsa-Module-Management-Key`. Require an applied
   addition for that package and source, with no failed packages. The runner retains the response.
4. Write the authored workflow catalog and enable `JsonWorkflowReconciliation` in `shells.json` with
   `Options.SourceId`, `Options.FilePath` and `PublishOnReconcile=false`. Allow the configuration watcher
   to observe the change, then call `POST /_module-management/reload`. The fixture enables polling for
   bind-mounted file watching and waits for the definition to become visible rather than equating a reload
   response or a fixed delay with completed reconciliation.
5. Log in to the actual Identity API. Read the reconciled definition through the Design API, publish its
   exact version, execute the returned artifact with its `sourceReferenceId`, and poll the Runtime instance
   API until completion. Require the exact expected output and zero incidents.

The runner compares the serving container identity, process ID and start timestamp across these steps.
No host restart or image change occurs. The [feed guide](../../host-feeds/feeds.md),
[pinning guide](../../host-feeds/pinning-and-closures.md) and
[reload guide](../../host-feeds/updating-packages.md) remain the operator references.

## Witnesses and evidence

`fixtures.py` builds three tiny assemblies against the CShells contracts extracted from the pinned image.
Their NuGet packages are test-only local directory-feed witnesses:

- `Fr2Proof.Directory` provides a serving route reporting the process, shell/catalog generation, discovered
  feature IDs and loaded assembly names. It supplies no missing Elsa implementation binary.
- `Fr2Proof.Compatible` declares an `Elsa.Primitives` floor equal to the host's actual version and must
  activate and serve.
- `Fr2Proof.Incompatible` declares `[4.0.999-preview,5.0.0)`, exceeding the pinned host's version. It must
  be refused with `host-version-unsatisfied`, remain absent from both active package state and loaded
  assemblies, and leave the earlier shell serving. The workflow path is exercised again after this refusal.

The output `receipts/` directory contains the criterion receipts, initial/final configurations, complete package
inventories with feed/source provenance, actual API responses, a sanitized host log and SHA-256 inventory.
The serving host's package cache begins empty. A separate restore performed by the driver cannot satisfy the
serving host's remote-acquisition requirements because that folder is never mounted into the container.

The committed `evidence/` directory records the verified run's public receipts. Private credentials, cookies,
TLS private material, raw databases, restored packages and built package binaries are excluded. Rebuild the
three local witnesses from the scripts when replaying.

## Earlier attempts

Retained exploratory attempts identified and corrected fixture configuration: explicit `ef-provider=Sqlite`,
the `Elsa.Primitives.Hosting` and Runtime Resumption roots, matching IAM connection options, and the recurring
trigger projection needed by aggregate EF publication. The default collectible load mode refused API response
metadata; the owner selected HostIntegrated instead. A driver check was also corrected to observe the original
authentication challenge rather than following its redirect to the HTML login page. These attempts are not
reported as completed acceptance runs. No product code was changed to make them pass.

## Review corrections

Code Review round 1 identified an optional scanner false positive in the password-construction expression
and a log filter that skipped values containing `=`. The generator now chooses its password-policy characters
randomly, and log redaction includes padded base64 values. The [regression receipt](evidence/redaction-regression.json)
records the saved-log test failing against the original source and passing after the correction. The entire
fresh-state proof was rerun after both changes; the published receipts and fixture-source hashes are from that run.

Independent merge review also found that a diagnostic-write failure could skip container cleanup, and that
Nuplane acquired `System.Text.Json` outside the framework-aware restore closure. Cleanup now stops its
owned container in a nested `finally`, even when snapshotting fails. The runtime dependency is explicitly
pinned, the SQLite capability contribution has an exact version and feed, and cold/final remote inventories
must exactly match declared IDs, versions and feeds. [Failure-injection receipts](evidence/review-regressions.json)
show both regressions failing without their enforcement and passing after the changes. The complete
fresh-state proof was replayed after these corrections; it has zero undeclared remote acquisitions.
