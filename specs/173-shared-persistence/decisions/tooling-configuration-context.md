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

The worker request adds only source mode and context version. Its existing validated HostDirectory, Environment and Shell fields remain the single authority; do not repeat them in a nested descriptor. The worker must explicitly hand those canonical values to the host-side adapter, because WorkerRequest.HostDirectory currently never reaches EfToolingRequest. Do not infer the directory from assembly location or working directory, and do not accept a second path that can point outside the selected host configuration root.

Branch into resource-context handling before ElsaCli.WithHostConfiguration performs its legacy feature/provider projection. The host-side adapter reads the selected sources and resolves the complete authored resource intent. Do not combine a new resource snapshot with old projected feature/provider values from an earlier file read.

Before sending the descriptor, the worker checks whether the loaded host contract supports it, following the existing CapabilitySelection reflection pattern. An older host refuses with a named unsupported-context error; it never falls back to provider-only agreement. The host validates descriptor version and source mode and refuses unknown values. Requests with no applicable resource context retain legacy behavior.

**Reviewed snapshot lifetime:** script currently invokes the host once to list selected module assemblies and again to generate its artifact. Re-reading the configuration from its descriptor on both calls would allow different selections. Use one host-owned, disposable in-process configuration context created through a negotiated reflected factory, reused by the internal list and final operation, and released when the worker command ends. The worker holds an opaque object; no configuration or connection snapshot is serialized. This is one invocation's local state, not a persistent session, cache or secret service. The context holds immutable source/presence/participant data, never the actual env/stdin connection or command-specific module selection. Each host call derives its selection from its request against that same snapshot.

Negotiate the factory and context-aware operation entry point atomically; partial support refuses and never retries through legacy RunAsync(Stream, Stream). The worker creates the context from its existing canonical fields plus mode/version, passes the identical object to all sequential subcalls, and owns disposal in a try/finally for success, refusal and cancellation. Creation, invocation and disposal failures all use the typed redacted boundary. The original two-stream entry point remains unchanged for legacy requests. Concrete reflected signatures belong in the Phase 1 tooling contract.

**Selected package boundary:** the configuration adapter belongs beside EfToolingHost in Elsa.Persistence.EntityFramework. Add explicit Microsoft.Extensions.Configuration.Json, Microsoft.Extensions.Configuration.EnvironmentVariables and CShells dependencies there; the selected host closure supplies their versions. The pure resolver still accepts detached data and performs no source reads. The CLI and worker do not receive EF references or implement persistence resolution. The exact public APIs are ShellBuilder.FromConfiguration for normalized settings and reset/disable behavior, FeatureDiscovery.DiscoverFeatures for descriptors and FeatureDependencyResolver.GetOrderedFeatures for dependency expansion. A published-package probe verified discovery/expansion without feature construction; the runtime/tooling composition comparison remains an integration gate.

**Host defaults integration under review:** source-file parsing alone cannot reconstruct ConfigureAllShells callbacks. Extract Workbench's current two global selections into one host-owned shell-default composer reused by startup and tooling. A small EF tooling contract, IEfToolingShellDefaults.Configure(ShellBuilder, IConfiguration), and an assembly-level declaration identify that composer on the explicitly selected host assembly. The host assembly identity comes from the same validated worker host layout; do not discover an arbitrary matching adapter from unrelated loaded packages. The composer may configure the builder and attach deferred feature actions, but must not instantiate features, build a shell provider, open databases or activate the host. The first-party implementation and its no-side-effect proof are part of #1968.

Tooling runs the shared composer against a new ShellBuilder and the frozen configuration, then calls FromConfiguration for the selected shell and expands dependencies using the public APIs above. Startup calls the same composer from ConfigureAllShells. Preserve deferred configurator identities so the existing resource-participant refusal can run without invoking those actions. Missing/ambiguous/unsupported host composition metadata refuses resource-context preparation; the adapter cannot claim equivalence for arbitrary code it has not represented. This is a host composition seam, not a general secret-provider framework. Review this narrow declaration and the exact factory signatures together in Phase 1.

For no-selector requests, use two stages. The CLI checks only for raw persistence-namespace syntax in the four selected files before its legacy projection; that is a hint, not enrollment or activation evidence. When a hint exists, a capable host performs semantic preflight from its own source snapshot before any provider/database operation. Context is required exactly when an enabled, enrolled owner of a selected module has an effective resource selection, including a malformed or missing selected reference. Definitions alone, directly disabled inactive bindings and unenrolled/unknown feature bindings do not satisfy that predicate. Unknowns remain unresolved and never become resource-readiness evidence.

Use the final dependency-expanded graph for applicability. In particular, CShells can reintroduce an explicitly disabled feature as a required dependency; do not simply subtract DisabledFeatureIds from the ordered graph. A resource-applicable disabled-required conflict refuses before feature effects. A wholly legacy selected module set retains legacy behavior; host preflight must not reclassify it or claim a resource target check occurred.

An older host with a raw hint but no negotiated context capability cannot distinguish inert definitions from applicable consumers. Refuse with context-capability-unavailable, explicitly stating that applicability could not be established; do not claim a resource is active or silently downgrade. A no-hint legacy request keeps its current path. Environment-only resource intent remains outside no-context execution: legacy/file-only results report external resource resolution as unchecked and cannot claim resource readiness or strict target verification. Every resource-aware invocation requires the explicit selector; its absence is never proof that an independent runtime has no resources.

## Verification order and output

1. Validate the context and obtain the authored configuration snapshot inside the selected host closure.
2. Discover participants and resolve resource references with the persistence-owned resolver, preserving host-owned module selection.
3. Establish the exact module set for this target invocation. A single actual connection must not receive modules belonging to a different target. The final resource selector and interaction with --modules / --from-host still need the authored-format contract.
4. Check provider and context constraints. Offline list/plan/script may validate references and report unresolved prerequisites, but do not look up expected connection values or claim a live target match.
5. For apply/validate/post-migrate, obtain the actual connection through the existing explicit env/stdin channel. Independently look up the selected resource's named connection in the chosen configuration snapshot. Missing/empty expectation or strict mismatch refuses before a context, connection or database command is created. Never use the actual input to fill a missing expectation.
6. Execute only after all applicable checks pass. Never reuse the expectation as a substitute for missing actual input.

Public evidence may contain context mode, environment, shell, resource/reference identities, checked provider/module identities and individual check outcomes. It must not contain raw paths, configuration snapshots, either connection value, connection hashes, or exception details that could contain them. Output must distinguish agreement with the supplied context from unverified parity with a separately running host. A successful context comparison is not a claim that migration, activation or data relocation succeeded.

Resource-context configuration reads, binding, expected lookup and reflection failures must terminate in typed, redacted refusals. Never concatenate an underlying JSON/provider/reflection exception message into output. The existing ToolingEntryPoint.InvokeAsync inner-exception message path is not safe evidence for the new context. Cover malformed files and environment-binding failures as well as ordinary missing/mismatched connections with canaries; do not rely on sanitizing only known successful-path values.

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

Independent design review approved retaining the Phase 0 proposal. It identified four readiness gates: no-context intent detection, resource/module selection, duplicate context authority and the configuration-adapter boundary. The latter two now have the selections above; the snapshot handoff and first two gates remain under review. Review also required the explicit exception-redaction rule above. No implementation readiness is claimed.

## Proposed reflected surface for contract review

Keep the existing legacy entry point. Add these overloads on EfToolingHost in the host's persistence assembly:

```csharp
public static EfToolingConfigurationContext CreateConfigurationContext(
    Stream request, CancellationToken cancellationToken);

public static Task<int> RunAsync(
    Stream request, Stream response,
    EfToolingConfigurationContext context, CancellationToken cancellationToken);
```

The factory reads a closed metadata-only JSON request containing contextVersion=1, source, hostDirectory, hostName, environment, shell and explicitSelection. The worker derives every field from its existing validated request; its new field supplies only the context version/source choice. Explicit resource execution requires one shell. A no-selector semantic probe may inspect all configured shells when the existing --shell selector is absent, preserving the legacy selection scope.

EfToolingConfigurationContext is a sealed host-owned IDisposable with no public configuration/secret projection and an internal constructor. Reflection verifies both exact method shapes and the context return/parameter type before creation. The worker never casts it to a configuration interface or serializes it. Disposal is idempotent, use after disposal refuses, and neither construction nor disposal may echo inner exception messages. Cancellation reaches the new entry points directly instead of being checked only after the reflected operation.

explicitSelection=false is used only for a file-context semantic probe prompted by a raw hint. Such a context accepts only the internal inspect-context command through the new overload; that command accepts the requested module selector but no actual Connection and performs no database or artifact work. If applicable resource intent is found, return configuration-context-required. Otherwise return a typed no-resource-applicable result, with unknowns retained as unresolved. Only that result permits the worker to execute its original legacy request through the unchanged entry point, preserving the legacy file projections and provider semantics. This is a proven non-applicable branch, never a fallback after unsupported capability, malformed context or unresolved applicability. It does not claim resource target verification.

Explicit mode resolves the context and follows the resource selection decision; every internal list and final operation uses that same context. Do not put command-specific resource/module selection or the actual Connection field in the context object; those stay on each operation request.

The context-aware operation request adds resource as a non-secret selector and retains the existing provider, schema, module selection and actual env/stdin-derived Connection fields where appropriate. It refuses legacy Shells/CapabilitySelection projections as duplicate configuration authority and derives them from the context instead. Host facts for scripts must match the context's canonical shell/environment and use the new checked-source explanation; they must not reuse the legacy file-only note for environment mode. Legacy entry-point requests carrying resource refuse instead of bypassing context verification.

This proposed surface and the host-default composer declaration must pass the final design review before Phase 1 contracts replace this proposal.
