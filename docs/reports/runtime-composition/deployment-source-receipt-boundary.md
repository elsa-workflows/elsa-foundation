# Deployer-to-Workbench source receipt boundary (#2068)

Status: bounded test-owned proof on 2026-09-26. This follows the [restart-scoped file proof](workbench-restart-snapshot.md) and [spec 177](../../../specs/177-file-deployed-activation/spec.md). It does not add a production deployment receipt or change Workbench's `candidateMatch=unverified` result.

## Decision

**No-go for a verified deployed-source or candidate match in today's Workbench.** A test deployer can retain one complete six-file artifact, refuse a changed copy or override before launching a child, and associate its own receipt with the child it started. That association is test-side orchestration evidence. Workbench does not consume or independently attest the receipt, capture the process-start source and effective override winners, or bind them to the active shell generation. An echoed label, readiness result, process ID, or post-start directory scan would overstate what the host knows.

The useful next boundary is a host-owned startup context shared by root and shell composition. Until that exists, an external receipt is evidence of what the deployer published, not evidence of what the process loaded. Do not implement a `verified` candidate-match branch on the current observation endpoint.

## Authorities and private receipt contract

| Authority | Must establish | Current evidence |
| --- | --- | --- |
| Reviewed candidate | Accepted selection, catalog pin, complete reviewed output and its private byte identity | The [file handoff](../../../src/essentials/Modularity/Planning/Bridge/CompositionHandoff.cs) has an opaque ID and safe file roles. The portable ID is not a deployer integrity proof. |
| External deployer | The complete base, selected overlay, and unselected sibling set switched as one immutable retained release, plus the previous/current release relation | The [test fixture](../../../tests/essentials/Workbench/Tests/CompositionHostAttestationProbeTests.cs) keeps private byte snapshots and refuses mismatches. No production deployer contract exists. |
| Workbench process | The retained artifact opened at startup, selected environment, effective environment/argument winners and source provenance before root and shell bind | [Program.cs](../../../src/apps/Elsa.Workbench/Program.cs) layers JSON, environment and command line, but keeps a live `IConfiguration`; it records no source receipt. |
| Active shell | The process instance, shell generation and readiness bound to that startup context and applied package cohort | The [management observation](../../../src/apps/Elsa.Workbench/Composition/CompositionActivationObservationEndpoints.cs) exposes process identity and lifecycle only; package state is not content attestation. |

A future private deployment receipt should identify an opaque release and prior release, supported host and selected environment, a complete manifest including unselected files, an integrity result, and the deployer's atomic-switch/retention outcome. A future private process-start record should identify that same retained release through a trusted channel, the exact selected environment, the effective environment/argument winners and provenance, the process instance, and the context consumed by both root and shell. Secret values, paths, raw configuration, and any content hashes stay private. The outward view may expose fresh opaque correlation IDs, refusal classes, process instance, generation and readiness; it must not expose a reusable digest of secret-bearing bytes or treat a caller-supplied ID as authority. Offline inspection cannot prove that a running host opened the artifact or applied a package cohort.

The deployer must retain the complete immutable source through the process-start read; merely checking bytes and then releasing a mutable directory leaves a time-of-check/time-of-use gap. Workbench needs to capture a single startup context *before* root services bind and make the shell loader consume that context. An external receipt must be independently compared with this host-held context. The current code does neither. Shell reload of startup-bound inputs remains ineligible; a new process is required for those changes.

## Rebuilt-host fixture evidence

The [test-owned receipt probe](../../../tests/essentials/Workbench/Tests/CompositionHostAttestationProbeTests.cs) runs built Workbench as a child using [WorkbenchProcess](../../../tests/essentials/Workbench/Tests/WorkbenchProcess.cs). It snapshots all six base/Development/Staging `appsettings` and `shells` files privately, copies that full set to the child content root, and checks source and copy against the test receipt at the launch boundary. The child shows the retained CORS origin, while an environment override wins over the selected shell-file setting. The authenticated observer reports a ready shell, a process instance ID and `candidateMatch=unverified`; it does not echo the receipt. The test deployer can record the receipt-to-process association in its own memory, but the host response cannot establish that association by itself.

The fixture rejects an arbitrary receipt label, changed selected root file, changed unselected sibling, changed selected environment, changed override and source changed after receipt. A complete-looking mixed copy is refused by byte comparison. The earlier [#2060 host probe](workbench-restart-snapshot.md) supplies the important counterexample: without that external refusal, a mixed file-at-a-time copy starts a ready process with old root CORS and new shell configuration. These probes show what a trusted deployer **could** reject, not a deployed Workbench enforcement or protection against concurrent mutation after the test check. A failed start or readiness result would not be a positive activation receipt; the fixture does not prove production failure recovery.

The focused attestation probe ran 6/6 and the full rebuilt-host Workbench project ran 25/25 on this change. Reproduce the host evidence with `dotnet test tests/essentials/Workbench/Tests/Elsa.Workbench.Tests.csproj --no-restore`; the probe is `CompositionHostAttestationProbeTests`. The architecture suite ran 277/277, the generated-map check passed after refreshing the spec-status snapshot, and the solution-filter check remained green. No EF container or database-migration result is claimed by this spike.

## Required correlation before an activation claim

1. Compare a reviewed candidate's private full-bundle evidence with a trusted deployed-artifact receipt. The candidate's portable random ID alone cannot prove byte equality.
2. Compare the deployer receipt with a host-produced process-start record for the retained source and effective override winners. Refuse a missing, stale, mismatched, or self-asserted label. Bind the source context to root and shell configuration rather than inspecting disk later.
3. Compare an applied package cohort with the expected package contents. A Nuplane package ID/version or store-state entry alone is insufficient.
4. Bind the startup context and package cohort to the observed process, default-shell generation and readiness. Generation numbers are only meaningful within a process. Keep migration/provider/connection prerequisites separately unresolved until their own evidence is available.

The bounded next design question is how Workbench can take a retained source handle or equivalent immutable snapshot, capture effective override provenance at its earliest configuration boundary, and pass that context through root registration and shell activation. A production receipt story should be written only after that host ownership and the external deployer contract are proven. The current spike leaves spec 177 US2/US3 open and `candidateMatch=unverified`.
