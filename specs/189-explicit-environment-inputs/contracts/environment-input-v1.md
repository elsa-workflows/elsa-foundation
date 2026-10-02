# Environment input protocol v1

This is the proposed additive host capability for [#2277](../spec.md). It does not modify the existing candidate-v1 contract. The public CLI and worker documents are in [cli-inspect-environment-v1.md](./cli-inspect-environment-v1.md).

Planned public/source seams are `src/essentials/Persistence/EntityFramework/Tooling/EfCandidateEnvironmentInspectionContract.cs`, `EfCandidateEnvironmentInputsAttribute.cs`, and `EfToolingHost.cs`; the EF-free loader helper is in `src/essentials/Cli/Worker/HostClosure.cs`. Existing `EfCandidateInspectionContract.cs`, `EfToolingContract.cs`, and the public `EfToolingHost` entry points remain compatibility seams.

## Capability and enrollment

The selected host advertises this exact additive capability:

```csharp
public static class EfCandidateEnvironmentInspectionContract
{
    public const int Version = 1;
}

public static Task<int> RunCandidateEnvironmentInspectionAsync(
    Stream request,
    Stream response,
    CancellationToken cancellationToken);
```

Admission order is fixed. First validate the outer request and selected host layout/closure, then load the actual host assembly and resolve the selected persistence assembly. The EF-free worker next binds the exact capability from that selected assembly. A missing, partial, wrong-version, or wrong-signature capability returns `candidate-capability-unavailable` with exit 3 and never falls back to `RunCandidateInspectionAsync` or another older method. Only after capability binding succeeds does it call the metadata-only enrollment validator on the actual host and selected persistence assemblies. A missing, duplicate, wrong-identity, wrong-version, or wrong-policy declaration returns `candidate-environment-host-unenrolled` with exit 3. Therefore a fully new-capability Foundation Host without enrollment is unenrolled, while an old host lacking the capability is capability-unavailable.

The first lane is enrolled only by exactly one declaration on the actual selected host assembly. The inspection-only loader helper is `HostClosure.LoadHostAssemblyForInspection(string,string):Assembly`: it returns the actual loaded assembly after validating its name and location against the selected layout. It does not inspect enrollment. After the selected persistence assembly is resolved from the installed closure, the EF-free worker binds the exact new capability first and then calls the metadata-only `ToolingEntryPoint.ValidateCandidateEnvironmentEnrollment(Assembly hostAssembly, Assembly persistenceAssembly):void`. That validator reads `CustomAttributeData` from the actual host assembly, checks the attribute type identity from the selected persistence assembly, and requires exactly two positional constructor arguments, integer `1` and the literal policy, with no named arguments. The existing public `void LoadHostAssembly(...)` signature remains unchanged; private loading can be shared.

```text
[assembly: EfCandidateEnvironmentInputs(1, "workbench-json-explicit-environment-v1")]
```

The exact attribute contract is:

```csharp
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class EfCandidateEnvironmentInputsAttribute : Attribute
{
    public EfCandidateEnvironmentInputsAttribute(int version, string policy)
    {
        Version = version;
        Policy = policy;
    }

    public int Version { get; }
    public string Policy { get; }
}
```

Declarations have exactly two positional constructor arguments, integer `1` and the literal policy, with no named arguments. The attribute has read-only `Version` and `Policy` values and allows duplicate declarations to be detected and refused. The worker verifies the declaration through that metadata-only validator before configuration construction, and the EF-owned host operation repeats the same metadata checks with its actual host and selected persistence assemblies immediately before building configuration or creating the composer. The host does not reference the CLI-owned `ToolingEntryPoint` helper. Method presence, a host name, or membership in the shared EF assembly does not enroll Foundation Host or another host. Existing composer, dependency-path, runtimeconfig, deps, and installed package guards remain authoritative.

## Raw document

The user-supplied private file is strict UTF-8 JSON. One optional leading UTF-8 BOM is accepted and counts toward the raw 1 MiB bound. Parsing may remove that prefix for JSON parsing, but capture and drift comparison retain the original bytes.

The raw document has JSON maximum depth 64, no comments, and no trailing commas. `version` must be the JSON integer `1` rather than a string, and duplicate property names are refused at every closed object level under an Ordinal, case-sensitive JSON property-name policy. Unknown fields and wrong types refuse. The implementation uses a deterministic first-failure order for malformed input, or one fixed `candidate-environment-input-invalid` classification when more than one invalidity is present; it never discloses the failing key, index, or value. Entry-key collision comparison remains the separate normalized OrdinalIgnoreCase rule below.

The root object is closed:

```json
{
  "version": 1,
  "entries": [
    { "key": "ConnectionStrings__Primary", "value": "Data Source=:memory:" },
    { "key": "EmptyValue", "value": "" }
  ]
}
```

Rules:

| Element | Admission |
|---|---|
| Root | Exactly `version` and `entries`; duplicate properties, unknown fields, wrong types, nested values, tombstones, and delete/remove forms refuse. |
| `version` | Integer 1. |
| `entries` | Zero to 1,024 entries. An empty array is a valid explicit empty overlay. |
| `key` | Required nonempty valid Unicode scalar sequence; at most 1,024 UTF-8 bytes in raw and normalized form. Reject Unicode controls, `=`, and unpaired UTF-16 surrogates. Do not trim, case-fold, or Unicode-normalize. |
| `value` | Required JSON string; at most 65,536 UTF-8 bytes. Blank strings, newline, and tab are valid. Reject NUL and unpaired surrogates. No value trim, normalization, or transformation is performed. |
| Raw document | At most 1 MiB including an optional BOM. Count bytes before allocation. |

The parser replaces every `__` with `:` and compares normalized keys using `StringComparer.OrdinalIgnoreCase` before provider admission. Duplicate raw keys, case aliases, and normalized `__`/`:` collisions refuse. Omission contributes no entry and does not remove a lower source. All eleven standard service prefixes are refused case-insensitively before normalization:

```text
MYSQLCONNSTR_ SQLAZURECONNSTR_ SQLCONNSTR_ CUSTOMCONNSTR_
POSTGRESQLCONNSTR_ APIHUBCONNSTR_ DOCDBCONNSTR_ EVENTHUBCONNSTR_
NOTIFICATIONHUBCONNSTR_ REDISCACHECONNSTR_ SERVICEBUSCONNSTR_
```

Ordinary `ConnectionStrings__<name>` is supported under these rules. Service-prefix expansion is not silently reproduced.

## Outer worker request

The outer transport remains WorkerContract version 2. The new command is `inspect-candidate-environment`. The following is one complete harmless syntactic example; the `/example` paths are illustrative values, not private source paths:

```json
{
  "version": 2,
  "command": "inspect-candidate-environment",
  "hostDirectory": "/example/host",
  "hostName": "Example.Host",
  "depsFile": "/example/host/Example.Host.deps.json",
  "packageRoots": ["/example/packages"],
  "candidate": {
    "version": 1,
    "source": "captured-workbench-json-v1",
    "invocationId": "11111111111111111111111111111111",
    "captureId": "22222222222222222222222222222222",
    "shell": "default",
    "environment": "Production",
    "acceptedFeatureIds": [],
    "removedFeatureIds": [],
    "files": [
      { "name": "appsettings.json", "captureId": "22222222222222222222222222222222", "content": "e30=" },
      { "name": "shells.Production.json", "captureId": "22222222222222222222222222222222", "content": "eyJDU2hlbGxzIjp7IlNoZWxscyI6eyJkZWZhdWx0Ijp7fX19fQ==" },
      { "name": "shells.json", "captureId": "22222222222222222222222222222222", "content": "eyJDU2hlbGxzIjp7IlNoZWxscyI6eyJkZWZhdWx0Ijp7IkZlYXR1cmVzIjp7fX19fX0=" }
    ]
  },
  "environmentInput": {
    "version": 1,
    "captureId": "22222222222222222222222222222222",
    "content": "eyJ2ZXJzaW9uIjoxLCJlbnRyaWVzIjpbXX0="
  }
}
```

`candidate` is exactly the existing v1 payload with source `captured-workbench-json-v1`; its fields, capture ID, invocation ID, file limits, and selected file semantics are unchanged. The new environment input contains exactly `version`, `captureId`, and canonical base64 `content`. The environment capture ID must equal the candidate capture ID. The candidate's capture-owned invocation ID binds the whole operation. The private source file path is not sent in this request; the content bytes are.

The existing candidate command and every other old command reader continue to reject every added field, including explicit `environmentInput:null`. The implementation must audit all old command validators/readers rather than rely on one shared DTO's optional property. No shared DTO change may broaden an old parser. Strict closed-object, duplicate-property, depth, and byte-bound rules apply independently at every outer, nested candidate, environment-input, host, response, selection, participant, and error object. Both worker and host check the closed shape, byte limits, and binding independently.

The 8 MiB serialized request ceiling is final and includes all base64 and metadata. Existing selected-file limits remain 1 MiB decoded each and 4 MiB decoded aggregate; the response remains at most 4 MiB and JSON depth at most 64. At most 1,024 combined participant-plus-finding projection rows and 4,096 selection IDs per set are admitted. Exact-bound and one-over cases must be proven in existing fixtures.

## Inner host request

The host method receives a separate closed version-1 envelope. The following complete harmless example uses the same candidate and captures the selected host metadata explicitly:

```json
{
  "version": 1,
  "host": { "name": "Example.Host", "directory": "/example/host" },
  "candidate": {
    "version": 1,
    "source": "captured-workbench-json-v1",
    "invocationId": "11111111111111111111111111111111",
    "captureId": "22222222222222222222222222222222",
    "shell": "default",
    "environment": "Production",
    "acceptedFeatureIds": [],
    "removedFeatureIds": [],
    "files": [
      { "name": "appsettings.json", "captureId": "22222222222222222222222222222222", "content": "e30=" },
      { "name": "shells.Production.json", "captureId": "22222222222222222222222222222222", "content": "eyJDU2hlbGxzIjp7IlNoZWxscyI6eyJkZWZhdWx0Ijp7fX19fQ==" },
      { "name": "shells.json", "captureId": "22222222222222222222222222222222", "content": "eyJDU2hlbGxzIjp7IlNoZWxscyI6eyJkZWZhdWx0Ijp7IkZlYXR1cmVzIjp7fX19fX0=" }
    ]
  },
  "environmentInput": {
    "version": 1,
    "captureId": "22222222222222222222222222222222",
    "content": "eyJ2ZXJzaW9uIjoxLCJlbnRyaWVzIjpbXX0="
  }
}
```

The host verifies its actual loaded host identity against the host metadata, resolves its selected persistence assembly, repeats capability/enrollment validation through its EF-owned equivalent metadata checks (without referencing the CLI helper), validates the candidate and environment capture IDs, and only then builds configuration. The explicit overlay is applied after appsettings base/environment and shells base/environment at the supported Workbench environment stage. No process environment, command-line provider, custom provider, installation/bootstrap state, deployed observation, or physical readiness is imported.

The host uses its existing configuration builder and `EfToolingConfigurationContext.ComposeShell`, descriptor/CShells resolver, selection reconciliation, and `EfPersistencePreparation` once. The frontend `CompositionCandidateBuilder` remains a CLI/Planning concern and is not duplicated in the host. Reconciliation precedes preparation; valid authored removals survive, while divergence, required-edge conflict, stale identity, or an active removed feature refuses. Inspection does not construct application services, start runtime services, create a DbContext, connect to a database, apply migrations, publish, or save state.

## Response

The host returns a separately validated closed version-1 response with existing correlation/status/exit fields and exactly one of `configurationResolution` or `error`. This complete harmless success example uses an empty explicit overlay and empty safe selection; it is syntactically valid but is not an execution result:

```json
{
  "version": 1,
  "invocationId": "11111111111111111111111111111111",
  "captureId": "22222222222222222222222222222222",
  "status": "ok",
  "exitCode": 0,
  "configurationResolution": {
    "source": "captured-workbench-json-explicit-environment-v1",
    "shell": "default",
    "environment": "Production",
    "resolution": "resolved",
    "externalInputs": "supplied-intended",
    "selection": {
      "acceptedFeatureIds": [],
      "requestedFeatureIds": [],
      "effectiveFeatureIds": [],
      "disabledFeatureIds": [],
      "implicitFeatureIds": []
    },
    "participants": [],
    "configuredValueAffinity": "not-applicable",
    "targetVerification": "not-performed",
    "runtimeParity": "unobserved",
    "packageReachability": "unverified",
    "connectivity": "unverified",
    "schemaReadiness": "unverified",
    "migrationReadiness": "unverified",
    "activation": "unobserved",
    "unresolved": []
  }
}
```

The new lane validator normatively inherits Spec 187's closed safe-response semantics, enums, configured-value affinity states, legacy-target handling, participant/finding rules, and unavailable evidence states. It changes only the source literal, `externalInputs`, and the new fixed refusal codes; it does not promote evidence or add per-key provenance. It rejects old-source, wrong-mode, stale-token, additional-field, or readiness-promoting responses. The old candidate-v1 validator remains closed. Exact-file/key/provider provenance, values, raw configuration, private paths, exception excerpts, deployment state, and fingerprints derived from private inputs are unavailable and must not be added to the response.

The successful `configurationResolution` object is closed to the existing safe projection fields plus the new source value: `source`, `shell`, `environment`, `resolution`, `selection`, `participants`, `configuredValueAffinity`, `targetVerification`, `runtimeParity`, `packageReachability`, `connectivity`, `schemaReadiness`, `migrationReadiness`, `activation`, `externalInputs`, and `unresolved`. Selection contains the accepted/requested/effective/disabled/implicit safe ID arrays. A participant row contains only the existing safe feature/module, selection kind, logical resource, provider, connection reference, selector scope, resource scope, and `exactFileProvenance` fields. No additional field, arbitrary provenance, or value is accepted. The combined participant-plus-finding projection admits at most 1,024 rows and retains every Spec 187 safe identity limit.

The error object is closed to required `code` plus optional fixed `reason`, safe `feature`, and safe `resource`. Optional error identities must be canonical declared public identities from the accepted/source context or actual known host metadata. Safe grammar alone does not authorize echoing an unknown identity introduced only by the supplied overlay; such errors retain the fixed classification and omit that identity. Candidate-v1 behavior is unchanged. The error object has no message, stack, raw details, path, key, value, index, or exception excerpt. The top-level response is closed to `version`, `invocationId`, `captureId`, `status`, `exitCode`, and exactly one of `configurationResolution` or `error`; duplicate or unknown fields refuse. The worker and CLI validate this host response independently and check token equality and process-exit consistency.

## Fixed classifications

The new admission/refusal codes are:

| Code | Meaning | Exit |
|---|---|---:|
| `candidate-environment-input-invalid` | Closed shape, character, null/tombstone, BOM/UTF-8, or entry/value/key grammar failure. | 2 |
| `candidate-environment-input-too-large` | Raw document, entry count, raw/normalized key, value, or encoded environment-input field bound exceeded. | 2 |
| `candidate-environment-key-collision` | Duplicate raw or normalized case/alias collision. | 2 |
| `candidate-environment-prefix-unsupported` | One of the eleven service-prefix families appeared. | 2 |
| `candidate-environment-host-unenrolled` | Missing, duplicate, or wrong declaration on the actual host assembly. | 3 |
| `candidate-request-too-large` | Existing shared candidate/worker serialized request bound, including the combined candidate files, environment field, envelope, or metadata, exceeded. | 2 |
| `candidate-capability-unavailable` | Exact new capability missing or mismatched. | 3 |
| Existing fixed drift/selection/EF/response/cancellation/cleanup codes | Existing boundary classifications, retained without raw remote details. | Existing mapping |

All diagnostics are fixed local messages. No key, entry index, value, source path, fingerprint, or peer exception text is returned. A malformed direct host request that cannot establish correlation returns exit 2 without invented response bytes; the worker maps missing/invalid exchange to its existing response-invalid classification. A valid correlated unenrolled host returns a host response with `status:"refused"`, `exitCode:3`, and the fixed `candidate-environment-host-unenrolled` error; the new lane validator must accept that response while the old candidate response path remains unchanged.

## Privacy and lifecycle

Modeled resource and connection references are public logical labels under Spec 187; fixed provider and selection metadata are also public. Their use in the closed safe fields is permitted even when the overlay selects them. Connection material and arbitrary/non-modeled overlay values remain private. Public-identity canaries must appear only in their authorized safe fields, while separate private-value canaries (including syntactically safe strings) must be absent from every public/process/log/artifact surface. Unknown overlay-only identities are not promoted to public labels merely because they pass a syntax check.

The original operator-owned input file is preserved and rechecked; no extra persisted private copy is created. Private values, raw configuration, and private-input-derived fingerprints do not enter child process arguments, public output, logs, diagnostics, or generated/public artifacts. The private input path is not forwarded in the new worker request or emitted publicly. Existing user-typed CLI file locations and actual assembly-loader/deps/package-root paths may remain as finite loader/regular-file-check metadata where the existing CLI requires them; this is distinct from transmitting captured document bytes or private values in child arguments. The new worker request carries document content over bounded stdin, never the private source path.

The owned worker captures and drains child streams, bounds reads/writes, observes cancellation during write/read and after capture, and uses the existing cleanup path. The boundary provides trusted-child lifecycle and cleanup, not a general sandbox or malicious-composer safety guarantee. No SHA256 or other fingerprint derived from private environment input is emitted; hashes of public catalog/profile/code artifacts remain outside this prohibition where existing contracts legitimately use them.
