# Explicit configuration context for resource-aware tooling

Status: engineering proposal under review, 2026-09-23. Part of #1967 Phase 0; not an implementation contract yet.

The owner-approved [target check](tooling-target-verification.md) stays one expected named-connection lookup and strict comparison. This proposal defines the inputs to that lookup. It does not introduce secret storage, database identity discovery, or another way to supply the connection used by a database operation.

## Source selection

Use an explicit configuration-context selector with two bounded Workbench source modes:

| Mode | Sources, in ascending precedence | Meaning |
|---|---|---|
| `workbench-json-v1` | appsettings.json, appsettings.&lt;environment&gt;.json, shells.json, shells.&lt;environment&gt;.json | Check the selected published host's file configuration. |
| `workbench-json-environment-v1` | The same four files, then the worker's inherited environment using the standard .NET environment configuration provider | Check an operator-supplied environment context. Opt-in; never automatically infer that it belongs to a running host. |

The selected host directory, explicit environment name and one shell identity accompany the mode. The environment name comes from the command selector, never implicitly from ASPNETCORE_ENVIRONMENT in the tool process. Reuse the existing environment default where applicable; report the resolved name.

Neither mode includes runtime command-line overrides, user secrets, vaults or custom providers. A runtime depending on those sources needs an equivalent supported context supplied for the check; the tool cannot claim it observed those sources. The environment mode covers ordinary container/deployment environment configuration without adding a provider plugin framework. The operator supplies the same intended configuration to runtime and tooling; an independent running process remains unobserved.

Build one configuration snapshot per invocation, with reload disabled. Resolve shell configuration and root fallback using the same semantics as the runtime adapter. Preserve source presence until the resolver applies setting-specific rules. Do not flatten the snapshot into the public request or export it.

## Transport and compatibility

Add an optional versioned target-context descriptor to the worker request and the reflected host request. It carries source mode, selected host directory, environment and shell, but no settings or secrets. The directory must be copied explicitly into the host request: the existing WorkerRequest.HostDirectory is currently not sent to EfToolingRequest. Do not infer it from assembly location or working directory.

Branch into resource-context handling before ElsaCli.WithHostConfiguration performs its legacy feature/provider projection. The host-side adapter reads the selected sources and resolves the complete authored resource intent. Do not combine a new resource snapshot with old projected feature/provider values from an earlier file read.

Before sending the descriptor, the worker checks whether the loaded host contract supports it, following the existing CapabilitySelection reflection pattern. An older host refuses with a named unsupported-context error; it never falls back to provider-only agreement. The host validates descriptor version and source mode and refuses unknown values. Requests with no applicable resource context retain legacy behavior.

Before this proposal becomes a contract, settle how a resource-aware configuration encountered without the explicit selector refuses rather than silently taking the legacy path. The source review recommends a non-EF raw file-intent check before projection, with a named configuration-context-required refusal. Its exact predicate still needs review against definitions-only configurations, disabled bindings and dependency-enabled participants so it does not redefine legacy behavior. Environment-only resource intent is outside no-context execution: that legacy/file-only result must report external resource resolution as unchecked and cannot claim resource readiness or strict target verification. Every resource-aware invocation requires the explicit selector; its absence is never proof that an independent runtime has no resources.

## Verification order and output

1. Validate the context and obtain the authored configuration snapshot inside the selected host closure.
2. Discover participants and resolve resource references with the persistence-owned resolver, preserving host-owned module selection.
3. Establish the exact module set for this target invocation. A single actual connection must not receive modules belonging to a different target. The final resource selector and interaction with --modules / --from-host still need the authored-format contract.
4. Check provider and context constraints. Offline list/plan/script may validate references and report unresolved prerequisites, but do not look up expected connection values or claim a live target match.
5. For apply/validate/post-migrate, obtain the actual connection through the existing explicit env/stdin channel. Independently look up the selected resource's named connection in the chosen configuration snapshot. Missing/empty expectation or strict mismatch refuses before a context, connection or database command is created. Never use the actual input to fill a missing expectation.
6. Execute only after all applicable checks pass. Never reuse the expectation as a substitute for missing actual input.

Public evidence may contain context mode, environment, shell, resource/reference identities, checked provider/module identities and individual check outcomes. It must not contain raw paths, configuration snapshots, either connection value, connection hashes, or exception details that could contain them. Output must distinguish agreement with the supplied context from unverified parity with a separately running host. A successful context comparison is not a claim that migration, activation or data relocation succeeded.

## Required proof

- Four-file precedence and shell-over-root fallback match the rebuilt Workbench's configuration.
- Environment overrides affect resource resolution only under explicit environment mode; files mode reports its limitation.
- Env/stdin actual input cannot manufacture its own missing expected value. Wrong value refuses with zero database/context creation.
- Offline commands never invoke expected-value lookup, even when secrets are available.
- Every command path, including the intermediate listing used by script, carries or refuses the selected context consistently.
- Old host, unknown source/version, missing shell, missing expected value and mixed-target selection refuse deterministically.
- Configuration and provider errors use safe diagnostics; canary values are absent from stdout/stderr, exceptions, serialized responses, plans and process arguments.
- A real runtime and packaged tool supplied the same environment context agree. A separately running host with different external overrides is explicitly outside that evidence.

Source baseline rechecked against origin/main on 2026-09-23: `e738badd1f9ddc2974248079de43d21440ba5afd`. Relevant source: Workbench Program.cs; ElsaCli.WithHostConfiguration; WorkerRunner.ExecuteAsync and ScriptRequest/ListModulesAsync; ToolingEntryPoint.Resolve; EfToolingRequest; EfToolingHost; HostAppSettings.Read. The plan will link final contracts after independent review resolves the remaining gates above.

The bounded source review supports these two modes. Existing test seams include WorkbenchConfigurationTests, NuplaneRestoreCliTests, ProviderAgreementCliTests, WorkerLaunchTests and PersistenceCliTests. Their present passing behavior is baseline evidence only; the new cases above still require implementation and execution.
