# Proposed ADR 0076 extension: explicit private environment input

**Status**: Proposed additive extension for #2277. This document does not rewrite or accept ADR 0076.  
**Source**: [ADR 0076, persistence tooling inside the host closure](../../../docs/adr/0076-persistence-tooling-runs-inside-the-host-closure.md)  
**Scope**: Extend D1, D4, and D7 for one explicit private environment-overlay inspection lane.

## Decision D1 extension: one host-owned capability

ADR 0076 D1 keeps the CLI thin and runs the EF operation inside the selected host closure. The new lane preserves that boundary by adding one independent capability:

```text
EfCandidateEnvironmentInspectionContract.Version = 1
EfToolingHost.RunCandidateEnvironmentInspectionAsync(Stream, Stream, CancellationToken)
```

The CLI worker client starts the EF-free worker in the existing owned child with the selected host's runtimeconfig/deps and loader metadata. Inside that child, the EF-free binder validates the outer request/layout closure, loads the actual host assembly through `HostClosure.LoadHostAssemblyForInspection` (name/location validation only), resolves the selected persistence assembly through the installed closure, binds the exact method and literal capability version from that selected EF assembly, and then calls metadata-only `ToolingEntryPoint.ValidateCandidateEnvironmentEnrollment(Assembly hostAssembly, Assembly persistenceAssembly)`. Only after those checks does it invoke the host operation through bounded JSON streams. It does not execute the host entry point or startup, add an EF reference, resolve providers, duplicate CShells composition, or construct a second preparation engine. The selected host owns the explicit overlay, its existing configuration builder/`EfToolingConfigurationContext.ComposeShell`, descriptor/selection reconciliation, and the existing `EfPersistencePreparation` call.

The existing candidate-v1 contract, `RunCandidateInspectionAsync`, `inspect-candidate` shape, and old response validator remain closed. The new outer command is `inspect-candidate-environment` under WorkerContract v2; the inner host envelope is separately versioned. Every old command reader must reject the additional field, including explicit `environmentInput:null`; an old worker must not reinterpret it merely because a shared DTO has an optional property.

Because the capability is carried by a shared EF assembly, method presence cannot enroll every host. The inspection-only `HostClosure.LoadHostAssemblyForInspection(string,string):Assembly` helper returns the actual loaded host assembly after validating only its name and location against the selected layout. The separate metadata-only `ToolingEntryPoint.ValidateCandidateEnvironmentEnrollment(Assembly hostAssembly, Assembly persistenceAssembly):void` reads `CustomAttributeData` from that actual assembly, checks the attribute type identity belonging to the selected persistence assembly, and requires exactly two positional constructor arguments, integer `1` and the literal policy, with no named arguments. The existing public `void LoadHostAssembly(...)` signature remains unchanged; private loading can be shared. The actual path-matched selected host assembly must carry exactly one:

```text
[assembly: EfCandidateEnvironmentInputs(1, "workbench-json-explicit-environment-v1")]
```

The first lane ships this declaration only with reviewed Workbench policy. The host repeats capability and enrollment metadata validation with the actual host and selected persistence assemblies immediately before configuration construction/composer creation. Foundation Host and other hosts refuse before composition unless separately enrolled through a reviewed policy. Host names are never used as enrollment evidence.

The attribute contract is `[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]` with constructor `(int version, string policy)` and read-only `Version`/`Policy` properties. The declaration uses exactly two positional arguments and no named arguments; duplicate declarations are refused.

## Decision D4 extension: explicit supported source policy

ADR 0076 D4 requires authoritative provider agreement and refuses fallback or auto-detection. The new lane makes the source policy explicit and deterministic:

```text
appsettings.json
appsettings.<environment>.json
shells.json
shells.<environment>.json
explicit supplied environment overlay
```

The explicit document is a strict closed v1 JSON file captured by the CLI. It contains string key/value entries, admits blank values, refuses null/tombstone/removal entries, normalizes `__` to `:`, refuses case/alias collisions before provider processing, and refuses all eleven standard service-prefix families. Ordinary `ConnectionStrings__<name>` remains supported.

The lane does not install or mutate a process environment and does not import caller ambient values, command-line providers, custom providers, deployment/bootstrap observations, or physical readiness. It is a supported Workbench configuration overlay, not a claim of parity with every `IConfiguration` source in a running process. Reconciliation and preparation use the same host-owned policy once; there is no frontend resolver or silent acceptance of a changed selection.

## Decision D7 extension: private supplied input and evidence

ADR 0076 D7 allows secrets through explicit stdin or environment-name mechanisms and excludes raw values, hashes, snapshots, and private paths from exported evidence. For this lane, the private file is read once into owned bounded bytes and sent as canonical base64 content in the private worker request. The source file is rechecked before launch and after the result/refusal. The original operator-owned file remains in place; no generated/private copy is created.

The first-lane bounds are 1 MiB raw document, at most 1,024 entries, 1,024 UTF-8 bytes per raw and normalized key, 65,536 UTF-8 bytes per value, existing 1 MiB decoded selected file, 4 MiB selected aggregate, 8 MiB serialized request, 4 MiB response, a combined participant-plus-finding projection limit of 1,024, 4,096 selection IDs per set, and JSON depth 64. The final serialized request/response limits remain authoritative. The implementation must prove exact and one-over boundaries without allocating unbounded buffers.

Private values, raw private configuration, private-input paths, and fingerprints derived from private environment input never enter child arguments, logs, diagnostics, public output, generated/public artifacts, or exception excerpts. The new worker request carries document bytes over stdin, never the private source path. Existing user-typed CLI file locations and actual host assembly/deps/package-root loader paths may remain as finite metadata required for regular-file checks and host loading; they are not private values, are not copied to public output, and do not authorize new path disclosure. Hashes of public catalog/profile/code artifacts remain governed by their existing contracts and are outside this private-input prohibition.

The worker retains ADR 0076's trusted selected-host boundary and cleanup obligations. This is not a general sandbox. Inspection does not start services, execute host startup, activate a runtime, access a database, run migrations, publish, or save application state. A real database/provider journey remains a separate persistence proof.

## Consequences

Positive consequences:

- Existing candidate-v1 consumers do not need to understand the new input and cannot silently receive ambient values.
- The host's actual configuration/reconciliation/preparation policy remains the single runtime-aligned source of target selection.
- The explicit input is reproducible and drift-checked while public output remains value-free.
- Workbench enrollment is inspectable and cannot be inferred for Foundation Host from a shared method.

Costs and limits:

- The lane is Workbench-only until another host policy is reviewed.
- Service-prefix expansion, general configuration taxonomy, deployment evidence, and physical readiness remain separate work.
- Combined maxima may exceed the 8 MiB request ceiling; the final bounded serializer may refuse a request whose individual parts fit.
- Trusted composer code is not sandboxed, and the operation cannot make claims about malicious code or physical erasure of managed strings.

## Compatibility and proof

The extension is additive and leaves ADR 0076 D1/D4/D7's existing operations unchanged. The proof plan is [acceptance-proof-matrix.md](./acceptance-proof-matrix.md): it requires actual built Workbench closure plus the public CLI wrapper, existing fixture controls, runtime-preparer/private-capture parity, external-toggle accept recovery, lifecycle cancellation/cleanup, all numeric boundaries, all eleven prefix refusals, and canary scans over public/process/artifact/digest surfaces. It records planned evidence only; no ADR acceptance or implementation result is claimed here.
