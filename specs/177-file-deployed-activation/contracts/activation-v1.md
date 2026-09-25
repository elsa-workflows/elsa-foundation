# File-deployed activation v1 contract

Status: Draft for #2036. The first implementable slice is the safe candidate handoff. A `candidateMatch=verified` outcome requires the separately proven host-attestation gate in [research.md](../research.md); no current endpoint supplies it.

## Actors and authority

| Actor | Owns | Cannot infer |
|---|---|---|
| Developer / file bridge | Explicit host source selection, authored acceptance, redacted diff, fresh local candidate, invocation-local file recheck. | Deployed source, process overrides, loaded packages, active generation or database readiness. |
| External deployment owner | Immutable release/artifact integrity, expected-current deployment check, complete-bundle switch, previous release retention, source rollback. | Host used the release on reload, active shell readiness, data rollback. |
| Workbench root host | Authorized shell reload, per-shell lifecycle result, default-shell active generation and readiness. | That a local candidate equals its loaded source without generation-bound evidence; broader persistence safety from readiness alone. |
| Future server bridge | Presents a sanitized, authenticated result to a builder client, keeping host management credentials server-side. | Authority to invent a verified candidate match or expose raw blueprint configuration. |

The v1 target is the configured Workbench **default shell** in one explicit environment. File-only candidate generation continues to support other shells. A non-default shell must not receive a verified-ready result from Workbench's default-shell readiness endpoint.

## Candidate handoff

After explicit file-bridge review and candidate publication, the local handoff performs a fresh all-file check. A safe shareable projection may have this shape:

```json
{
  "schemaVersion": "1",
  "candidateId": "opaque-random-id",
  "host": "selected-host-alias",
  "shell": "default",
  "environment": "Production",
  "catalog": { "id": "reviewed-catalog", "version": "1", "digest": "catalog-definition-digest" },
  "acceptedFeatureIds": ["A"],
  "includedFileRoles": ["shells-base", "shells-selected-overlay", "appsettings-base", "appsettings-selected-overlay", "unselected-copy"],
  "unresolved": ["package-inventory-unchecked", "connection-target-unchecked"],
  "deploymentIntegrity": "external-attestation-required",
  "activation": "unchecked"
}
```

The example is schematic; `includedFileRoles` records one entry per included file and a safe disambiguator if a role repeats, without a physical path or raw filename that can escape the selected host. It must account for unselected files copied into the candidate. The catalog digest covers reviewed public catalog definitions, not source configuration. `candidateId` is a random label for one reviewed artifact; it is **not** a content hash, signed artifact, or replay authorization. The private invocation snapshot checks source/candidate bytes before handoff; it is never serialized into this projection. A later process with only this JSON must re-review or rely on a trusted external artifact receipt before treating files as unchanged. Do not put a public unkeyed digest of secret-bearing source files into the handoff.

If any included file changes during the handoff check, return `candidate-changed` and publish no successful handoff. An absent accepted composition, catalog mismatch, unsupported file shape, non-default activation target, or incomplete candidate returns a safe refusal. The existing bridge refusal codes remain the authority for its own preview/generation stage; this contract does not rename them.

## External deployment receipt

The deployment owner supplies a trusted receipt containing its release/artifact ID, selected host/environment, expected-current and actual-current release IDs, complete-switch outcome, and observation time. It must verify all files in the artifact under its own integrity mechanism and retain the previous complete release. Foundation does not specify the deployer's storage or atomic-switch mechanism in v1. If the owner cannot attest a complete switch and compare expected-current, report `deployment-unverified` and do not request a verified activation claim.

A successful receipt still does not prove the selected shell loaded that release. Environment and command-line providers can override JSON, and a process may have a different package generation. The host-attestation gate must decide how those facts are represented without exposing values.

## Host observation and outcome

The trusted operator invokes the existing root `POST /_admin/shells/reload/{name}` with the management credential held server-side, inspects its **body** (`success`, `newShell`, `drain`, `error`), then reads the selected shell's active generation and `/health/ready` for the configured default shell. A response transport status alone is insufficient. The CShells `GET /_admin/shells/{name}` payload includes raw blueprint configuration and must not be returned to a browser or shareable report; a trusted host observer extracts only safe generation/state fields. The existing Workbench management key is not a new user permission.

One operation keeps these dimensions separate:

| Dimension | Values | Evidence rule |
|---|---|---|
| Candidate | `reviewed`, `changed`, `absent` | File bridge plus local handoff check. |
| Deployment | `not-observed`, `switched`, `mismatch`, `uncertain` | Trusted external deployment receipt, not local file presence. |
| Reload | `not-attempted`, `success`, `error`, `uncertain` | Per-shell response and later host readback. |
| Active generation | observed generation number or `unknown` | Selected host registry/readiness; never a count alone. |
| Candidate match | `verified`, `mismatch`, `unverified` | `verified` requires a host-produced marker bound to the active generation and the exact reviewed bundle. Current Workbench evidence only permits `unverified`. |
| Broader checks | named `verified`/`unverified`/`refused` findings | Package, provider, connection, migration and transaction claims remain separately evidenced. |

A ready default shell with `candidateMatch=unverified` is reported as **active generation observed, candidate match unverified**. It is not described as “candidate active.” A failed reload can leave the prior generation ready; record both deployment source and that prior active generation. If the reload response is lost, read back deployment receipt and active generation before any retry. Readback may remain inconclusive; do not replace `uncertain` with a guessed success or failure.

## Recovery

A repair edits or regenerates a candidate and requires a fresh review. Source rollback is performed by the external deployment owner against its known previous complete release, then the operator explicitly reloads and observes the resulting generation. Neither path reuses the earlier candidate ID as an authorization token. A previous active generation may keep serving during a failed candidate build; the operator must still repair or roll back the deployed files before a later restart or reload. No result claims that schema migration, database contents or issued credentials were rolled back.

## Redaction and refusal classes

Shareable output can contain safe shell/feature IDs, logical resource names, file roles, generation numbers, stage codes, and a random operation/candidate ID. It cannot contain connection values, physical source paths, unknown raw settings, management keys, raw blueprint payloads, raw provider exceptions, private file tokens, or public unkeyed source-file digests. Stable stage/refusal classes for this contract are `candidate-changed`, `candidate-incomplete`, `deployment-unverified`, `deployment-conflict`, `reload-failed`, `reload-uncertain`, `candidate-match-unverified`, and `candidate-mismatch`. Implementation may add narrower safe codes after review, but must not merge these classes into generic `apply failed`.

## Validation gate

Use the [quickstart](../quickstart.md) and a disposable Workbench host. The candidate handoff is independently implementable with file-only tests. Before any implementation advertises `candidateMatch=verified`, demonstrate a host-produced, generation-bound marker that changes with every included source file, survives successful promotion, is absent on failed candidate promotion, distinguishes process overrides from source identity, and leaks neither canary. If this gate fails, keep the match unverified and file an investigation rather than weaken the contract.
