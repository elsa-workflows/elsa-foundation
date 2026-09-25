# File-deployed activation v1 contract

Status: Draft for #2036. The safe candidate handoff is implemented. The #2039 [host-attestation decision](../research.md#3-existing-host-observations-prove-shell-lifecycle-not-exact-candidate-match) found no generation-bound reviewed-bundle marker in the current Workbench/CShells path. #2041 delivered sanitized default-shell observation with `candidateMatch=unverified`. The [#2046 hot-reload no-go](../../../docs/reports/runtime-composition/workbench-source-snapshot.md) and [#2060 restart proof](../../../docs/reports/runtime-composition/workbench-restart-snapshot.md) require a complete retained artifact and fresh process for startup-bound changes; `candidateMatch=verified` remains a separate, unimplemented gate.

## Actors and authority

| Actor | Owns | Cannot infer |
|---|---|---|
| Developer / file bridge | Explicit host source selection, authored acceptance, redacted diff, fresh local candidate, invocation-local file recheck. | Deployed source, process overrides, loaded packages, active generation or database readiness. |
| External deployment owner | Immutable release/artifact integrity, expected-current deployment check, complete-bundle switch, process restart, previous release retention, source rollback. | Host used the release on restart or reload, active shell readiness, data rollback. |
| Workbench root host | Authorized shell reload, per-shell lifecycle result, default-shell active generation and readiness. | That a local candidate equals its loaded source without generation-bound evidence; broader persistence safety from readiness alone. |
| Future server bridge | Presents a sanitized, authenticated result to a builder client, keeping host management credentials server-side. | Authority to invent a verified candidate match or expose raw blueprint configuration. |

The v1 target is the configured Workbench **default shell** in one explicit environment. File-only candidate generation continues to support other shells. A non-default shell must not receive a verified-ready result from Workbench's default-shell readiness endpoint.

## Candidate handoff

Pass `--handoff-host <safe-alias>` to `composition generate` to request a handoff in the same reviewed invocation. The alias is an operator label, never inferred from the host directory path. The existing generation command remains available without a handoff. After explicit file-bridge review and candidate publication, the local handoff rechecks the source and all published candidate files. The CLI prints one compact `handoff` JSON line only after those checks succeed. The shareable projection has this shape:

```json
{
  "handoff": {
    "schemaVersion": "1",
    "candidateId": "opaque-random-id",
    "host": "selected-host-alias",
    "shell": "default",
    "environment": "Production",
    "catalog": { "id": "reviewed-catalog", "version": "1", "digest": "catalog-definition-digest" },
    "acceptedFeatureIds": ["A"],
    "includedFiles": [{ "role": "shells-base", "ordinal": 1 }, { "role": "unselected-copy", "ordinal": 1 }],
    "unresolved": ["package-inventory-unchecked", "connection-target-unchecked"],
    "deploymentIntegrity": "external-attestation-required",
    "activation": "unchecked",
    "createdAt": "2026-09-25T00:00:00Z"
  }
}
```

The example is abbreviated: `includedFiles` contains **one entry for every copied file**, with a safe role and an ordinal that disambiguates repeated roles. It contains neither physical paths nor raw filenames. The catalog digest covers reviewed public catalog definitions, not source configuration. `candidateId` is a random label for one reviewed artifact; it is **not** a content hash, signed artifact, or replay authorization. The private invocation snapshot checks source/candidate bytes before handoff; it is never serialized into this projection. A later process with only this JSON must re-review or rely on a trusted external artifact receipt before treating files as unchanged. Do not put a public unkeyed digest of secret-bearing source files into the handoff. A failed post-publication check leaves a local directory that must be discarded or freshly reviewed; it emits no successful handoff.

If any included file changes during the handoff check, return `candidate-changed` and publish no successful handoff. An absent accepted composition, catalog mismatch, unsupported file shape, unsafe host alias, or incomplete candidate returns a safe refusal. File-only handoff can label another selected shell; it never asserts default-shell readiness or runtime activation. The existing bridge refusal codes remain the authority for its own preview/generation stage; this contract does not rename them.

## External deployment receipt

The deployment owner supplies a trusted receipt containing its release/artifact ID, selected host/environment, expected-current and actual-current release IDs, complete-switch outcome, and observation time. It must verify all files in the artifact under its own integrity mechanism, retain the previous complete release, and make the new complete artifact immutable before starting a Workbench process. A file-at-a-time copy from mutable source can mix root and shell revisions even at startup. Foundation does not specify the deployer's storage or atomic-switch mechanism in v1. If the owner cannot attest a complete switch and compare expected-current, report `deployment-unverified` and do not request a verified activation claim.

A successful receipt still does not prove the selected shell loaded that release. Environment and command-line providers can override JSON, and a process may have a different package generation. Until a new generation-bound marker is proven, represent those facts as unchecked without exposing their values.

## Host observation and outcome

Workbench exposes a root-host `GET /_admin/composition/default-shell/observation` and `POST /_admin/composition/default-shell/reload` behind its existing management key. The POST invokes the configured default shell's per-shell registry reload, examines the typed result rather than treating HTTP 200 as proof, and reads back the active generation/readiness. Its response includes `outcome` (`ready`, `reload-failed`, or `reload-uncertain`), `previousGeneration`, `reportedGeneration`, `activeGeneration`, `ready`, `processInstanceId`, and `candidateMatch=unverified`. The GET returns the point-in-time `shell`, `activeGeneration`, `ready`, `processInstanceId`, and `candidateMatch=unverified`. The opaque `processInstanceId` is stable for that Workbench process and changes on a fresh process start; pair it with the shell generation when comparing observations. Both responses omit raw blueprint, provider exception, configuration and credential values. A missing or incorrect management key is refused; no route accepts an arbitrary shell name. The existing CShells `GET /_admin/shells/{name}` can expose raw blueprint configuration and must not be returned to a browser or shareable report. A caller that loses the POST response may read back with GET, but that snapshot cannot establish which candidate, if any, was promoted; it must not automatically retry or infer success. The management key remains a server-side host credential, not a Foundation user permission.

The POST is a shell-only operation; it cannot apply startup-bound root settings. For those changes the external owner starts a new process from the retained artifact, then observes its default shell. The host's process instance ID distinguishes its own observations across restarts, because generation numbers can repeat across processes. It does not correlate the process to a deployment receipt or reviewed candidate. Neither a successful process start nor a later directory scan can change `candidateMatch` from `unverified` without the separately reviewed source/override and package evidence.

One operation keeps these dimensions separate:

| Dimension | Values | Evidence rule |
|---|---|---|
| Candidate | `reviewed`, `changed`, `absent` | File bridge plus local handoff check. |
| Deployment | `not-observed`, `switched`, `mismatch`, `uncertain` | Trusted external deployment receipt, not local file presence. |
| Activation attempt | `not-attempted`, `restart`, `shell-reload`, `error`, `uncertain` | External process manager for restart; per-shell response and later host readback for reload. |
| Active process/generation | observed process instance ID and shell generation, or `unknown` | Selected host observation distinguishes processes; deployment-owner correlation remains unproven. Generation is never globally comparable alone. |
| Candidate match | `verified`, `mismatch`, `unverified` | `verified` requires a host-produced marker bound to the active generation and the exact reviewed bundle. Current Workbench evidence only permits `unverified`. |
| Broader checks | named `verified`/`unverified`/`refused` findings | Package, provider, connection, migration and transaction claims remain separately evidenced. |

A ready default shell with `candidateMatch=unverified` is reported as **active generation observed, candidate match unverified**. It is not described as “candidate active.” This is the maximum positive outcome supported by the current Workbench observation path, even when the external deployer attests a complete switch. A failed reload can leave the prior generation ready; a failed restart preserves a previous process only when that instance is observed still serving. If either response is lost, read back deployment receipt and active process/generation before retry. Readback may remain inconclusive; do not replace `uncertain` with a guessed success or failure. A later marker design must separately prove exact reviewed-file, process-override, and package-generation correlation before this contract permits `verified`.

## Recovery

A repair edits or regenerates a candidate and requires a fresh review. Source rollback is performed by the external deployment owner against its known previous complete release, then the operator explicitly restarts or performs an eligible shell-only reload and observes the resulting process/generation. Neither path reuses the earlier candidate ID as an authorization token. A previous active instance may keep serving during a failed candidate build only if the host/deployment topology actually retains it; the operator must still repair or roll back the deployed files before a later attempt. No result claims that schema migration, database contents or issued credentials were rolled back.

## Redaction and refusal classes

Shareable output can contain safe shell/feature IDs, logical resource names, file roles, generation numbers, stage codes, and a random operation/candidate ID. It cannot contain connection values, physical source paths, unknown raw settings, management keys, raw blueprint payloads, raw provider exceptions, private file tokens, or public unkeyed source-file digests. Stable stage/refusal classes for this contract are `candidate-changed`, `candidate-incomplete`, `deployment-unverified`, `deployment-conflict`, `reload-failed`, `reload-uncertain`, `candidate-match-unverified`, and `candidate-mismatch`. Implementation may add narrower safe codes after review, but must not merge these classes into generic `apply failed`.

## Validation gate

Use the [quickstart](../quickstart.md) and a disposable Workbench host. The candidate handoff is independently implementable with file-only tests. #2039's current-host gate did not find a sound marker, so #2041's host observer returns `candidateMatch=unverified` and keeps exact candidate activation outside its success claim. #2060 additionally requires a retained complete artifact and process restart for startup-bound changes. Before any later implementation advertises `candidateMatch=verified`, demonstrate a trusted deployment receipt and host-produced process/generation-bound marker for every included source file, process override and applied package cohort; failed activation must not promote the marker, and neither canary may leak. Preserve this gate instead of weakening it.
