# Captured candidate inspection producer/consumer v1

Proposed operation under ADR0076's existing host-owned persistence boundary. No production API is added by issue2175. Existing EfToolingContract v1, EfToolingContextContract v2 and configuration-context v1 stay unchanged, including offline verifyConnectionValues:false.

## Capability and ownership

Add host-owned `EfCandidateInspectionContract.Version = 1` and public static `EfToolingHost.RunCandidateInspectionAsync(Stream request, Stream response, CancellationToken)` returning `Task<int>`. The worker resolves this exact method and literal version from the selected host's Elsa.Persistence.EntityFramework assembly before sending private candidate bytes to it. Missing, partial, wrong-version or wrong-signature capability refuses candidate-capability-unavailable; no v2 fallback.

The EF-free CLI and worker add no EF/CShells/persistence references. They reuse HostLayout, WorkerProcess.Arguments, HostDepsFile, HostClosure and the installed Nuplane package loader. Candidate execution is a bounded operation branch; it must not use existing raw diagnostic/Restore paths. Host-owned operation creates configuration from supplied streams, constructs the selected assembly's declared composer using existing declaration rules, merges the selected shell, discovers actual descriptors, uses the real CShells dependency resolver, reconciles selection and calls EfPersistencePreparation with verifyConnectionValues:true. It neither builds services nor starts feature activators nor creates DbContexts. Refactor shared host composition narrowly if needed; v2 semantics remain false and live commands remain independent.

## Private request

Outer WorkerContract version2 remains the existing envelope for normal commands. Add a distinct `command: inspect-candidate` with one required `candidate` payload version1. Other commands reject candidate fields. Candidate mode permits only version, command, hostDirectory, hostName, depsFile, packageRoots, candidate, and restore exactly false (or omitted default false). Every other existing outer field MUST be absent/null: selection, provider, schema, output, environment, shell, contextSource, contextVersion, resource, shells, connectionEnv, connection, finalization and skewAllowance. Unmapped fields refuse. Shell/environment authority exists only in the candidate payload; null inherited fields carry no authority. Existing envelope defaults are not authority. Same-package frontend/worker own this private additive command; an older worker rejects its unknown shape/command. Host capability has its independent version1. No request silently downgrades.

Payload fields, all required unless stated:

| Field | Validation |
|---|---|
| version | Integer1. |
| source | Literal captured-workbench-json-v1. |
| invocationId, captureId | Independent random 32-lowercase-hex tokens, generated once per invocation/capture, never derived from input values. |
| shell, environment | Safe ASCII selection references, 1..128 characters; filenames use separately validated basename grammar. |
| acceptedFeatureIds, removedFeatureIds | Sorted unique safe stable IDs, <=4096 each; case-insensitive collisions refuse. Removed and accepted sets disjoint. |
| files | Exactly the selected3/4 layer files: appsettings.json, shells.json, shells.<environment>.json and appsettings.<environment>.json when captured. Each entry is a closed object with exactly name, captureId and content fields; name is the logical basename and content is canonical base64 of the post-edit selected candidate file bytes. Each has the same captureId. No unrelated sibling overlay, path separator or traversal admitted. Environment filename grammar is ASCII alphanumeric/underscore/hyphen. Selected-overlay rules inherit file-bridge contract. |

Files are the selected layer entries copied byte-for-byte from `CompositionCandidate.Files` of one Build call. Retained unrelated sibling bytes remain frontend-local and are still drift-checked; they are not transported to the host. Decoded files must be valid UTF-8 JSON, duplicate keys refused under configuration's case-insensitive semantics, depth<=64 and supported source shape. Candidate construction retains unknown nested content. The producer associates the immutable source and composition-input snapshots with that candidate by object/invocation ownership; it never accepts an independently assembled candidate and a caller assertion that their bytes agree. Mixed capture objects refuse before launch. Worker rejects inconsistent entry capture tokens and response correlation. Correlation tokens are only consistency checks: they do not authenticate an untrusted handcrafted request or prove a sender used the builder. The frontend is trusted within this local invocation; actual host input resolution comes only from bytes supplied by that producer.

Compiled HostDirectory/HostName/DepsFile and installed package roots remain separate private outer metadata. After actual host assembly loading, the worker calls the host operation with a closed version1 envelope containing `host:{name,directory}` (verified compiled layout) and `candidate:<payload above>`. The host requires its actual loaded assembly name/location to match those host fields using the existing declaration rule, independent of configuration byte origin. This host envelope also obeys the8MiB request bound. The worker never derives host location from --host-dir. They select actual assembly loading, not configuration-file reads. Observe/recheck the selected runtimeconfig/deps pair and installed package-state/manifest identities around loading; changed or ambiguous closure refuses. Use Nuplane's own loading, no custom graph, no Restore/feeds. This is observed installed closure evidence, not immutable distributed-package or deployed-host parity. Loader use of installed state/assembly paths is separate from the guarantee that configuration is supplied from candidate bytes.

## Bounds and lifecycle

The candidate worker is selected by a safe literal --candidate-inspection launch argument appended after the existing runtimeconfig/deps/worker assembly arguments. That branch uses bounded parsing and console suppression before any request/host handling. Normal worker launches retain their current arguments/parser/diagnostic behavior. No candidate metadata/value or configuration-source path travels in this mode argument.

Candidate-specific source capture and recheck MUST first refuse nonregular files (including supported-name Unix FIFOs/devices and unused sibling overlays) through the existing CompositionInputSnapshot fail-closed regular-file preflight; extract shared setup rather than duplicating it. This preflight occurs before file-content reads, so a FIFO never blocks waiting for a writer outside the worker timeout. Symlink/reparse/path-escape refusal remains.

Bounded source capture/recheck also applies before SourceSnapshot/CompositionInputSnapshot allocation: each context admits <=32 source files, <=1MiB each and <=8MiB aggregate. There are separate host-source and supplied-intent contexts, so combined retained source bytes are <=16MiB. Count actual bytes while reading rather than relying on file size then using unbounded reads. Both contexts retain all admitted sibling/unused-profile files. Existing command readers keep their behavior through a candidate-specific bounded option.

All transport limits apply before allocating an unbounded buffer: 1MiB decoded per selected file; 4MiB decoded candidate aggregate; 8MiB UTF-8 request on stdin (including outer metadata/base64/IDs); 4MiB response; <=1024 total participant/finding projection rows and <=4096 selection IDs per set; <=128 characters per public logical identity; JSON depth64. Requests/responses forbid duplicate and unmapped fields; entry limits count actual decoded bytes. No compressing or chunked fallback. Both sides use counting/bounded readers; bytes exactly at a bound are accepted, next byte refuses.

Frontend timeout defaults60s, CLI range1..300; cancellation covers process start, stdin write/flush, stdout/stderr drain, parse and wait. Capture both child streams; drain/discard stderr continuously without storing it. Save the dedicated raw stdout response stream in worker before replacing Console.Out and Console.Error with TextWriter.Null for candidate execution. Dedicated host response is privately bounded then content-validated before worker serialization. Only complete bounded responses are buffered for final output. Every failure/cancellation after process creation enters try/finally, kills the owned process tree if needed, awaits termination and disposes streams/process. Cleanup deadline5s; failure returns candidate-cleanup-failed and records no success. No surviving process may be treated as successful cleanup. Exception messages and raw process output never reach frontend output.

The process inherits OS runtime environment needed for loading; no AddEnvironmentVariables/command-line/custom configuration provider is added by this producer. A trusted composer can still read ambient state or execute arbitrary code. The operation therefore marks ambient/custom inputs unverified and makes no sandbox/deployed parity claim. Tests verify Elsa-owned paths with non-side-effecting declared composers.

## Selection and configuration semantics

The host first validates all accepted IDs against its real descriptor closure. Case aliases, absent descriptors or unsafe metadata refuse with value-free fixed codes. Capture requested IDs from composed ShellSettings, disabled IDs and ordered/implicit IDs from the existing CShells dependency resolver. Refuse if requested or expanded sets differ from accepted, if any explicit removal remains active, or if a required target is disabled/absent from accepted. Never restore an ID, add optional companions or mutate accepted intent. Accepted profiles include their reviewed required closure; actual host disagreement requires explicit user reacceptance.

Shared EF preparation owns provider/resource pair selection, authored presence, root/shell/binding precedence, legacy/resource ambiguity, enrollment, shared context/provider/schema/pooling/transaction constraints and private configured connection agreement. No frontend resolver. Refusal produces no target preview. Legacy participants remain selection:Legacy with resource/provider/connectionReference:null and legacy-target-unprojected; the existing resource preparer does not resolve feature-registration legacy defaults. Do not construct features or invent a legacy target to fill the gap.

## Safe response

For an admitted correlated request, the host returns one closed response: version1, invocationId, captureId, status (`ok` or `refused`), exitCode, and exactly one of configurationResolution/error. Worker/frontend check token equality and process exit consistency. The worker carries this host document in the existing private WorkerResponse version2 Tooling field, preserving its exact host document after validation; pre-host failure uses the existing outer Error shape with an admitted fixed code. Candidate frontend validates both envelope layers, never the old v1/v2 tooling parser. Every error code maps to a fixed local message; no remote free-text message is printed.

Correlated host input refusals additionally admit candidate-request-invalid, candidate-request-too-large and candidate-capture-invalid with exit2; malformed decoded selected-file JSON uses candidate-capture-invalid. They carry no reason or target preview. If a malformed/oversized host request cannot establish valid independent correlation tokens, the host returns exit2 with no response bytes rather than inventing identities. Such a missing/uncorrelated host exchange is candidate-response-invalid at the worker boundary. Frontend/worker request admission rejects invalid private requests before host dispatch; these direct malformed-host calls are not accepted frontend journeys.

Successful configurationResolution fields:

- source:captured-workbench-json-v1; safe shell/environment; resolution:resolved or partial (partial when legacy targets or supported ownership/resource-scope evidence are unavailable). Exact-file provenance is independently unavailable and alone does not make resolved logical resource targets partial.
- selection: acceptedFeatureIds, requestedFeatureIds, effectiveFeatureIds, disabledFeatureIds, implicitFeatureIds; successful accepted/requested/effective sets equal, implicit empty; disabled sets contain all authored removals. These are observations, not activation evidence.
- participants: sorted unique feature/module rows with selection enum Legacy/ShellBinding/ShellDefault/RootDefault; resource, provider, connectionReference nullable; selectorScope and resourceScope nullable; exactFileProvenance:unavailable. Feature may map multiple modules, preserving current metadata; do not invent one context per feature.
- configuredValueAffinity:checked if applicable resource-mode agreement checks ran, otherwise not-applicable. This never means physical target verified.
- targetVerification:not-performed; runtimeParity:unobserved; packageReachability:unverified; connectivity:unverified; schemaReadiness:unverified; migrationReadiness:unverified; activation:unobserved; externalInputs:unverified.
- unresolved: sorted fixed codes only: legacy-target-unprojected, exact-file-provenance-unavailable, resource-participant-unenrolled, resource-scope-unsupported; unknown future codes refuse the version rather than echoing arbitrary text.

Public resource/connection names must satisfy the existing adapter grammar (leading letter or underscore; subsequent letters/digits/underscore/hyphen/dot; true/false/null forbidden), plus128-character bound. Feature/module IDs follow existing SelectionValueRules.IsSafeReference plus128 bound. Provider is one of Sqlite, SqlServer, PostgreSql, MySql or null. Scope enums are root, shell-composed, shell-authored, feature or unavailable. Exact source filenames/paths, schemas/settings, connection values, unknown values and hashes of private configuration are not response fields. Validation is performed by host producer, worker and frontend, not only serializer shape validation.

The host error object admits only code (required), reason (optional fixed selection-conflict enum listed in the CLI contract), feature and resource (optional validated identities). No message/stack/raw details field is admitted. Pre-host outer WorkerError.Message is ignored; frontend prints only local fixed code mappings.

Existing EF refusal codes admitted here: resource-selection-invalid, resource-not-found, resource-definition-invalid, resource-configurator-unsupported, resource-required-feature-disabled, resource-legacy-conflict, resource-ownership-unresolved, resource-context-conflict. Private error carries fixed code/reason and optional safe feature/resource identity; invalid identity is omitted. Unexpected code/status/content/shape uses candidate-response-invalid. Missing capability uses exit3; invalid candidate/EF configuration exit2; successful projection exit0. No DatabaseFailure exit is an allowed successful invocation of this operation.

## Examples

These JSON fragments illustrate proposed host request/response fields, not production artifacts. Omitted files are not valid wire requests. Secret bytes are never shown in examples.

```json
{"version":1,"source":"captured-workbench-json-v1","invocationId":"11111111111111111111111111111111","captureId":"22222222222222222222222222222222","shell":"default","environment":"Production","acceptedFeatureIds":["DiagnosticsStructuredLogs","DiagnosticsStructuredLogsEntityFrameworkCore"],"removedFeatureIds":["DiagnosticsOpenTelemetryEntityFrameworkCore"],"files":[]}
```

The empty files example refuses candidate-capture-invalid. A real producer must supply base+selected overlay bytes, with that explicit removal represented as false.

```json
{"feature":"DiagnosticsStructuredLogsEntityFrameworkCore","module":"Diagnostics.StructuredLogs","selection":"ShellBinding","resource":"observability","provider":"PostgreSql","connectionReference":"Observability","selectorScope":"shell-authored","resourceScope":"root","exactFileProvenance":"unavailable"}
```

This is a row inside configurationResolution after actual preparation, not proof by itself. Distinct references resolving unequal strings in one shared context return resource-context-conflict instead of rows. An older host returns candidate-capability-unavailable. A valid Legacy row uses null target fields and legacy-target-unprojected. Invalid inline syntax in either reference refuses without echo. Changed original source after this response refuses bridge-source-changed at the final frontend input recheck, so no preview is published.

Complete syntactic wire examples: [host request](examples/candidate-request.json), [host response](examples/candidate-response.json). They use an empty synthetic selected shell and harmless empty appsettings to illustrate all fields and byte transport. They are not an executable host artifact or producer parity evidence; real acceptance requires A01–A14.
