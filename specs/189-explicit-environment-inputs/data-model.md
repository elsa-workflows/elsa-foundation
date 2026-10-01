# Data model: Explicit private environment inputs

This model turns the closed choices in [research.md](./research.md#d11-closed-contract-choices-for-phase-1) into implementation boundaries. Names and fields are proposed planning shapes; they are not existing production APIs.

Planned source seams are `src/essentials/Cli/Worker/HostClosure.cs` for the inspection-only host loader helper, `src/essentials/Persistence/EntityFramework/Tooling/EfCandidateEnvironmentInputsAttribute.cs` for the assembly declaration, `src/essentials/Persistence/EntityFramework/Tooling/EfCandidateEnvironmentInspectionContract.cs` for the public capability version, and `src/essentials/Persistence/EntityFramework/Tooling/EfToolingHost.cs` for the public host method. Existing `EfCandidateInspectionContract.cs`, `EfToolingContract.cs`, and `EfToolingHost.cs` remain compatibility seams.

## Ownership and lifecycle

| Entity | Owner | Purpose and invariants |
|---|---|---|
| `ExplicitEnvironmentDocumentV1` | CLI capture | The complete explicitly supplied overlay document. It is strict UTF-8 JSON, optionally prefixed by one UTF-8 BOM that counts toward the 1 MiB raw bound. Exactly `version` and `entries` are accepted at the document root. |
| `EnvironmentEntryV1` | CLI capture | Exactly string `key` and string `value`. Empty values are meaningful. `null`, tombstone, delete, nested objects, and unknown entry fields refuse. |
| `CapturedEnvironmentInputV1` | CLI/worker private channel | Version 1, the candidate's capture ID, and canonical base64 of the original captured raw bytes. The source file is captured once, defensively copied, and rechecked before launch and after the result/refusal. |
| `CandidateInspectionPayloadV1` | Existing CLI/worker contract | Existing `captured-workbench-json-v1` candidate payload containing the selected file-only candidate. Its version, fields, source literal, and validator remain unchanged. |
| `CandidateEnvironmentWorkerRequestV2` | Worker client | Outer WorkerContract v2 request with exactly the new command's allowed fields: version, command, hostDirectory, hostName, depsFile, packageRoots, candidate, and environmentInput. Old commands still reject `environmentInput`, including explicit `null`. |
| `CandidateEnvironmentHostRequestV1` | Host operation | Inner host request with exactly version, `host:{name,directory}`, `candidate`, and `environmentInput`. The environment capture ID equals the nested candidate capture ID; the candidate invocation ID binds the whole operation. |
| `EfCandidateEnvironmentInputsAttribute` | Selected host assembly | Assembly declaration `(Version=1, Policy="workbench-json-explicit-environment-v1")`. `HostClosure.LoadHostAssemblyForInspection(string,string):Assembly` only returns the actual assembly after name/location validation. After resolving the selected persistence assembly from the installed closure, the EF-free worker binds the exact new capability first and calls metadata-only `ToolingEntryPoint.ValidateCandidateEnvironmentEnrollment(Assembly hostAssembly, Assembly persistenceAssembly):void`; that validator uses `CustomAttributeData` on the actual host assembly to find exactly one declaration with the selected persistence-assembly attribute identity and exactly two positional constructor arguments. Method presence, host name, or shared EF assembly membership does not enroll a host. |
| `SelectionReconciliation` | Host | Accepted, requested, effective, disabled, implicit IDs and required edges. A successful result requires the agreed accepted/requested/effective relationship; valid authored removals remain disabled and are not silently rewritten. |
| `ConfigurationResolutionProjection` | Host, validated by worker and CLI | Safe logical participant/resource/provider/reference/scope rows and evidence states. It contains no raw configuration, values, private paths, exact-file provenance, or private-input fingerprints. |
| `CandidateEnvironmentInspectionResponseV1` | Host private response | Correlated version-1 response with status, exit code, and exactly one safe resolution or fixed-code error. It requires the new source and `externalInputs:supplied-intended`; the old response validator remains closed. |

## `ExplicitEnvironmentDocumentV1`

The raw document is:

```json
{
  "version": 1,
  "entries": [
    { "key": "ConnectionStrings__Primary", "value": "Data Source=:memory:" },
    { "key": "EmptyValue", "value": "" }
  ]
}
```

Admission rules:

- Root fields are exactly `version` and `entries`; duplicate JSON properties, unknown fields, wrong types, and nested/tombstone forms refuse.
- `version` is integer `1`. `entries` may be empty and has at most 1,024 elements.
- A key is a nonempty valid Unicode scalar sequence, at most 1,024 UTF-8 bytes in both raw and normalized form. Reject Unicode control characters, `=`, and unpaired UTF-16 surrogates. Do not trim, case-fold, or Unicode-normalize it; a nonempty whitespace key remains literal.
- A value is a JSON string of at most 65,536 UTF-8 bytes. Blank values, newline, and tab are valid. Reject NUL and unpaired surrogates. `null` is not a value.
- The original raw UTF-8 document, including an optional leading BOM, is at most 1 MiB. Base64 transport bytes and the final serialized request are separately bounded.
- Normalize by replacing every `__` with `:`. Compare normalized keys with `StringComparer.OrdinalIgnoreCase` before provider processing. Duplicate raw entries and normalized collisions refuse; provider enumeration order never selects a winner.
- Refuse all eleven raw service-prefix families case-insensitively before normalization: `MYSQLCONNSTR_`, `SQLAZURECONNSTR_`, `SQLCONNSTR_`, `CUSTOMCONNSTR_`, `POSTGRESQLCONNSTR_`, `APIHUBCONNSTR_`, `DOCDBCONNSTR_`, `EVENTHUBCONNSTR_`, `NOTIFICATIONHUBCONNSTR_`, `REDISCACHECONNSTR_`, and `SERVICEBUSCONNSTR_`.
- Ordinary `ConnectionStrings__<name>` is admitted under the same grammar and normalization rules. Omission contributes no external entry and does not delete or mask a lower source.

## Capture and binding state

The lifecycle is:

```text
source file admitted
  -> raw bytes captured once
  -> document parsed and normalized for admission checks
  -> candidate and environment capture IDs bound
  -> candidate built from the same captured source context
  -> pre-launch source recheck
  -> owned worker exchange
  -> post-result source recheck
  -> safe response rendered
```

Reloading the detached frozen root and mutating a caller-owned dictionary or document after defensive copying must leave the owned capture stable; it must not reread ambient state. Actual supplied-file drift, mixed generation, changed selected binding, disposed/reused capture, cancellation, timeout, malformed response, or cleanup failure ends in refusal. The original operator-owned file is preserved; no additional persisted private copy is created. Capture IDs are admitted correlation tokens that detect mixed captures; they are not signatures, byte attestations, authentication receipts, or proof against malicious trusted composer code forging the same valid tokens.

## Configuration and selection relationships

The host builds one configuration policy from the frozen candidate files and explicit entries. The supported Workbench order is appsettings base/environment, shells base/environment, then the explicit environment overlay at the environment stage. Command-line, caller ambient, custom providers, installation/bootstrap state, deployed attestation, and physical readiness are outside this model.

The host uses its existing configuration builder and `EfToolingConfigurationContext.ComposeShell` with the CShells descriptor resolver to derive requested/effective/disabled selections, then calls the existing `EfPersistencePreparation` once. The frontend `CompositionCandidateBuilder` remains a CLI/Planning concern and is not duplicated in the host. Reconciliation precedes preparation. Required-edge conflicts, stale accepted identities, case collisions, or a removed feature remaining active refuse. A valid authored removal is retained. Recovery is authored edit, existing interactive `composition accept`, and a fresh capture.

The worker first uses `HostClosure.LoadHostAssemblyForInspection(string,string):Assembly` to return the actual loaded assembly after validating only its name and location against the selected layout. It resolves the selected persistence assembly through the existing installed-closure loader, binds the exact new capability before making an enrollment decision, and then calls metadata-only `ToolingEntryPoint.ValidateCandidateEnvironmentEnrollment(Assembly hostAssembly, Assembly persistenceAssembly):void`. Capability failure takes precedence over enrollment failure. The existing public `void LoadHostAssembly(...)` signature remains unchanged; the inspection helper may share private loading code. The host repeats capability/enrollment metadata validation with its actual host and selected persistence assemblies immediately before configuration construction/composer creation. An unenrolled Foundation Host or other host refuses before configuration construction. The trusted selected-host execution boundary is an owned worker boundary; it is not a general sandbox.

## Resolution projection and evidence state

The safe projection may retain existing candidate fields and uses:

```text
source: captured-workbench-json-explicit-environment-v1
externalInputs: supplied-intended
```

It inherits all closed safe-response semantics, enums, affinity states, legacy-target handling, participant/finding rules, identity limits, and unavailable evidence states from [Spec 187's candidate-v1 contract](../187-effective-persistence-preview/contracts/candidate-inspection-v1.md). This lane changes only the source literal, `externalInputs`, and its new fixed refusal codes; it does not promote any evidence or add per-key provenance. It may contain sorted safe feature/module identities, logical resource/provider/reference names, selection/resource scopes, and truthful states such as unverified, not-performed, or unobserved. It must not contain values, raw configuration, exception excerpts, private-input paths, exact-file/key/provider provenance, deployment/physical-readiness claims, or any fingerprint derived from private input. Existing CLI file-location and assembly-loader paths may remain private loader metadata where needed and are not public projection fields.

The old candidate-v1 response source and validator remain unchanged. The new lane has a separate response-aware validator that rejects old-source, wrong-mode, stale-token, additional-field, or readiness-promoting responses.

## Bounds

The existing selected file limits remain: 1 MiB decoded per file, 4 MiB decoded selected-file aggregate, 8 MiB final serialized request, 4 MiB response, JSON depth 64, a combined participant-plus-finding projection limit of 1,024, and existing 4,096 selection IDs per set. Four 1 MiB files consume 5,592,416 base64 bytes and the 1 MiB environment document consumes 1,398,104, leaving 1,398,088 bytes of the 8,388,608-byte request ceiling for envelopes and metadata. The final serialized bound is authoritative even when every component cap passes. Exact-bound and one-over cases belong to the existing fixtures and remain planned evidence.
